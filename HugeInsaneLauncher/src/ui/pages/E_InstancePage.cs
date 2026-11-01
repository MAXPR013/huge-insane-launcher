// HugeLauncher.Ui.Pages - 实例列表页（子代理 E）：左侧二级标签「已下载游戏 / 运行中游戏」+ 右侧卡片列表
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    // 导航 key: instance:list
    class PageInstanceList : PageBase
    {
        StackPanel tabHost;        // 左侧二级标签列
        Grid tabWrap;              // 标签列外壳（pill 压在文字层上）
        SquircleBorder localPill;  // 液态玻璃选中指示框（主窗口同款：活底多环+弹簧滑动）
        TranslateTransform localPillTf;
        Spring localPillSpring = new Spring(0) { Stiffness = 200, Damping = 16 };
        bool pillSnapped;          // 首次建脸时直接落位，不滑
        StackPanel listHost;       // 右侧卡片列表
        int curTab;                // 0=已下载游戏 1=运行中游戏
        int lastRunningCount = -1; // 上次渲染的运行中数量：变了才重建标签（保住选中态动画）
        DispatcherTimer tick;      // 停留本页时每秒刷新运行时长文字

        // 运行中卡片的副行 TextBlock 表，秒表只改文字不重建卡片
        List<KeyValuePair<RunningGames.Entry, TextBlock>> runTexts =
            new List<KeyValuePair<RunningGames.Entry, TextBlock>>();

        public PageInstanceList()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
            g.ColumnDefinitions.Add(UiHelpers.StarCol());

            tabWrap = new Grid();
            tabWrap.Margin = new Thickness(10, 16, 4, 16);
            tabWrap.VerticalAlignment = VerticalAlignment.Top;
            tabHost = new StackPanel();
            tabHost.Margin = new Thickness(2, 0, 8, 0);
            tabWrap.Children.Add(tabHost);
            // 玻璃选中 pill：活底透镜刷标签文字层，弹簧滑动（主窗口二级栏同款）
            localPill = new SquircleBorder();
            localPill.Height = 34;
            localPill.CornerRadius = new CornerRadius(100);
            localPill.VerticalAlignment = VerticalAlignment.Top;
            localPill.Margin = new Thickness(2, 0, 8, 0);
            localPill.IsHitTestVisible = false;
            localPillTf = new TranslateTransform(0, 0);
            localPill.RenderTransform = localPillTf;
            // pill 压在文字层上面（13 定稿结构）：透镜刷标签文字层，衬底挡真文字防重影
            tabWrap.Children.Add(localPill);
            Grid.SetColumn(tabWrap, 0);
            g.Children.Add(tabWrap);

            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Margin = new Thickness(6, 16, 16, 16);
            listHost = new StackPanel();
            scroll.Content = listHost;
            Grid.SetColumn(scroll, 1);
            g.Children.Add(scroll);

            Children.Add(g);
        }

        public override void OnEnter()
        {
            RunningGames.Changed += OnRunningChanged;
            Rebuild();
            // 玻璃脸等首帧渲染后再建（活底取标签层的实时画面）
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate { BuildLocalPillFace(); }));
            tick = new DispatcherTimer();
            tick.Interval = TimeSpan.FromSeconds(1);
            tick.Tick += delegate { RefreshRunTexts(); };
            tick.Start();
        }

        public override void OnLeave()
        {
            RunningGames.Changed -= OnRunningChanged;
            if (tick != null)
            {
                tick.Stop();
                tick = null;
            }
        }

        // RunningGames.Changed 在非 UI 线程触发，回 UI 线程重建
        void OnRunningChanged()
        {
            Dispatcher.BeginInvoke(new Action(delegate { Rebuild(); }));
        }

        void Rebuild()
        {
            // 运行中数量写进标签后缀，数量变了才重建标签
            int rn = RunningGames.Count;
            if (rn != lastRunningCount)
            {
                lastRunningCount = rn;
                BuildTabs(rn);
            }
            runTexts.Clear();
            listHost.Children.Clear();
            if (curTab == 0) BuildDownloadedList();
            else BuildRunningList();
        }

        void BuildTabs(int runningCount)
        {
            tabHost.Children.Clear();
            var t0 = new SideTabItem("已下载游戏");
            t0.Click += delegate { SwitchTab(0); };
            var t1 = new SideTabItem(runningCount > 0
                ? "运行中游戏 (" + runningCount + ")"
                : "运行中游戏");
            t1.Click += delegate { SwitchTab(1); };
            tabHost.Children.Add(t0);
            tabHost.Children.Add(t1);
            t0.SetSelected(curTab == 0);
            t1.SetSelected(curTab == 1);
            UpdateLocalPill(pillSnapped);
            BuildLocalPillFace();
        }

        void SwitchTab(int t)
        {
            curTab = t;
            // 标签可能刚被 Rebuild 重建过，这里统一按 curTab 重设选中态
            for (int i = 0; i < tabHost.Children.Count; i++)
            {
                var st = tabHost.Children[i] as SideTabItem;
                if (st != null) st.SetSelected(i == t);
            }
            UpdateLocalPill(true);
            Rebuild();
            // 切标签内容区淡入上移
            if (ThemeManager.AnimOn)
            {
                listHost.Opacity = 0;
                Ani.Ts(listHost).Y = 8;
                Ani.Start("instListSwap",
                    Ani.AaOpacity(listHost, 1, 120),
                    Ani.AaY(listHost, -8, 220, new AniEaseOutFluent(Ani.Weak)));
            }
        }

        // 玻璃 pill 滑动：动画走弹簧，首次/玻璃关时直接落位
        void UpdateLocalPill(bool animate)
        {
            if (localPill == null) return;
            if (!Glass.On)
            {
                localPill.Visibility = Visibility.Collapsed;
                return;
            }
            localPill.Visibility = Visibility.Visible;
            double target = curTab * 34.0;
            if (animate && ThemeManager.AnimOn)
            {
                SpringEngine.Go(localPillSpring, target, ApplyLocalPillSpring);
                pillSnapped = true;
            }
            else
            {
                localPillSpring.SnapTo(target);
                localPillTf.Y = localPillSpring.Value;
                pillSnapped = true;
            }
        }

        void ApplyLocalPillSpring(double v, double vel) { localPillTf.Y = v; }

        // 玻璃 pill 脸（活底多环 + 顶部淡高光）：与主窗口 BuildPillFace 同配方
        void BuildLocalPillFace()
        {
            if (!Glass.On || localPill == null) return;
            Grid host;
            var rings = Glass.LensRings(tabHost, 100, Glass.BandPx, out host, true);
            if (rings == null) { localPill.Background = Glass.SimulatedFace(0.08); return; }
            localPill.Background = null;
            var sheen = new SquircleBorder();
            sheen.Background = Glass.Sheen();
            sheen.CornerRadius = new CornerRadius(100);
            sheen.IsHitTestVisible = false;
            host.Children.Add(sheen);
            localPill.Child = rings;
        }

        // 卡片入场瀑布：25ms/项错落，淡入 + 左飘回弹（主窗口二级栏同款配方）
        void EnterAnim(UIElement el, int i)
        {
            if (!ThemeManager.AnimOn) return;
            int delay = Math.Min(i, 12) * 25;
            el.Opacity = 0;
            Ani.Ts(el).X = -16;
            Ani.Start("instCard" + el.GetHashCode(),
                Ani.AaOpacity(el, 1, 100, new AniEaseOutFluent(Ani.Weak), delay),
                Ani.AaX(el, 5, 200, new AniEaseOutFluent(Ani.Middle), delay),
                Ani.AaX(el, 11, 300, new AniEaseOutBack(Ani.Weak), delay));
        }

        // ---------- 已下载游戏 ----------

        void BuildDownloadedList()
        {
            List<string> versions;
            try { versions = GameCore.GetInstalledVersions(); }
            catch { versions = new List<string>(); }
            // 排序：启动过的在前（RecentLaunches 新→旧的天然顺序）；没启动过的按目录创建时间倒序（后下载的靠前）
            var recent = Cfg != null ? Cfg.RecentLaunches : null;
            versions.Sort(delegate(string a, string b)
            {
                int ia = recent != null ? recent.IndexOf(a) : -1;
                int ib = recent != null ? recent.IndexOf(b) : -1;
                if (ia >= 0 && ib >= 0) return ia.CompareTo(ib);
                if (ia >= 0) return -1;
                if (ib >= 0) return 1;
                DateTime ta = DateTime.MinValue, tb = DateTime.MinValue;
                try { ta = Directory.GetCreationTime(Paths.VersionDir(a)); } catch { }
                try { tb = Directory.GetCreationTime(Paths.VersionDir(b)); } catch { }
                return tb.CompareTo(ta);
            });
            if (versions.Count == 0)
                listHost.Children.Add(DimNote("还没有已下载的版本"));
            string selected = Cfg != null ? Cfg.VersionId : null;
            int ai = 0;
            foreach (var id in versions)
            {
                var c = BuildVersionCard(id, id == selected);
                listHost.Children.Add(c);
                EnterAnim(c, ai++);
            }

            // 末尾「去下载页装新版本」虚线感卡
            var add = new Border();
            add.BorderThickness = new Thickness(1);
            add.BorderBrush = Palette.B(Palette.Dim);
            add.Background = Palette.B(System.Windows.Media.Colors.Transparent);
            add.CornerRadius = new CornerRadius(10);
            add.Padding = new Thickness(12);
            add.Margin = new Thickness(0, 0, 0, 10);
            add.Cursor = Cursors.Hand;
            var addText = new TextBlock();
            addText.Text = "+ 去下载页装新版本";
            addText.FontSize = 12;
            addText.Foreground = Palette.B(Palette.Dim);
            addText.HorizontalAlignment = HorizontalAlignment.Center;
            add.Child = addText;
            add.MouseLeftButtonUp += delegate { Navigator.Go("download:install"); };
            listHost.Children.Add(add);
            EnterAnim(add, ai++);
        }

        UIElement BuildVersionCard(string id, bool isSelected)
        {
            var card = new Border();
            card.Background = Palette.B(Palette.Tile);
            card.CornerRadius = new CornerRadius(10);
            card.Padding = new Thickness(12);
            card.Margin = new Thickness(0, 0, 0, 10);
            if (isSelected)
            {
                // 当前选中的版本描主题色边
                card.BorderBrush = Palette.B(ThemeManager.CurrentColor);
                card.BorderThickness = new Thickness(1.5);
            }

            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            // 左：加载器图标
            var icon = new Image();
            icon.Width = 40;
            icon.Height = 40;
            icon.Margin = new Thickness(0, 0, 12, 0);
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
            string ip = Paths.LauncherDir + IconPathFor(id);
            try { if (File.Exists(ip)) icon.Source = new BitmapImage(new Uri(ip, UriKind.Absolute)); }
            catch { }
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            // 中：版本 id + 类型/状态行
            var mid = new StackPanel();
            mid.VerticalAlignment = VerticalAlignment.Center;
            var t1 = new TextBlock();
            t1.Text = id;
            t1.FontSize = 14;
            t1.FontWeight = FontWeights.SemiBold;
            t1.Foreground = Palette.B(Palette.Text);
            mid.Children.Add(t1);
            string sub = LoaderOf(id);
            try { if (Modrinth.GetInstanceLaunchId(id) != null) sub += " · 整合包"; } catch { }
            var t2 = new TextBlock();
            t2.FontSize = 11;
            t2.Foreground = Palette.B(Palette.Dim);
            t2.Margin = new Thickness(0, 3, 0, 0);
            t2.Text = sub;
            if (IsRunning(id))
            {
                var r = new System.Windows.Documents.Run(" · 正在运行");
                r.Foreground = Palette.B(Palette.Green);
                t2.Inlines.Add(r);
            }
            mid.Children.Add(t2);
            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);

            // 右：选择该版本（悬弹出提示条）+ 详情
            var btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.VerticalAlignment = VerticalAlignment.Center;
            var btnSel = new FlatButton("选择该版本");
            btnSel.MouseEnter += delegate { Toast("选中后，下次启动将使用该版本"); };
            btnSel.Click += delegate
            {
                if (Cfg != null)
                {
                    Cfg.VersionId = id;
                    Cfg.Save();
                    NotifyVersionSelected();   // 主窗口侧栏「当前版本」跟着刷
                }
                Toast("已选中 " + id + "，下次启动将使用该版本");
                Rebuild();
            };
            btns.Children.Add(btnSel);
            var btnDetail = new FlatButton("详情");
            btnDetail.Margin = new Thickness(8, 0, 0, 0);
            btnDetail.Click += delegate { Navigator.Go("instance:overall", id); };
            btns.Children.Add(btnDetail);
            Grid.SetColumn(btns, 2);
            g.Children.Add(btns);

            card.Child = g;
            return card;
        }

        // ---------- 运行中游戏 ----------

        void BuildRunningList()
        {
            var list = RunningGames.All();
            if (list.Count == 0)
            {
                listHost.Children.Add(DimNote("没有正在运行的游戏"));
                return;
            }
            int ai = 0;
            foreach (var e in list)
            {
                var c = BuildRunningCard(e);
                listHost.Children.Add(c);
                EnterAnim(c, ai++);
            }
        }

        UIElement BuildRunningCard(RunningGames.Entry e)
        {
            var card = new Border();
            card.Background = Palette.B(Palette.Tile);
            card.CornerRadius = new CornerRadius(10);
            card.Padding = new Thickness(12);
            card.Margin = new Thickness(0, 0, 0, 10);

            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            // 左：按实际启动版本的加载器取图标
            var icon = new Image();
            icon.Width = 40;
            icon.Height = 40;
            icon.Margin = new Thickness(0, 0, 12, 0);
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.NearestNeighbor);
            string ip = Paths.LauncherDir + IconPathFor(e.VersionId ?? "");
            try { if (File.Exists(ip)) icon.Source = new BitmapImage(new Uri(ip, UriKind.Absolute)); }
            catch { }
            Grid.SetColumn(icon, 0);
            g.Children.Add(icon);

            // 中：实例名 + 运行时长/PID 副行（秒表只刷这行文字）
            var mid = new StackPanel();
            mid.VerticalAlignment = VerticalAlignment.Center;
            var t1 = new TextBlock();
            t1.Text = e.InstanceName;
            t1.FontSize = 14;
            t1.FontWeight = FontWeights.SemiBold;
            t1.Foreground = Palette.B(Palette.Text);
            mid.Children.Add(t1);
            var t2 = new TextBlock();
            t2.FontSize = 11;
            t2.Foreground = Palette.B(Palette.Dim);
            t2.Margin = new Thickness(0, 3, 0, 0);
            t2.Text = RunSubText(e);
            runTexts.Add(new KeyValuePair<RunningGames.Entry, TextBlock>(e, t2));
            mid.Children.Add(t2);
            Grid.SetColumn(mid, 1);
            g.Children.Add(mid);

            // 右：关闭游戏 / 再次启动 / 详情
            var btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            btns.VerticalAlignment = VerticalAlignment.Center;
            var btnKill = new FlatButton("关闭游戏");
            btnKill.SetColors(Palette.Btn, Palette.BtnHover, Palette.Red, Palette.Red);
            btnKill.Click += delegate { RunningGames.Kill(e); };   // 退出后守护线程 Remove → Changed 自动刷新
            btns.Children.Add(btnKill);
            var btnRelaunch = new FlatButton("再次启动");
            btnRelaunch.Margin = new Thickness(8, 0, 0, 0);
            btnRelaunch.Click += delegate
            {
                // 「再次启动」= 再开一个，不是重启；同实例双开先打个招呼（存档/配置可能互踩）
                if (!e.Exited && MessageBox.Show(
                        "该实例正在运行中，再开一个会同时运行两个相同实例，存档和配置可能互相覆盖。\n\n确定再启动一个吗？",
                        "Huge Insane Launcher", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                    return;
                RequestLaunch(e.InstanceName);
            };
            btns.Children.Add(btnRelaunch);
            var btnDetail = new FlatButton("详情");
            btnDetail.Margin = new Thickness(8, 0, 0, 0);
            btnDetail.Click += delegate { Navigator.Go("instance:overall", e.VersionId); };
            btns.Children.Add(btnDetail);
            Grid.SetColumn(btns, 2);
            g.Children.Add(btns);

            card.Child = g;
            return card;
        }

        static string RunSubText(RunningGames.Entry e)
        {
            string pid;
            try { pid = e.Proc != null && !e.Proc.HasExited ? e.Proc.Id.ToString() : "-"; }
            catch { pid = "-"; }
            return e.RunText + " · PID " + pid;
        }

        void RefreshRunTexts()
        {
            if (curTab != 1) return;
            foreach (var kv in runTexts)
            {
                try { kv.Value.Text = RunSubText(kv.Key); } catch { }
            }
        }

        // ---------- 小工具 ----------

        // 该版本（或其同名实例）是否在运行中
        static bool IsRunning(string id)
        {
            try
            {
                foreach (var e in RunningGames.All())
                    if (e.VersionId == id || e.InstanceName == id) return true;
            }
            catch { }
            return false;
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

        static string LoaderOf(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("fabric")) return "Fabric";
            if (s.Contains("neoforge")) return "NeoForge";
            if (s.Contains("forge")) return "Forge";
            return "原版";
        }

        static string IconPathFor(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("fabric")) return "/Images/Loaders/Fabric.png";
            if (s.Contains("neoforge")) return "/Images/Loaders/NeoForge.png";
            if (s.Contains("forge")) return "/Images/Blocks/Anvil.png";
            return "/Images/Blocks/Grass.png";
        }
    }
}
