// HugeLauncher.Ui - 桌面悬浮球：置顶透明窗口。收起=50px 主题色圆球（HIL 字）；
// 点 logo 向左伸出 500px 胶囊（贴左边框不够时向右镜像伸出），外端竖排三个小点
// 切换标签（H史聊天 / 下载进度 / 服务器日志），条身随标签换角色，上方 220px
// 透明区（点击穿透）显示气泡/控制台行。拖动 logo 移位，位置存配置。
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HugeLauncher.Ai;

namespace HugeLauncher.Ui
{
    // 管理器：唯一显隐真相——主窗可见则无球，主窗不可见（隐藏/最小化）则有球。
    // 所有状态变化（启动游戏/游戏全退/双击球/最小化/设置开关）都汇集到 Sync。
    static class FloatBall
    {
        public static FloatBallWindow Wnd;
        public static Action RestoreMainWindow;   // 双击球 → 切回全窗模式（主窗口接线）
        static bool mainVisible = true;
        static bool vanishing;
        static Config lastCfg;

        public static void SetMainVisible(bool v, Config cfg)
        {
            mainVisible = v;
            Sync(cfg);
        }

        public static void Sync(Config cfg)
        {
            if (cfg != null) lastCfg = cfg;
            if (cfg == null) cfg = lastCfg;
            if (cfg == null) return;
            bool want = cfg.FloatBallOn && !mainVisible;
            if (want)
            {
                if (vanishing) return;   // 消失动画跑完后 OnVanished 会再 Sync 一次
                if (Wnd == null)
                {
                    Wnd = new FloatBallWindow(cfg);
                    HilLog.Write("悬浮球 显示");
                }
                if (!Wnd.IsVisible)
                {
                    Wnd.Show();
                    Wnd.PlayAppear();
                }
            }
            else if (Wnd != null && !vanishing)
            {
                vanishing = true;
                var w = Wnd;
                Wnd = null;
                HilLog.Write("悬浮球 隐藏");
                w.PlayVanish();   // 动画收尾后自行 Close
            }
        }

        public static void OnVanished()
        {
            vanishing = false;
            Sync(lastCfg);   // 消失期间可能又来了显示请求
        }

        public static void Shutdown()
        {
            var w = Wnd;
            Wnd = null;
            if (w != null) { try { w.Close(); } catch { } }
        }
    }

    class FloatBallWindow : Window
    {
        const double StripH = 50;      // 胶囊条高（收起时的球直径）
        const double ContentH = 220;   // 上方透明内容区高
        const double BarW = 500;       // 展开后的胶囊宽
        const int TabChat = 0, TabDownload = 1, TabServer = 2, TabLaunch = 3;

        readonly Config cfg;
        Border capsule;                // 主题色渐变胶囊（宽 50↔500 动画）
        LinearGradientBrush capsuleBrush;
        Grid strip;                    // 底部条（出现/消失动画的缩放目标）
        Grid barHost;                  // 胶囊内内容宿主（收起时透明且不响应点击）
        Grid barContent;               // 条身内容区（避开小点区与 logo 区）
        StackPanel dotsPanel;          // 三个小点（竖排，无外框）
        Grid logoZone;                 // 50×50 锚点区（exe 图标 + 拖动/单击/双击）
        Image logoImg;
        DispatcherTimer clickTimer;    // 单击延迟判定（给双击让路）
        readonly Ellipse[] dots = new Ellipse[4];
        readonly SolidColorBrush[] dotBrushes = new SolidColorBrush[4];
        readonly Grid[] bars = new Grid[4];
        Grid contentGrid;              // 上方透明区
        Grid consoleHost;              // 服务器控制台行宿主
        StackPanel consoleStack;
        Border chatBubble;             // H史气泡（主题色底白字 + ✕）
        TextBlock chatBubbleText;
        TextBlock chatBubbleClose;
        TextBlock dlName, dlPct;       // 下载页：名称 + 百分比/速度
        Border dlFill;                 // 进度条填充
        Grid dlTrack;
        TextBox chatInput, serverInput;
        TextBlock chatHint, serverHint;
        Border[] launchCells = new Border[3];       // 快速启动页：三个最近版本格（横排）
        TextBlock[] launchNames = new TextBlock[3];
        SolidColorBrush[] launchCellBg = new SolidColorBrush[3];
        SolidColorBrush[] launchCellFg = new SolidColorBrush[3];
        TextBlock launchEmpty;                      // 「还没启动过游戏」占位提示
        Border launchBtn;
        SolidColorBrush launchBtnBg;
        TextBlock launchBtnText;
        int launchSel;                              // 快速启动页当前选中格
        Color fbFg = Colors.White;                  // RefreshContrast 算好的对比色，启动格复用
        int tab = TabDownload;
        bool expanded;
        bool mirror;                   // true=向右展开（logo 在左端）
        bool dragging, dragMoved;
        Point grabScreen;
        double startLeft, startTop;
        double centerX, centerY;       // logo 球心屏幕坐标
        bool hadActiveTasks;
        bool bubbleVisible;
        string currentServer;
        DateTime lastDlUi = DateTime.MinValue;

