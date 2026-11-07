// HugeLauncher.Ui.Pages - 从其他启动器迁移：扫描 → 选择 → 导入（复制优先，原文件不动）
// 导航 key: settings:migrate
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    class PageMigrate : PageBase
    {
        StackPanel rootsPanel;
        StackPanel listPanel;
        TextBlock status;
        FlatButton importBtn;
        FlatButton scanBtn;
        SmoothScrollViewer scroll;

        readonly List<ScannedVersion> all = new List<ScannedVersion>();
        readonly HashSet<string> picked = new HashSet<string>();
        readonly Dictionary<string, MiniCheckBox> rootChecks = new Dictionary<string, MiniCheckBox>();
        readonly Dictionary<string, MiniCheckBox> verChecks = new Dictionary<string, MiniCheckBox>();
        string[] manualRoot;

        public PageMigrate()
        {
            RowDefinitions.Add(UiHelpers.AutoRow());
            RowDefinitions.Add(UiHelpers.StarRow());
            RowDefinitions.Add(UiHelpers.AutoRow());

            // 顶部：返回 + 标题 + 重新扫描
            var head = new Grid();
            head.Margin = new Thickness(16, 14, 16, 6);
            var back = new FlatButton("← 返回");
            back.SetFontSize(12);
            back.SetPadding(new Thickness(10, 4, 10, 4));
            back.SetCornerRadius(new CornerRadius(6));
            back.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Click += delegate { Navigator.Go("settings:game"); };
            head.Children.Add(back);

            var title = new TextBlock();
            title.Text = "从其他启动器迁移";
            title.FontSize = 16;
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(96, 0, 0, 0);
            title.Foreground = Palette.B(Palette.Text);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);

            scanBtn = new FlatButton("重新扫描");
            scanBtn.SetFontSize(12);
            scanBtn.SetPadding(new Thickness(12, 4, 12, 4));
            scanBtn.SetCornerRadius(new CornerRadius(6));
            scanBtn.HorizontalAlignment = HorizontalAlignment.Right;
            scanBtn.Click += delegate { StartScan(); };
            head.Children.Add(scanBtn);
            Children.Add(head);

            // 中间：滚动内容
            scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var body = new StackPanel();
            body.Margin = new Thickness(16, 0, 16, 8);
            scroll.Content = body;
            Grid.SetRow(scroll, 1);
            Children.Add(scroll);

            rootsPanel = new StackPanel();
            body.Children.Add(rootsPanel);

            var legend = new TextBlock();
            legend.Text = "找到的版本（按内容探测：读 json 的 id / inheritsFrom，不靠文件夹名）";
            legend.FontSize = 11;
            legend.Margin = new Thickness(2, 10, 0, 6);
            legend.Foreground = Palette.B(Palette.Dim);
            body.Children.Add(legend);

            listPanel = new StackPanel();
            body.Children.Add(listPanel);

            // 底部：状态 + 全选 + 导入
            var foot = new Grid();
            foot.Margin = new Thickness(16, 6, 16, 12);
            status = new TextBlock();
            status.FontSize = 11;
            status.Foreground = Palette.B(Palette.Dim);
            status.VerticalAlignment = VerticalAlignment.Center;
            foot.Children.Add(status);

            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            var allBtn = new FlatButton("全选");
            allBtn.SetFontSize(12);
            allBtn.SetPadding(new Thickness(12, 4, 12, 4));
            allBtn.SetCornerRadius(new CornerRadius(6));
            allBtn.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            allBtn.Click += delegate { SetAll(true); };
            right.Children.Add(allBtn);
            var noneBtn = new FlatButton("清空");
            noneBtn.SetFontSize(12);
            noneBtn.SetPadding(new Thickness(12, 4, 12, 4));
            noneBtn.SetCornerRadius(new CornerRadius(6));
            noneBtn.Margin = new Thickness(8, 0, 0, 0);
            noneBtn.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            noneBtn.Click += delegate { SetAll(false); };
            right.Children.Add(noneBtn);

            importBtn = new FlatButton("导入选中");
            importBtn.SetFontSize(12);
            importBtn.SetPadding(new Thickness(16, 5, 16, 5));
            importBtn.SetCornerRadius(new CornerRadius(6));
            importBtn.Margin = new Thickness(8, 0, 0, 0);
            importBtn.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.12), Palette.Dark, Palette.Dark);
            importBtn.Click += delegate { DoImport(); };
            right.Children.Add(importBtn);
            foot.Children.Add(right);
            Grid.SetRow(foot, 2);
            Children.Add(foot);
        }

        public override void OnEnter()
        {
            if (all.Count == 0) StartScan();
        }

        void StartScan()
        {
            if (all.Count > 0) return;   // 已在扫
            status.Text = "正在扫描…";
            scanBtn.IsEnabled = false;
            importBtn.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                    var found = new List<ScannedVersion>();
                var roots = new List<string[]>();
                try
                {
                    var candidates = LauncherScan.CandidateRoots();
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        var list = LauncherScan.ScanRoot(candidates[i][0], candidates[i][1]);
                        // HIL 自己的版本不用迁移，直接从列表里去掉
                        if (string.Equals(candidates[i][1], "HIL", StringComparison.OrdinalIgnoreCase)) continue;
                        if (list.Count == 0) continue;
                        roots.Add(candidates[i]);
                        found.AddRange(list);
                    }
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    all.Clear();
                    all.AddRange(found);
                    picked.Clear();
                    BuildRoots(roots);
                    BuildList();
                    scanBtn.IsEnabled = true;
                    importBtn.IsEnabled = true;
                    UpdateStatus();
                }));
            });
        }

        void BuildRoots(List<string[]> roots)
        {
            rootsPanel.Children.Clear();
            rootChecks.Clear();
            var card = new Border();
            card.Background = Palette.B(Palette.Panel);
            card.CornerRadius = new CornerRadius(8);
            card.Padding = new Thickness(14, 10, 14, 10);
            var sp = new StackPanel();
            card.Child = sp;
            if (roots.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "没找到别的启动器目录。可以把 .minecraft 拖进来，或用「手动指定」加一个路径。";
                none.FontSize = 12;
                none.TextWrapping = TextWrapping.Wrap;
                none.Foreground = Palette.B(Palette.Dim);
                sp.Children.Add(none);
            }
            for (int i = 0; i < roots.Count; i++)
            {
                string path = roots[i][0];
                string src = roots[i][1];
                var row = new Grid();
                row.Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0);
                row.ColumnDefinitions.Add(UiHelpers.AutoCol());
                row.ColumnDefinitions.Add(UiHelpers.StarCol());

                var chk = new MiniCheckBox();
                var existing = rootChecks;
                chk.VerticalAlignment = VerticalAlignment.Center;
                chk.Margin = new Thickness(0, 0, 10, 0);
                row.Children.Add(chk);

                var t = new TextBlock();
                t.Text = src + "  ·  " + path;
                t.FontSize = 12;
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                t.VerticalAlignment = VerticalAlignment.Center;
                t.Foreground = Palette.B(Palette.Text);
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                sp.Children.Add(row);
                rootChecks[path] = chk;
            }
            rootsPanel.Children.Add(card);
        }

        void BuildList()
        {
            listPanel.Children.Clear();
            verChecks.Clear();
            if (all.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "没有可迁移的版本。";
                none.FontSize = 12;
                none.Foreground = Palette.B(Palette.Dim);
                none.Margin = new Thickness(2, 4, 0, 0);
                listPanel.Children.Add(none);
                return;
            }

            var card = new Border();
            card.Background = Palette.B(Palette.Panel);
            card.CornerRadius = new CornerRadius(8);
            card.Padding = new Thickness(14, 8, 14, 8);
            var sp = new StackPanel();
            card.Child = sp;

            for (int i = 0; i < all.Count; i++)
            {
                ScannedVersion v = all[i];
                string key = v.Root + "|" + v.Dir;
                var row = new Grid();
                row.Margin = new Thickness(0, 4, 0, 4);
                row.ColumnDefinitions.Add(UiHelpers.AutoCol());
                row.ColumnDefinitions.Add(UiHelpers.StarCol());
                row.ColumnDefinitions.Add(UiHelpers.AutoCol());

                var chk = new MiniCheckBox();
                chk.VerticalAlignment = VerticalAlignment.Center;
                chk.Margin = new Thickness(0, 0, 10, 0);
                if (v.HasJar) chk.SetChecked(picked.Contains(key), false);
                else chk.IsEnabled = false;
                chk.Changed += delegate
                {
                    if (chk.IsChecked) picked.Add(key); else picked.Remove(key);
                    UpdateStatus();
                };
                row.Children.Add(chk);
                verChecks[key] = chk;

                var mid = new StackPanel();
                Grid.SetColumn(mid, 1);
                var name = new TextBlock();
                name.Text = v.Display;
                name.FontSize = 12.5;
                name.Foreground = Palette.B(v.HasJar ? Palette.Text : Palette.Dim);
                mid.Children.Add(name);
                var sub = new TextBlock();
                sub.Text = v.Source + "  ·  " + (v.HasJson ? (string.IsNullOrEmpty(v.BaseId) ? "独立版本" : "基于 " + v.BaseId) : "缺 json");
                sub.FontSize = 10.5;
                sub.Foreground = Palette.B(Palette.Dim);
                mid.Children.Add(sub);
                row.Children.Add(mid);

                var badge = new TextBlock();
                badge.Text = v.HasJar ? (v.Loader.Length > 0 ? v.Loader : "") : "缺 jar";
                badge.FontSize = 10.5;
                badge.VerticalAlignment = VerticalAlignment.Center;
                badge.Foreground = Palette.B(v.HasJar ? Palette.Dim : Palette.Red);
                Grid.SetColumn(badge, 2);
                row.Children.Add(badge);

                sp.Children.Add(row);
            }
            listPanel.Children.Add(card);
        }

        void SetAll(bool on)
        {
            picked.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                ScannedVersion v = all[i];
                if (!v.HasJar) continue;
                if (on) picked.Add(v.Root + "|" + v.Dir);
            }
            foreach (var kv in verChecks)
            {
                ScannedVersion v = Find(kv.Key);
                if (v != null && v.HasJar) kv.Value.SetChecked(on, false);
            }
            UpdateStatus();
        }

        ScannedVersion Find(string key)
        {
            for (int i = 0; i < all.Count; i++)
                if (all[i].Root + "|" + all[i].Dir == key) return all[i];
            return null;
        }

        void UpdateStatus()
        {
            int n = 0;
            for (int i = 0; i < all.Count; i++) if (picked.Contains(all[i].Root + "|" + all[i].Dir)) n++;
            status.Text = "共 " + all.Count + " 个版本，已选 " + n + " 个";
            importBtn.Text = n > 0 ? "导入选中 (" + n + ")" : "导入选中";
            importBtn.IsEnabled = n > 0;
        }

        void DoImport()
        {
            if (picked.Count == 0) return;
            var batch = new List<ScannedVersion>();
            for (int i = 0; i < all.Count; i++)
                if (picked.Contains(all[i].Root + "|" + all[i].Dir)) batch.Add(all[i]);

            importBtn.IsEnabled = false;
            scanBtn.IsEnabled = false;
            status.Text = "正在导入 0/" + batch.Count + "…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                int ok = 0, fail = 0;
                var log = new List<string>();
                for (int i = 0; i < batch.Count; i++)
                {
                    ScannedVersion v = batch[i];
                    int idx = i;
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        status.Text = "正在导入 " + (idx + 1) + "/" + batch.Count + "：" + v.Id;
                    }));
                    try
                    {
                        string newId = LauncherScan.UniqueId(v.Id);
                        LauncherScan.CopyVersion(v, newId, delegate(string m) { lock (log) log.Add(m); });
                        LauncherScan.CopyLibraries(v, delegate(string m) { lock (log) log.Add(m); });
                        ok++;
                    }
                    catch (Exception ex) { fail++; lock (log) log.Add(v.Id + "：" + ex.Message); }
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    importBtn.IsEnabled = true;
                    scanBtn.IsEnabled = true;
                    status.Text = "导入完成：成功 " + ok + " 个" + (fail > 0 ? "，失败 " + fail + " 个" : "") +
                                  "。游戏库文件缺失会在启动时自动补下。";
                    Toast("迁移完成：成功 " + ok + " 个" + (fail > 0 ? "，失败 " + fail : ""));
                    // 导入完刷新列表（目标已有同名目录，重名会加后缀）
                }));
            });
        }
    }
}
