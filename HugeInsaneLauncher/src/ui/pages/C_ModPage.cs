// HugeLauncher.Ui.Pages - 版本设置·Mod 管理页（子代理 C）：扫描/启停/删除/从文件安装
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    // 导航 key: instance:mod，NavParam = string 版本 id（可为 null）
    // 注：LocalMods.Scan/InstallFromFiles 传版本 id（= 游戏实际运行的实例目录，见 GameCore.Launch 的 InstanceDir(instanceName ?? id)）
    class PageInstanceMod : PageBase
    {
        string versionId;
        List<LocalMod> mods = new List<LocalMod>();
        StackPanel listHost;
        TextBlock countText;
        TextBlock stateText;
        bool loading;
        Border tabHost;   // 三级标签宿主（OnEnter 拿到 versionId 后才构建）

        public PageInstanceMod()
        {
            var root = new Grid();
            root.RowDefinitions.Add(UiHelpers.AutoRow());
            root.RowDefinitions.Add(UiHelpers.AutoRow());
            root.RowDefinitions.Add(UiHelpers.AutoRow());
            root.RowDefinitions.Add(UiHelpers.StarRow());

            // 顶部：返回 + 三级标签（概览/Mod/存档/设置）
            var topStack = new StackPanel();
            topStack.Margin = new Thickness(16, 16, 16, 0);
            var back = new FlatButton("← 返回");
            back.SetPadding(new Thickness(10, 5, 10, 5));
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 0, 0, 10);
            back.Click += delegate { Navigator.Go("instance:list"); };
            topStack.Children.Add(back);
            tabHost = new Border();
            topStack.Children.Add(tabHost);
            Grid.SetRow(topStack, 0);
            root.Children.Add(topStack);

            // 顶部操作行
            var bar = new StackPanel();
            bar.Orientation = Orientation.Horizontal;
            bar.Margin = new Thickness(16, 6, 16, 0);
            var btnAdd = new FlatButton("添加 Mod");
            btnAdd.Margin = new Thickness(0, 0, 8, 0);
            btnAdd.Click += OnAdd;
            var btnRefresh = new FlatButton("刷新");
            btnRefresh.Margin = new Thickness(0, 0, 8, 0);
            btnRefresh.Click += delegate { Refresh(); };
            var btnDownload = new FlatButton("下载 Mod");
            btnDownload.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.2), Palette.Dark, Palette.Dark);
            btnDownload.Click += delegate { Navigator.Go("download:mod"); };
            bar.Children.Add(btnAdd);
            bar.Children.Add(btnRefresh);
            bar.Children.Add(btnDownload);
            Grid.SetRow(bar, 1);
            root.Children.Add(bar);

            // 计数行
            countText = new TextBlock();
            countText.FontSize = 11;
            countText.Foreground = Palette.B(Palette.Dim);
            countText.Margin = new Thickness(16, 10, 16, 4);
            Grid.SetRow(countText, 2);
            root.Children.Add(countText);

            // Mod 列表
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Margin = new Thickness(16, 4, 16, 16);
            listHost = new StackPanel();
            scroll.Content = listHost;
            Grid.SetRow(scroll, 3);
            root.Children.Add(scroll);

            Children.Add(root);

            // 空态 / 加载态提示（居中覆盖）
            stateText = new TextBlock();
            stateText.FontSize = 12;
            stateText.Foreground = Palette.B(Palette.Dim);
            stateText.HorizontalAlignment = HorizontalAlignment.Center;
            stateText.VerticalAlignment = VerticalAlignment.Center;
            stateText.Margin = new Thickness(16);
            stateText.TextWrapping = TextWrapping.Wrap;
            stateText.TextAlignment = TextAlignment.Center;
            Children.Add(stateText);
        }

        public override void OnEnter()
        {
            versionId = NavParam as string;
            tabHost.Child = InstanceTabs.Build("mod", versionId);
            Refresh();
        }

        bool HasId()
        {
            if (string.IsNullOrEmpty(versionId)) { Toast("未选择版本"); return false; }
            return true;
        }

        void OnAdd(object sender, EventArgs e)
        {
            if (!HasId()) return;
            var dlg = new System.Windows.Forms.OpenFileDialog();
            dlg.Title = "选择要安装的 Mod";
            dlg.Filter = "Mod 文件 (*.jar)|*.jar";
            dlg.Multiselect = true;
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            try
            {
                int n = LocalMods.InstallFromFiles(versionId, dlg.FileNames);
                Toast(n > 0 ? "已安装 " + n + " 个 Mod" : "没有可安装的 .jar 文件");
            }
            catch (Exception ex)
            {
                Toast("安装失败: " + ex.Message);
            }
            Refresh();
        }

        void Refresh()
        {
            if (loading) return;
            loading = true;
            countText.Text = "";
            listHost.Children.Clear();
            stateText.Text = "正在加载 Mod 列表…";
            stateText.Visibility = Visibility.Visible;
            string vid = versionId;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<LocalMod> result = new List<LocalMod>();
                string err = null;
                try
                {
                    if (!string.IsNullOrEmpty(vid)) result = LocalMods.Scan(vid);
                }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    loading = false;
                    mods = result;
                    if (err != null) Toast("扫描 Mod 失败: " + err);
                    RenderList();
                }));
            });
        }

        void RenderList()
        {
            listHost.Children.Clear();
            if (string.IsNullOrEmpty(versionId))
            {
                stateText.Text = "未选择版本";
                stateText.Visibility = Visibility.Visible;
                countText.Text = "";
                return;
            }
            if (mods.Count == 0)
            {
                stateText.Text = "还没有 Mod，点击上方「下载 Mod」去社区挑选";
                stateText.Visibility = Visibility.Visible;
            }
            else
            {
                stateText.Visibility = Visibility.Collapsed;
            }
            UpdateCount();
            foreach (var m in mods) listHost.Children.Add(BuildModCard(m));
        }

        void UpdateCount()
        {
            if (string.IsNullOrEmpty(versionId)) { countText.Text = ""; return; }
            int en = 0;
            foreach (var m in mods) if (m.Enabled) en++;
            countText.Text = "共 " + mods.Count + " 个 Mod，启用 " + en + " 个";
        }

        UIElement BuildModCard(LocalMod m)
        {
            var card = Card();
            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            // 图标（无图标用工作台占位）
            var icon = new Image();
            icon.Width = 40;
            icon.Height = 40;
            icon.Margin = new Thickness(0, 0, 10, 0);
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
            string ip = m.IconPath;
            if (string.IsNullOrEmpty(ip) || !File.Exists(ip))
                ip = Paths.LauncherDir + "/Images/Blocks/CraftingTable.png";
            try { if (File.Exists(ip)) icon.Source = new BitmapImage(new Uri(ip, UriKind.Absolute)); }
            catch { }
            Grid.SetColumn(icon, 0);

            // 名称 + 版本 / 描述
            var texts = new StackPanel();
            texts.VerticalAlignment = VerticalAlignment.Center;
            var line1 = new TextBlock();
            line1.TextTrimming = TextTrimming.CharacterEllipsis;
            var r1 = new Run(m.DisplayName);
            r1.FontSize = 13;
            r1.Foreground = Palette.B(Palette.Text);
            line1.Inlines.Add(r1);
            if (!string.IsNullOrEmpty(m.Version))
            {
                var r2 = new Run("  " + m.Version);
                r2.FontSize = 11;
                r2.Foreground = Palette.B(Palette.Dim);
                line1.Inlines.Add(r2);
            }
            if (!m.Enabled) line1.Opacity = 0.55;
            var line2 = new TextBlock();
            line2.Text = string.IsNullOrEmpty(m.Description) ? m.FileName : m.Description;
            line2.FontSize = 11;
            line2.Foreground = Palette.B(Palette.Dim);
            line2.TextTrimming = TextTrimming.CharacterEllipsis;
            line2.Margin = new Thickness(0, 2, 0, 0);
            texts.Children.Add(line1);
            texts.Children.Add(line2);
            Grid.SetColumn(texts, 1);

            // 启用勾选
            var chk = new MiniCheckBox();
            chk.VerticalAlignment = VerticalAlignment.Center;
            chk.Margin = new Thickness(10, 0, 10, 0);
            chk.SetChecked(m.Enabled, false);   // 初始化不触发事件
            Grid.SetColumn(chk, 2);
            chk.Changed += delegate
            {
                try
                {
                    LocalMods.SetEnabled(m, chk.IsChecked);
                    line1.Opacity = m.Enabled ? 1.0 : 0.55;
                    UpdateCount();
                }
                catch (Exception ex)
                {
                    Toast("切换启用状态失败: " + ex.Message);
                    chk.SetChecked(m.Enabled, false);
                }
            };

            // 删除
            var del = new FlatButton("删除");
            del.SetColors(Palette.Btn, Palette.BtnHover, Palette.Red, Palette.Red);
            del.SetPadding(new Thickness(10, 5, 10, 5));
            del.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(del, 3);
            del.Click += delegate
            {
                if (MessageBox.Show("确定删除 Mod " + m.DisplayName + "？此操作不可恢复。", "Huge Insane Launcher",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                try
                {
                    LocalMods.Delete(m);
                    Toast("已删除 " + m.DisplayName);
                }
                catch (Exception ex)
                {
                    Toast("删除失败: " + ex.Message);
                }
                Refresh();
            };

            g.Children.Add(icon);
            g.Children.Add(texts);
            g.Children.Add(chk);
            g.Children.Add(del);
            card.Child = g;
            return card;
        }

        static Border Card()
        {
            var b = new Border();
            b.Background = Palette.B(Palette.Panel);
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(14);
            b.Margin = new Thickness(0, 0, 0, 8);
            return b;
        }
    }
}
