// HugeLauncher.Ui.Pages - 版本设置·存档页（子代理 C）：存档列表 + 打开文件夹/备份/删除
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    // 导航 key: instance:saves，NavParam = string 版本 id（可为 null）
    // 存档目录 = Paths.InstanceDir(versionId) + "/saves"（实例隔离存储）
    class PageInstanceSaves : PageBase
    {
        string versionId;
        Border tabHost;      // 三级标签宿主（OnEnter 拿到 versionId 后才构建）
        StackPanel listHost;
        TextBlock countText;

        public PageInstanceSaves()
        {
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel();
            stack.Margin = new Thickness(16);
            scroll.Content = stack;
            Children.Add(scroll);

            // 顶部返回栏
            var back = new FlatButton("← 返回");
            back.SetPadding(new Thickness(10, 5, 10, 5));
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 0, 0, 10);
            back.Click += delegate { Navigator.Go("instance:list"); };
            stack.Children.Add(back);

            // 三级标签（概览/Mod/存档/设置）
            tabHost = new Border();
            stack.Children.Add(tabHost);

            // 操作行：左打开文件夹，右存档计数
            var bar = new Grid();
            bar.Margin = new Thickness(0, 0, 0, 10);
            bar.ColumnDefinitions.Add(UiHelpers.AutoCol());
            bar.ColumnDefinitions.Add(UiHelpers.StarCol());
            var btnOpen = new FlatButton("打开存档文件夹");
            btnOpen.Click += OnOpenFolder;
            Grid.SetColumn(btnOpen, 0);
            bar.Children.Add(btnOpen);
            countText = new TextBlock();
            countText.FontSize = 11;
            countText.Foreground = Palette.B(Palette.Dim);
            countText.VerticalAlignment = VerticalAlignment.Center;
            countText.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(countText, 1);
            bar.Children.Add(countText);
            stack.Children.Add(bar);

            // 存档卡列表
            listHost = new StackPanel();
            stack.Children.Add(listHost);
        }

        public override void OnEnter()
        {
            versionId = NavParam as string;
            tabHost.Child = InstanceTabs.Build("saves", versionId);
            Refresh();
        }

        string SavesDir()
        {
            return string.IsNullOrEmpty(versionId) ? null : Path.Combine(Paths.InstanceDir(versionId), "saves");
        }

        void Refresh()
        {
            listHost.Children.Clear();
            string dir = SavesDir();
            string[] saves = null;
            try
            {
                if (dir != null && Directory.Exists(dir)) saves = Directory.GetDirectories(dir);
            }
            catch { }
            if (saves == null || saves.Length == 0)
            {
                countText.Text = "";
                listHost.Children.Add(DimNote("还没有存档，进游戏创建一个世界吧"));
                return;
            }
            // 最近玩过的排前面
            Array.Sort(saves, delegate(string a, string b)
            {
                try { return Directory.GetLastWriteTime(b).CompareTo(Directory.GetLastWriteTime(a)); }
                catch { return 0; }
            });
            countText.Text = "共 " + saves.Length + " 个存档";
            foreach (var s in saves) listHost.Children.Add(BuildSaveCard(s));
        }

        UIElement BuildSaveCard(string dir)
        {
            string name = Path.GetFileName(dir);
            var card = new Border();
            card.Background = Palette.B(Palette.Tile);
            card.CornerRadius = new CornerRadius(10);
            card.Padding = new Thickness(12);
            card.Margin = new Thickness(0, 0, 0, 10);

            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            // 左：草地径图标
            var icon = new Image();
            icon.Width = 32;
            icon.Height = 32;
            icon.Margin = new Thickness(0, 0, 10, 0);
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
            string ip = Paths.LauncherDir + "/Images/Blocks/GrassPath.png";
            try { if (File.Exists(ip)) icon.Source = new BitmapImage(new Uri(ip, UriKind.Absolute)); }
            catch { }
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            // 中：存档名 + 最后玩时间/大小副行（大小后台线程算完回填）
            var mid = new StackPanel();
            mid.VerticalAlignment = VerticalAlignment.Center;
            var t1 = new TextBlock();
            t1.Text = name;
            t1.FontSize = 13;
            t1.FontWeight = FontWeights.SemiBold;
            t1.Foreground = Palette.B(Palette.Text);
            mid.Children.Add(t1);
            string time;
            try { time = Directory.GetLastWriteTime(dir).ToString("MM-dd HH:mm"); }
            catch { time = "未知"; }
            var t2 = new TextBlock();
            t2.FontSize = 11;
            t2.Foreground = Palette.B(Palette.Dim);
            t2.Margin = new Thickness(0, 3, 0, 0);
            t2.Text = "最后玩: " + time + " · 计算中…";
            mid.Children.Add(t2);
            string d = dir;
            string timeStr = time;
            TextBlock t2ref = t2;
            ThreadPool.QueueUserWorkItem(delegate
            {
                long bytes = DirSize(d);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try { t2ref.Text = "最后玩: " + timeStr + " · " + FormatSize(bytes); } catch { }
                }));
            });
            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);

            // 右：备份 / 删除
            var btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.VerticalAlignment = VerticalAlignment.Center;
            var btnBackup = new FlatButton("备份");
            btnBackup.Click += delegate { BackupSave(dir, name); };
            btns.Children.Add(btnBackup);
            var btnDel = new FlatButton("删除");
            btnDel.SetColors(Palette.Btn, Palette.BtnHover, Palette.Red, Palette.Red);
            btnDel.Margin = new Thickness(8, 0, 0, 0);
            btnDel.Click += delegate { DeleteSave(dir, name); };
            btns.Children.Add(btnDel);
            Grid.SetColumn(btns, 2);
            g.Children.Add(btns);

            card.Child = g;
            return card;
        }

        void OnOpenFolder(object sender, EventArgs e)
        {
            string dir = SavesDir();
            if (dir == null) { Toast("未选择版本"); return; }
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start("explorer.exe", dir);
            }
            catch (Exception ex) { Toast("打开失败: " + ex.Message); }
        }

        // 备份到 HugeLauncher/backups/<实例>/<存档>_yyyyMMdd_HHmmss.zip（后台线程压缩）
        void BackupSave(string dir, string name)
        {
            string zip = Path.Combine(Paths.LauncherDir, "backups", SafeName(versionId),
                SafeName(name) + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");
            string src = dir;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string err = null;
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(zip));
                    ZipFile.CreateFromDirectory(src, zip);
                }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (err == null) Toast("已备份到 HugeLauncher/backups/");
                    else Toast("备份失败: " + err);
                }));
            });
        }

        void DeleteSave(string dir, string name)
        {
            if (MessageBox.Show("删除存档 " + name + "？不可恢复", "Huge Insane Launcher",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                Directory.Delete(dir, true);
                Toast("已删除存档 " + name);
            }
            catch (Exception ex)
            {
                Toast("删除失败: " + ex.Message);
            }
            Refresh();
        }

        // ---------- 小工具 ----------

        // 递归目录大小（字节），异常静默按已累加值返回
        static long DirSize(string dir)
        {
            long total = 0;
            try
            {
                foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        static string FormatSize(long bytes)
        {
            if (bytes < 1024L * 1024) return (bytes / 1024) + " KB";
            return (bytes / 1048576.0).ToString("0.#") + " MB";
        }

        static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "unknown";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        static TextBlock DimNote(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 12;
            t.Foreground = Palette.B(Palette.Dim);
            t.Margin = new Thickness(4, 8, 4, 8);
            return t;
        }
    }
}