        // 事件 handler 存根，OnClosed 时统一退订（防设置页关球后事件挂着死窗口）
        EventHandler hColor;
        Action hTask;
        Action<string, string> hOutput;
        Action<string, int, bool> hExited;
        Action<string> hState, hReply, hError;

        public FloatBallWindow(Config c)
        {
            cfg = c;
            Width = BarW;
            Height = ContentH + StripH;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = null;         // 空画刷区域点击穿透（Transparent 画刷反而会截点击）
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            BuildUi();
            WireEvents();
            InitPosition();
            SelectTab(TabDownload);
            UpdateDownload(true);
        }

        // ============ UI 构建 ============
        void BuildUi()
        {
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(ContentH) });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(StripH) });

            // ---- 上方透明内容区（背景 null = 点击穿透；只有气泡/文字行本体可交互）----
            contentGrid = new Grid();
            contentGrid.Margin = new Thickness(4, 0, 4, 6);
            contentGrid.Opacity = 0;
            contentGrid.IsHitTestVisible = false;
            Grid.SetRow(contentGrid, 0);
            root.Children.Add(contentGrid);

            // 服务器控制台行栈（每行 45% 黑底小胶囊衬底，保证任何桌面底色上可读）
            consoleHost = new Grid();
            consoleHost.VerticalAlignment = VerticalAlignment.Bottom;
            consoleHost.ClipToBounds = true;
            consoleStack = new StackPanel();
            consoleHost.Children.Add(consoleStack);
            consoleHost.Visibility = Visibility.Collapsed;
            contentGrid.Children.Add(consoleHost);

            // H史气泡
            chatBubble = new Border();
            chatBubble.CornerRadius = new CornerRadius(14);
            chatBubble.Padding = new Thickness(12, 8, 12, 8);
            chatBubble.VerticalAlignment = VerticalAlignment.Bottom;
            chatBubble.Background = ThemeManager.Accent;   // 共享画刷，换主题自动跟随
            chatBubble.Visibility = Visibility.Collapsed;
            var bdp = new DockPanel();
            chatBubbleClose = new TextBlock();
            chatBubbleClose.Text = "✕";
            chatBubbleClose.FontSize = 11;
            chatBubbleClose.VerticalAlignment = VerticalAlignment.Top;
            chatBubbleClose.Margin = new Thickness(8, 0, 0, 0);
            chatBubbleClose.Cursor = Cursors.Hand;
            DockPanel.SetDock(chatBubbleClose, Dock.Right);
            chatBubbleClose.MouseLeftButtonUp += delegate { CloseBallBubble(); };
            chatBubbleText = new TextBlock();
            chatBubbleText.FontSize = 12;
            chatBubbleText.TextWrapping = TextWrapping.Wrap;
            bdp.Children.Add(chatBubbleClose);
            bdp.Children.Add(chatBubbleText);
            chatBubble.Child = bdp;
            contentGrid.Children.Add(chatBubble);

            // ---- 底部条 ----
            strip = new Grid();
            strip.Height = StripH;
            strip.VerticalAlignment = VerticalAlignment.Bottom;
            Grid.SetRow(strip, 1);
            root.Children.Add(strip);

            capsuleBrush = new LinearGradientBrush();
            capsuleBrush.StartPoint = new Point(0, 0);
            capsuleBrush.EndPoint = new Point(1, 1);
            RefreshGradient();
            capsule = new Border();
            capsule.Height = StripH;
            capsule.Width = StripH;
            capsule.CornerRadius = new CornerRadius(StripH / 2);
            capsule.Background = capsuleBrush;
            strip.Children.Add(capsule);

            barHost = new Grid();
            barHost.Opacity = 0;
            barHost.IsHitTestVisible = false;
            capsule.Child = barHost;

            // 四个小点：竖排画在胶囊底上，选中=对比色实心，其余 40% 透明
            dotsPanel = new StackPanel();
            dotsPanel.Orientation = Orientation.Vertical;
            dotsPanel.Width = 26;
            dotsPanel.VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < 4; i++)
            {
                var br = new SolidColorBrush(Color.FromArgb(102, 255, 255, 255));
                var el = new Ellipse();
                el.Width = 6;
                el.Height = 6;
                el.Margin = new Thickness(0, 2, 0, 2);   // 4 个点要塞进 50px 胶囊，间距从 4 压到 2
                el.Fill = br;
                el.Cursor = Cursors.Hand;
                int idx = i;
                el.MouseLeftButtonUp += delegate { SelectTab(idx); if (!expanded) SetExpanded(true); };
                dots[i] = el;
                dotBrushes[i] = br;
                dotsPanel.Children.Add(el);
            }
            dotsPanel.MouseWheel += delegate(object s, MouseWheelEventArgs e)
            {
                int next = e.Delta > 0 ? tab - 1 : tab + 1;
                if (next < 0) next = 3;
                if (next > 3) next = 0;
                SelectTab(next);
                e.Handled = true;
            };
            barHost.Children.Add(dotsPanel);

            barContent = new Grid();
            barHost.Children.Add(barContent);

            BuildChatBar();
            BuildDownloadBar();
            BuildServerBar();
            BuildLaunchBar();

            // logo 锚点区（不参与胶囊宽度，永远钉在锚端）
            // 注意：Transparent 画刷必须有——null 背景的 Grid 不做命中测试，点击会穿透
            logoZone = new Grid();
            logoZone.Width = StripH;
            logoZone.Height = StripH;
            logoZone.Cursor = Cursors.Hand;
            logoZone.Background = Brushes.Transparent;
            // logo = 史蒂夫方脸鱼眼变形圆形图（从内嵌 steve.png 皮肤裁正脸生成）；
            // 失败兜底 = exe 内嵌图标
            logoImg = new Image();
            logoImg.Width = 34;
            logoImg.Height = 34;
            logoImg.Stretch = Stretch.Uniform;
            logoImg.IsHitTestVisible = false;
            var steveLogo = BuildSteveLogo();
            if (steveLogo != null)
            {
                RenderOptions.SetBitmapScalingMode(logoImg, BitmapScalingMode.NearestNeighbor);
                logoImg.Source = steveLogo;
            }
            else
            {
                try
                {
                    using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(
                        System.Reflection.Assembly.GetExecutingAssembly().Location))
                    {
                        if (ico != null)
                            logoImg.Source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                ico.Handle, Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                    }
                }
                catch { }
            }
            logoZone.Children.Add(logoImg);
            strip.Children.Add(logoZone);

            clickTimer = new DispatcherTimer();
            clickTimer.Interval = TimeSpan.FromMilliseconds(280);
            clickTimer.Tick += delegate
            {
                clickTimer.Stop();
                SetExpanded(!expanded);   // 单击：展开/收起简化面板
            };

            // 拖动 / 单击（简化面板）/ 双击（切回全窗）
            logoZone.MouseLeftButtonDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (e.ClickCount >= 2)
                {
                    // 双击：取消单击判定，切回全窗模式
                    clickTimer.Stop();
                    dragging = false;
                    SetExpanded(false);
                    var r = FloatBall.RestoreMainWindow;
                    if (r != null) r();
                    return;
                }
                dragging = true;
                dragMoved = false;
                grabScreen = PointToScreen(e.GetPosition(this));
                startLeft = Left;
                startTop = Top;
                logoZone.CaptureMouse();
            };
            logoZone.MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!dragging || e.LeftButton != MouseButtonState.Pressed) return;
                var sp = PointToScreen(e.GetPosition(this));
                double dx = sp.X - grabScreen.X, dy = sp.Y - grabScreen.Y;
                if (!dragMoved && Math.Abs(dx) < 6 && Math.Abs(dy) < 6) return;
                dragMoved = true;
                Left = startLeft + dx;
                Top = startTop + dy;
            };
            logoZone.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                if (!dragging) return;
                dragging = false;
                logoZone.ReleaseMouseCapture();
                e.Handled = true;
                if (!dragMoved)
                {
                    clickTimer.Stop();
                    clickTimer.Start();   // 等 280ms：没来第二击才算单击
                    return;
                }
                // 落点：换算球心 → 钳位 → 定镜像 → 存配置
                centerX = Left + (mirror ? StripH / 2 : BarW - StripH / 2);
                centerY = Top + ContentH + StripH / 2;
                ClampCenter();
                bool nm = centerX - BarW / 2 < SystemParameters.WorkArea.Left + 6;
                if (nm != mirror) { mirror = nm; ApplyMirror(); }
                ApplyPosition();
                cfg.FloatBallX = centerX;
                cfg.FloatBallY = centerY;
                cfg.Save();
            };

            Content = root;
            RefreshContrast();
        }

        TextBox MakePillInput(out TextBlock hintOut, string hint)
        {
            var pill = new Border();
            pill.CornerRadius = new CornerRadius(15);
            pill.Height = 30;
            pill.VerticalAlignment = VerticalAlignment.Center;
            pill.Background = new SolidColorBrush(Color.FromArgb(56, 255, 255, 255));
            var g = new Grid();
            var box = new TextBox();
            box.Background = Brushes.Transparent;
            box.BorderThickness = new Thickness(0);
            box.FontSize = 12;
            box.Padding = new Thickness(10, 0, 10, 0);
            box.VerticalContentAlignment = VerticalAlignment.Center;
            var hintT = new TextBlock();
            hintT.Text = hint;
            hintT.FontSize = 12;
            hintT.Margin = new Thickness(11, 0, 0, 0);
            hintT.VerticalAlignment = VerticalAlignment.Center;
            hintT.IsHitTestVisible = false;
            g.Children.Add(box);
            g.Children.Add(hintT);
            pill.Child = g;
            box.GotFocus += delegate { hintT.Visibility = Visibility.Collapsed; };
            box.LostFocus += delegate { if (box.Text.Length == 0) hintT.Visibility = Visibility.Visible; };
            box.TextChanged += delegate { hintT.Visibility = box.Text.Length == 0 && !box.IsFocused ? Visibility.Visible : Visibility.Collapsed; };
            hintOut = hintT;
            barPillHolder.Children.Add(pill);
            return box;
        }

        Grid barPillHolder;   // 每个 bar 构建前指向自己，MakePillInput 往里塞输入框

        void BuildChatBar()
        {
            var g = new Grid();
            barPillHolder = g;
            chatInput = MakePillInput(out chatHint, "和 H史 聊聊…（回车发送）");
            chatInput.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                SendChat();
            };
            g.Visibility = Visibility.Collapsed;
            bars[TabChat] = g;
            barContent.Children.Add(g);
        }

        void BuildDownloadBar()
        {
            var g = new Grid();
            g.VerticalAlignment = VerticalAlignment.Center;
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            var top = new Grid();
            dlName = new TextBlock();
            dlName.FontSize = 12;
            dlName.TextTrimming = TextTrimming.CharacterEllipsis;
            dlName.Margin = new Thickness(2, 0, 76, 0);
            dlPct = new TextBlock();
            dlPct.FontSize = 12;
            dlPct.FontWeight = FontWeights.Bold;
            dlPct.HorizontalAlignment = HorizontalAlignment.Right;
            top.Children.Add(dlName);
            top.Children.Add(dlPct);
            Grid.SetRow(top, 0);
            g.Children.Add(top);
            dlTrack = new Grid();
            dlTrack.Height = 4;
            dlTrack.Margin = new Thickness(2, 5, 2, 0);
            var trackBg = new Border();
            trackBg.CornerRadius = new CornerRadius(2);
            trackBg.Background = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255));
            dlTrack.Children.Add(trackBg);
            dlFill = new Border();
            dlFill.CornerRadius = new CornerRadius(2);
            dlFill.HorizontalAlignment = HorizontalAlignment.Left;
            dlFill.Width = 0;
            dlTrack.Children.Add(dlFill);
            Grid.SetRow(dlTrack, 1);
            g.Children.Add(dlTrack);
            bars[TabDownload] = g;
            barContent.Children.Add(g);
        }

        void BuildServerBar()
        {
            var g = new Grid();
            barPillHolder = g;
            serverInput = MakePillInput(out serverHint, "服务器命令…（回车发送）");
            serverInput.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key != Key.Enter) return;
                e.Handled = true;
                SendServerCmd();
            };
            g.Visibility = Visibility.Collapsed;
            bars[TabServer] = g;
            barContent.Children.Add(g);
        }

        // 快速启动页：最近启动的 3 个版本横排 + 一个启动 pill；点格=选中，点启动=直接开游戏
        void BuildLaunchBar()
        {
            var g = new Grid();
            g.VerticalAlignment = VerticalAlignment.Center;
            for (int i = 0; i < 3; i++) g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            for (int i = 0; i < 3; i++)
            {
                var bg = new SolidColorBrush(Color.FromArgb(0, 255, 255, 255));
                var fg = new SolidColorBrush(Colors.White);
                var cell = new Border();
                cell.Height = 30;
                cell.CornerRadius = new CornerRadius(15);
                cell.Background = bg;
                cell.Margin = new Thickness(2, 0, 2, 0);
                cell.Cursor = Cursors.Hand;
                var tb = new TextBlock();
                tb.FontSize = 12;
                tb.Foreground = fg;
                tb.TextTrimming = TextTrimming.CharacterEllipsis;   // 写不下就 …
                tb.HorizontalAlignment = HorizontalAlignment.Center;
                tb.VerticalAlignment = VerticalAlignment.Center;
                tb.Margin = new Thickness(8, 0, 8, 0);
                cell.Child = tb;
                int idx = i;
                cell.MouseLeftButtonUp += delegate
                {
                    if (idx >= cfg.RecentLaunches.Count) return;
                    launchSel = idx;
                    RefreshLaunchCells();
                };
                Grid.SetColumn(cell, i);
                g.Children.Add(cell);
                launchCells[i] = cell;
                launchNames[i] = tb;
                launchCellBg[i] = bg;
                launchCellFg[i] = fg;
            }
            // 启动 pill：实心对比色底，文字色在 RefreshContrast 里跟主题算
            launchBtnBg = new SolidColorBrush(Colors.White);
            launchBtn = new Border();
            launchBtn.Height = 30;
            launchBtn.CornerRadius = new CornerRadius(15);
            launchBtn.Background = launchBtnBg;
            launchBtn.Margin = new Thickness(6, 0, 2, 0);
            launchBtn.Cursor = Cursors.Hand;
            launchBtnText = new TextBlock();
            launchBtnText.Text = "▶ 启动";
            launchBtnText.FontSize = 12;
            launchBtnText.FontWeight = FontWeights.Bold;
            launchBtnText.Margin = new Thickness(12, 0, 12, 0);
            launchBtnText.VerticalAlignment = VerticalAlignment.Center;
            launchBtn.Child = launchBtnText;
            launchBtn.MouseLeftButtonUp += delegate { LaunchSelected(); };
            Grid.SetColumn(launchBtn, 3);
            g.Children.Add(launchBtn);
            // 空历史占位
            launchEmpty = new TextBlock();
            launchEmpty.Text = "还没启动过游戏，先去主窗启动一次吧";
            launchEmpty.FontSize = 12;
            launchEmpty.HorizontalAlignment = HorizontalAlignment.Center;
            launchEmpty.VerticalAlignment = VerticalAlignment.Center;
            launchEmpty.IsHitTestVisible = false;
            Grid.SetColumnSpan(launchEmpty, 4);
            g.Children.Add(launchEmpty);
            g.Visibility = Visibility.Collapsed;
            bars[TabLaunch] = g;
            barContent.Children.Add(g);
        }

        // 每次切到本页重读历史（主窗启动新版本后会变）
        void RefreshLaunchBar()
        {
            int cnt = cfg.RecentLaunches.Count;
            bool any = cnt > 0;
            launchEmpty.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 0; i < 3; i++)
            {
                string name = i < cnt ? cfg.RecentLaunches[i] : null;
                launchNames[i].Text = name ?? "";
                launchNames[i].ToolTip = name;   // 省略号时悬停看全名
                launchCells[i].Visibility = name != null ? Visibility.Visible : Visibility.Hidden;
            }
            launchBtn.Visibility = any ? Visibility.Visible : Visibility.Hidden;
            if (launchSel >= cnt) launchSel = 0;
            RefreshLaunchCells();
        }

        void RefreshLaunchCells()
        {
            for (int i = 0; i < 3; i++)
            {
                bool sel = i == launchSel;
                launchCellBg[i].Color = sel
                    ? Color.FromArgb(72, fbFg.R, fbFg.G, fbFg.B)
                    : Color.FromArgb(0, fbFg.R, fbFg.G, fbFg.B);
                launchCellFg[i].Color = sel ? fbFg : Color.FromArgb(178, fbFg.R, fbFg.G, fbFg.B);
            }
        }

        void LaunchSelected()
        {
            if (launchSel >= cfg.RecentLaunches.Count) return;
            string name = cfg.RecentLaunches[launchSel];
            SetExpanded(false);   // 收起面板，别挡游戏窗口
            Pages.PageBase.RequestLaunch(name);   // 走主窗 LaunchGame：整合包/每版本设置都生效
        }

        // ============ 事件接线 ============
        void WireEvents()
        {
            hColor = delegate { RefreshGradient(); RefreshContrast(); };
            ThemeManager.ColorChanged += hColor;

            hTask = delegate { Dispatcher.BeginInvoke(new Action(delegate { UpdateDownload(false); })); };
            TaskCenter.Changed += hTask;

            hOutput = delegate(string name, string line)
            { Dispatcher.BeginInvoke(new Action(delegate { AppendConsole(name, line); })); };
            ServerManager.Output += hOutput;

            hExited = delegate(string name, int code, bool userStop)
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (!userStop && code != 0) Pulse();
                }));
            };
            ServerManager.Exited += hExited;

            hState = delegate(string name)
            { Dispatcher.BeginInvoke(new Action(delegate { EnsureServer(false); })); };
            ServerManager.StateChanged += hState;

            hReply = delegate(string text)
            { Dispatcher.BeginInvoke(new Action(delegate { OnAiText(text); })); };
            AiSteve.ReplyReceived += hReply;

            hError = delegate(string msg)
            { Dispatcher.BeginInvoke(new Action(delegate { OnAiText("H史：" + msg); })); };
            AiSteve.ErrorOccurred += hError;
        }

        protected override void OnClosed(EventArgs e)
        {
            ThemeManager.ColorChanged -= hColor;
            TaskCenter.Changed -= hTask;
            ServerManager.Output -= hOutput;
            ServerManager.Exited -= hExited;
            ServerManager.StateChanged -= hState;
            AiSteve.ReplyReceived -= hReply;
            AiSteve.ErrorOccurred -= hError;
            if (FloatBall.Wnd == this) FloatBall.Wnd = null;
            base.OnClosed(e);
        }

        // ============ 位置 ============
        void InitPosition()
        {
            var wa = SystemParameters.WorkArea;
            if (cfg.FloatBallX >= 0 && cfg.FloatBallY >= 0)
            {
                centerX = cfg.FloatBallX;
                centerY = cfg.FloatBallY;
            }
            else
            {
                centerX = wa.Right - 42;
                centerY = wa.Bottom - 42;
            }
            ClampCenter();
            mirror = centerX - BarW / 2 < wa.Left + 6;
            ApplyMirror();
            ApplyPosition();
        }

        void ClampCenter()
        {
            var wa = SystemParameters.WorkArea;
            // 内容区在条的上方展开，顶部必须留足 ContentH，否则气泡/控制台会顶出屏幕
            if (centerX < wa.Left + 28) centerX = wa.Left + 28;
            if (centerX > wa.Right - 28) centerX = wa.Right - 28;
            if (centerY < wa.Top + ContentH + 28) centerY = wa.Top + ContentH + 28;
            if (centerY > wa.Bottom - 28) centerY = wa.Bottom - 28;
        }

        void ApplyPosition()
        {
            Top = centerY - ContentH - StripH / 2;
            Left = mirror ? centerX - StripH / 2 : centerX + StripH / 2 - BarW;
        }

        void ApplyMirror()
        {
            capsule.HorizontalAlignment = mirror ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            logoZone.HorizontalAlignment = mirror ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            dotsPanel.HorizontalAlignment = mirror ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            barContent.Margin = mirror ? new Thickness(52, 0, 28, 0) : new Thickness(28, 0, 52, 0);
        }

        // ============ 展开 / 收起 ============
        void SetExpanded(bool on)
        {
            if (expanded == on) return;
            expanded = on;
            Ani.Stop("fb");
            Ani.Stop("fbc");
            if (on)
            {
                barHost.IsHitTestVisible = true;
                contentGrid.IsHitTestVisible = true;
                Ani.Start("fb",
                    Ani.Aa(delegate(double d) { capsule.Width += d; }, BarW - capsule.Width, 380, new AniEaseOutBack(Ani.Weak)),
                    Ani.AaOpacity(barHost, 1 - barHost.Opacity, 150, new AniEaseOutFluent(Ani.Weak), 150));
                Ani.Start("fbc", Ani.AaOpacity(contentGrid, 1 - contentGrid.Opacity, 170, new AniEaseOutFluent(Ani.Weak), 130));
            }
            else
            {
                barHost.IsHitTestVisible = false;
                Ani.Start("fb",
                    Ani.AaOpacity(barHost, -barHost.Opacity, 90, new AniEaseInFluent(Ani.Weak)),
                    Ani.Aa(delegate(double d) { capsule.Width += d; }, StripH - capsule.Width, 250, new AniEaseInFluent(Ani.Weak), 60));
                Ani.Start("fbc",
                    Ani.AaOpacity(contentGrid, -contentGrid.Opacity, 110, new AniEaseInFluent(Ani.Weak)),
                    Ani.AaCode(delegate { contentGrid.IsHitTestVisible = false; }));
            }
        }

        // 待机提醒：收起态下呼吸脉冲两轮
        void Pulse()
        {
            if (expanded || !IsVisible || Ani.IsRun("fbpulse")) return;
            Ani.Start("fbpulse",
                Ani.AaOpacity(capsule, -0.55, 240),
                Ani.AaOpacity(capsule, 0.55, 300, null, 0, true),
                Ani.AaOpacity(capsule, -0.55, 240, null, 0, true),
                Ani.AaOpacity(capsule, 0.55, 300, null, 0, true));
        }

        // ============ 标签切换 ============
        void SelectTab(int i)
        {
            bool changed = i != tab;
            tab = i;
            bool light = IsLightTheme();
            Color on = light ? Color.FromRgb(0x22, 0x24, 0x2A) : Colors.White;
            Ani.Stop("fbtab");
            Ani.Stop("fbcontent");
            for (int k = 0; k < 4; k++)
            {
                Color to = k == i ? on : Color.FromArgb(102, on.R, on.G, on.B);
                Ani.Start("fbdot" + k, Ani.AaColor(dotBrushes[k], to, 120));
                bool sel = k == i;
                // 未选中的复位（上次切换动画可能半途被停，留下半截透明度/位移）
                if (!sel)
                {
                    bars[k].Visibility = Visibility.Collapsed;
                    bars[k].Opacity = 1;
                    Ani.Ts(bars[k]).Y = 0;
                }
            }
            var nb = bars[i];
            nb.Visibility = Visibility.Visible;
            if (changed && expanded)
            {
                // 条身换角：新内容淡入 + 上滑回弹（主窗换页配方缩小版）
                nb.Opacity = 0;
                var tf = Ani.Ts(nb);
                tf.Y = 6;
                Ani.Start("fbtab",
                    Ani.AaOpacity(nb, 1, 130, new AniEaseOutFluent(Ani.Weak)),
                    Ani.AaY(nb, -6, 220, new AniEaseOutBack(Ani.Weak)));
            }
            else
            {
                nb.Opacity = 1;
                Ani.Ts(nb).Y = 0;
            }
            // 内容区同步换角
            consoleHost.Visibility = i == TabServer ? Visibility.Visible : Visibility.Collapsed;
            chatBubble.Visibility = i == TabChat && bubbleVisible ? Visibility.Visible : Visibility.Collapsed;
            FrameworkElement incoming = i == TabServer ? (FrameworkElement)consoleHost
                : (i == TabChat && bubbleVisible ? (FrameworkElement)chatBubble : null);
            if (incoming != null && changed && expanded)
            {
                incoming.Opacity = 0;
                Ani.Start("fbcontent", Ani.AaOpacity(incoming, 1, 150, new AniEaseOutFluent(Ani.Weak)));
            }
            else if (incoming != null)
            {
                incoming.Opacity = 1;
            }
            if (i == TabChat && bubbleVisible && !Ani.IsRun("fbb"))
            {
                // 气泡在别的页签期间被塞了内容：直接摆正显示，不依赖已经跑完的进场动画
                chatBubble.Opacity = 1;
                var tf = Ani.Ts(chatBubble);
                tf.X = 0;
                tf.Y = 0;
            }
            if (i == TabServer) EnsureServer(true);
            if (i == TabLaunch) RefreshLaunchBar();
        }

        // ============ H史 ============
        void SendChat()
        {
            string text = (chatInput.Text ?? "").Trim();
            if (text.Length == 0) return;
            chatInput.Text = "";
            ShowBallBubble("思考中…");
            AiSteve.Send(text);   // 未开启/没 Key/忙 都会走 ErrorOccurred → 也会弹气泡
        }

        void OnAiText(string text)
        {
            ShowBallBubble(text);
            if (!expanded || tab != TabChat) Pulse();
        }

        void ShowBallBubble(string text)
        {
            Ani.Stop("fbb");
            bubbleVisible = true;
            chatBubbleText.Text = text;
            if (tab != TabChat) return;   // 藏着，切到聊天页再看
            chatBubble.Visibility = Visibility.Visible;
            var tf = Ani.Ts(chatBubble);
            tf.X = 0;
            tf.Y = 20;
            chatBubble.Opacity = 0;
            Ani.Start("fbb",
                Ani.AaOpacity(chatBubble, 1, 180, new AniEaseOutFluent(Ani.Weak)),
                Ani.AaY(chatBubble, -20, 280, new AniEaseOutBack(Ani.Weak)));
        }

        void CloseBallBubble()
        {
            if (!bubbleVisible) return;
            bubbleVisible = false;
            Ani.Start("fbb",
                Ani.AaX(chatBubble, -16 - Ani.Ts(chatBubble).X, 180, new AniEaseInFluent(Ani.Weak)),
                Ani.AaOpacity(chatBubble, -chatBubble.Opacity, 180),
                Ani.AaCode(delegate
                {
                    chatBubble.Visibility = Visibility.Collapsed;
                    Ani.Ts(chatBubble).X = 0;
                }));
        }

        // ============ 下载页 ============
        void UpdateDownload(bool force)
        {
            double p, m;
            int files;
            TaskCenter.Overall(out p, out m, out files);
            int active = 0;
            string name = null;
            lock (TaskCenter.Tasks)
                foreach (var t in TaskCenter.Tasks)
                    if (!t.Finished) { active++; if (name == null) name = t.Name; }
            if (active > 0)
            {
                hadActiveTasks = true;
                if (!force && (DateTime.Now - lastDlUi).TotalMilliseconds < 150) return;
                lastDlUi = DateTime.Now;
                dlName.Text = active > 1 ? name + " 等 " + active + " 项" : name;
                dlPct.Text = ((int)(p * 100)).ToString() + "%  " + m.ToString("0.0") + " MB/s";
                double tw = dlTrack.ActualWidth;
                if (tw > 0) dlFill.Width = tw * Math.Max(0, Math.Min(1, p));
            }
            else
            {
                if (hadActiveTasks) { hadActiveTasks = false; Pulse(); }   // 全部下完 → 提醒
                dlName.Text = "暂无下载任务";
                dlPct.Text = "";
                dlFill.Width = 0;
            }
        }

        // ============ 服务器页 ============
        void EnsureServer(bool refill)
        {
            string pick = null;
            var inst = ServerCore.GetInstances();
            foreach (var n in inst) if (ServerManager.IsRunning(n)) { pick = n; break; }
            if (pick == null && inst.Count > 0) pick = inst[0];
            bool changed = pick != currentServer;
            currentServer = pick;
            if (currentServer == null)
            {
                if (consoleStack.Children.Count == 0)
                    AppendConsole(null, "[HIL] 还没有服务器实例，去「服务器 → 一键开服」创建一个吧");
                return;
            }
            if (changed || refill)
            {
                consoleStack.Children.Clear();
                string tail = ServerManager.GetLogTail(currentServer, 12);
                if (tail.Length > 0)
                    foreach (var line in tail.Split('\n')) AppendConsole(null, line);
                AppendConsole(null, "[HIL] 正在看实例「" + currentServer + "」" +
                    (ServerManager.IsRunning(currentServer) ? "（运行中）" : "（已停止）"));
            }
        }

        void SendServerCmd()
        {
            string cmd = (serverInput.Text ?? "").Trim();
            if (cmd.Length == 0) return;
            serverInput.Text = "";
            EnsureServer(false);
            if (currentServer == null)
            {
                AppendConsole(null, "[HIL] 还没有服务器实例，去「服务器 → 一键开服」创建一个吧");
                return;
            }
            if (!ServerManager.IsRunning(currentServer))
            {
                AppendConsole(null, "[HIL] 实例「" + currentServer + "」没在运行，先去启动它");
                return;
            }
            AppendConsole(null, "> " + cmd);
            ServerManager.SendCommand(currentServer, cmd);
        }

        void AppendConsole(string name, string line)
        {
            // 只盯当前实例；name=null 表示 HIL 自己塞的提示行，无条件显示
            if (name != null)
            {
                if (currentServer == null) EnsureServer(false);
                if (currentServer == null || name != currentServer) return;
            }
            var b = new Border();
            b.CornerRadius = new CornerRadius(6);
            b.Padding = new Thickness(6, 2, 6, 2);
            b.Margin = new Thickness(0, 1, 0, 1);
            b.Background = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            var t = new TextBlock();
            t.Text = line;
            t.FontFamily = new FontFamily("Consolas");
            t.FontSize = 9.5;
            t.Foreground = Palette.B(Colors.White);
            b.Child = t;
            consoleStack.Children.Add(b);
            while (consoleStack.Children.Count > 40) consoleStack.Children.RemoveAt(0);
        }

        // ============ 史蒂夫鱼眼脸 ============
        // 经典 64×64 皮肤布局：正脸 = (8,8) 起 8×8。把方脸按半球鱼眼投影弯成圆形：
        // 中心放大、边缘压缩 + 边缘暗角，最近邻采样保留像素感。
        static System.Windows.Media.Imaging.BitmapSource BuildSteveLogo()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var rs = asm.GetManifestResourceStream("stevepng"))
                {
                    if (rs == null) return null;
                    var skin = new System.Drawing.Bitmap(rs);
                    var face = new System.Drawing.Color[8, 8];
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < 8; x++)
                            face[x, y] = skin.GetPixel(8 + x, 8 + y);
                    skin.Dispose();
                    const int N = 96;
                    const double C = (N - 1) / 2.0;
                    var outp = new System.Drawing.Bitmap(N, N, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    for (int y = 0; y < N; y++)
                    {
                        for (int x = 0; x < N; x++)
                        {
                            double u = (x - C) / C, v = (y - C) / C;
                            double r = Math.Sqrt(u * u + v * v);
                            if (r > 1) continue;   // 圆外全透明
                            double theta = Math.Atan2(v, u);
                            double rSrc = Math.Sin(r * Math.PI / 2);
                            int ix = (int)(4 + rSrc * Math.Cos(theta) * 4);
                            int iy = (int)(4 + rSrc * Math.Sin(theta) * 4);
                            if (ix < 0) ix = 0; if (ix > 7) ix = 7;
                            if (iy < 0) iy = 0; if (iy > 7) iy = 7;
                            var c = face[ix, iy];
                            double k = 1 - 0.38 * r * r * r;   // 球面暗角
                            outp.SetPixel(x, y, System.Drawing.Color.FromArgb(c.A,
                                (byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k)));
                        }
                    }
                    var ms = new MemoryStream();
                    outp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    outp.Dispose();
                    ms.Position = 0;
                    var dec = new System.Windows.Media.Imaging.PngBitmapDecoder(ms,
                        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
                        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                    var f = dec.Frames[0];
                    f.Freeze();
                    return f;
                }
            }
            catch (Exception ex)
            {
                HilLog.Write("悬浮球 史蒂夫脸生成失败: " + ex.Message);
                return null;
            }
        }

        // ============ 出现 / 消失 ============
        public void PlayAppear()
        {
            var s = Ani.Sc(strip);
            strip.RenderTransformOrigin = new Point(mirror ? 0.05 : 0.95, 0.5);
            if (!ThemeManager.AnimOn) return;
            s.ScaleX = 0.3;
            s.ScaleY = 0.3;
            strip.Opacity = 0;
            Ani.Start("ballio",
                Ani.AaOpacity(strip, 1, 140, new AniEaseOutFluent(Ani.Weak)),
                Ani.Aa(delegate(double d) { s.ScaleX += d; s.ScaleY += d; }, 0.7, 320, new AniEaseOutBack(Ani.Middle)));
        }

        public void PlayVanish()
        {
            if (!ThemeManager.AnimOn) { CloseNow(); return; }
            var s = Ani.Sc(strip);
            strip.RenderTransformOrigin = new Point(mirror ? 0.05 : 0.95, 0.5);
            Ani.Start("ballio",
                Ani.AaOpacity(strip, -strip.Opacity, 130, new AniEaseInFluent(Ani.Weak)),
                Ani.Aa(delegate(double d) { s.ScaleX += d; s.ScaleY += d; }, 0.4 - s.ScaleX, 150, new AniEaseInFluent(Ani.Weak)),
                Ani.AaCode(delegate { CloseNow(); }));
        }

        void CloseNow()
        {
            try { Close(); } catch { }
            FloatBall.OnVanished();
        }

        // ============ 主题跟随 ============
        void RefreshGradient()
        {
            Color a = ThemeManager.CurrentColor;
            Color b = Color.FromRgb((byte)(a.R * 0.72), (byte)(a.G * 0.72), (byte)(a.B * 0.72));
            capsuleBrush.GradientStops.Clear();
            capsuleBrush.GradientStops.Add(new GradientStop(a, 0));
            capsuleBrush.GradientStops.Add(new GradientStop(b, 1));
        }

        static bool IsLightTheme()
        {
            Color a = ThemeManager.CurrentColor;
            return a.R * 0.299 + a.G * 0.587 + a.B * 0.114 > 190;
        }

        // 白主题下球面接近白色：文字/小点/输入全部翻深色，保证可读
        void RefreshContrast()
        {
            bool light = IsLightTheme();
            Color fg = light ? Color.FromRgb(0x22, 0x24, 0x2A) : Colors.White;
            var fgB = Palette.B(fg);
            var fgDim = new SolidColorBrush(Color.FromArgb(160, fg.R, fg.G, fg.B));
            dlName.Foreground = fgB;
            dlPct.Foreground = fgB;
            dlFill.Background = fgB;
            chatInput.Foreground = fgB;
            chatInput.CaretBrush = fgB;
            serverInput.Foreground = fgB;
            serverInput.CaretBrush = fgB;
            chatHint.Foreground = fgDim;
            serverHint.Foreground = fgDim;
            chatBubbleText.Foreground = fgB;
            chatBubbleClose.Foreground = fgDim;
            // 快速启动页同步对比色
            fbFg = fg;
            launchEmpty.Foreground = fgDim;
            launchBtnBg.Color = fg;
            launchBtnText.Foreground = Palette.B(light ? Colors.White : ThemeManager.CurrentColor);
            RefreshLaunchCells();
            // 小点选中色重刷（不动当前选中态）
            Color on = fg;
            for (int k = 0; k < 4; k++)
                dotBrushes[k].Color = k == tab ? on : Color.FromArgb(102, on.R, on.G, on.B);
        }
    }
}
