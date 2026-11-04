// HugeLauncher.Ui - 主窗口：无边框圆角 + 自绘标题栏 + 左启动栏 + 右侧胶囊导航与页面宿主 + Toast
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using HugeLauncher.Ui.Pages;
using HugeLauncher.Ai;

namespace HugeLauncher.Ui
{
    class MainWindow : Window
    {
        const string BrandName = "Huge Insane Launcher";
        // 版本号规则：年份.月份.该月第几个版本（当月首个省略末项，第二个为 1）
        public const string Version = "26.11.1";
        public const string Channel = "Alpha";   // 渠道徽章：Alpha/Beta/Release

        Config cfg;
        DarkComboBox userCombo;
        TextBlock versionText;
        FlatButton btnLaunch;
        Border rootBorder;
        TranslateTransform enterTransform;

        // 页面宿主与导航
        Grid contentHost;
        UIElement currentPage;
        Dictionary<string, UIElement> pages = new Dictionary<string, UIElement>();
        // 两级导航：一级标签（下载/设置）+ 二级边栏
        class NavGroup { public string Name; public string[] Labels; public string[] Keys; }
        NavGroup[] navGroups;
        int activeGroup = -1;
        TextBlock[] groupTabs;
        Border[] groupCells;     // 一级标签单元格（UiMap 注册用）
        Border groupIndicator;
        TranslateTransform groupIndTf;
        SquircleBorder groupPill;    // 液态玻璃：一级标签滑动 pill（活底多环；可抓取拖动，iOS 式）
        TranslateTransform groupPillTf;
        ScaleTransform groupPillSc;
        Spring groupPillSpring;      // pill 位置弹簧（速度接果冻形变）
        Spring groupPillGrab = new Spring(1) { Stiffness = 320, Damping = 13 };  // 抓住/松手缩放
        bool gPillDragging;
        double gPillGrabX, gPillLastX, gPillDownX;
        int gPillLastTick;
        bool gTabPending;            // 史莱姆开时文字层拖动：按下挂起（未超阈值不抢点击）
        double gTabStartX;
        double gPillGrabK = 1.0, gPillJelly;
        StackPanel tabStack;         // 一级标签文字层（一级 pill 的活底来源）
        Grid groupArena;             // 一级 pill 的活动容器（拖动取坐标用）
        StackPanel sideList;
        Grid sideWrapPanel;          // 二级边栏整列外壳（实例管理/详情等游离页时隐藏）
        ColumnDefinition sideCol;    // 二级边栏列宽（游离页时归零，不留空带）
        bool pendingSideVisible;     // 下一次换页 swap 时才生效的侧栏状态
        int pendingSideWidth = 118;
        StackPanel verticalSecondary;          // 纵向布局：二级横条
        Border verticalSecondaryBar;           // 二级横条外壳（无二级项时收起）
        Border verticalPill;                   // 二级选中指示框（主题化，滑动）
        TranslateTransform verticalPillTransform;
        bool verticalPillInit;
        readonly List<FlatButton> verticalButtons = new List<FlatButton>();
        Border railPill;
        TranslateTransform railPillTransform;
        bool railPillInit;
        Spring railPillSpring = new Spring(0) { Stiffness = 200, Damping = 16 };
        Spring verticalPillSpring = new Spring(0) { Stiffness = 200, Damping = 16 };
        TextBlock[] railLabels;
        Path[] railIcons;
        Grid floatBall;              // 下载管理悬浮球：有任务时出现在侧栏右下角，单击进下载管理
        TextBlock floatText;         // 球心白字百分比
        bool ballVisible, ballUpdatePending;
        DateTime lastBallText = DateTime.MinValue;
        Ellipse ballSheen;           // 玻璃球高光（随鼠标视差偏移）
        RadialGradientBrush ballSheenBrush;
        readonly List<SideTabItem> sideItems = new List<SideTabItem>();
        SquircleBorder sidePill;         // 玻璃模式共享滑动选中 pill（活底多环；可抓取拖动）
        TranslateTransform sidePillTf;
        ScaleTransform sidePillSc;
        Spring sidePillSpring = new Spring(0) { Stiffness = 200, Damping = 16 };
        Spring sidePillGrab = new Spring(1) { Stiffness = 320, Damping = 13 };  // 抓住/松手缩放
        bool pillDragging;
        double pillGrabY, pillLastY, pillDownY;
        int pillLastTick;
        bool sTabPending;            // 史莱姆开时二级文字层拖动：按下挂起
        double sTabStartY;
        double pillGrabK = 1.0, pillJelly;
        readonly List<string> sideKeys = new List<string>();

        // Toast
        Border toastBox;
        TextBlock toastText;
        DispatcherTimer toastTimer;

        // AI 史蒂夫（H史）
        SteveView steveView;
        Grid steveLayer;         // 史蒂夫层：左栏玻璃件的活底来源（按钮底下史蒂夫的脚实时动）
        Border chatWrap;         // 聊天输入框外壳（仅 AiEnabled 时显示）
        TextBox chatInput;
        TextBlock chatHint;
        bool chatBusy;
        Border bubble;           // 回复气泡（主题色底白字，场上最多一条）
        TextBlock bubbleText;
        SmoothScrollViewer bubbleScroll;   // 气泡文字滚动区（帮助泡会临时加高，关闭时复位 100）
        StackPanel bubbleBtns;   // 确认按钮行（好/算了）
        bool bubbleVisible;
        Action<bool> bubbleDecision;
        StackPanel btnStack;     // 左下按钮组（气泡定位锚点）
        Border lockOverlay;      // ACTING 操作锁拦截层
        Border skinOverlay;      // 换肤渐变罩
        string curNavKey = "download:install";   // 换肤重建后恢复页面用
        object curNavParam;
        int runningGames;        // 在跑的游戏进程数；>0 时主窗退到悬浮球模式
        DispatcherTimer ballTimer;              // 转球守望：游戏窗口出现 + 30s 无操作才转球
        System.Diagnostics.Process watchedGame;
        bool gameWindowSeen;
        DateTime lastInput = DateTime.Now;

        public MainWindow()
        {
            cfg = Config.Load();
            AccountStore.Init(cfg);   // 账号库：首启从用户名历史迁移离线账号
            AccountStore.Changed += delegate { RefreshAccounts(); };
            Modrinth.MirrorOverride = string.IsNullOrEmpty(cfg.ModrinthMirror) ? null : cfg.ModrinthMirror;
            UiWatchdog.Start(Dispatcher);   // 卡顿哨兵：只观测不改行为，卡死记录进 hil.log
            ThemeManager.Speed = cfg.AnimSpeed;
            ThemeManager.ApplyTheme(cfg.Theme);
            ThemeManager.LightModeSwitch = SwitchSkin;    // 设置页只调钩子，不碰窗口
            ThemeManager.EvalAuto = EvalAutoDayNight;
            // SkinChanged 在 BuildUi 之后再接：ApplySkin 在构造期会触发事件，早接会建两次树
            // 流动史莱姆：色板半透明化在 Palette.Apply 里吃 Slime.On，必须先落位再建 UI
            Slime.On = cfg.SlimeOn;
            Slime.BlurBg = cfg.SlimeBlurBg;
            // 中层/前景调节已砍掉：锁定默认值（中层 60% 通透度、前景 0 不糊），忽略旧配置残留
            Slime.FrostMid = 60;
            Slime.BlurFore = 0;
            Slime.Changed += delegate { Palette.Apply(cfg.LightMode); RebuildUi(); };
            Slime.RefreshColorsAsync();   // 后台取壁纸色，回来后只给画布换色（Recolor），不重建 UI
            Palette.Apply(cfg.LightMode);   // 亮/暗色板在建 UI 前定位，控件首张脸就是对的
            Updater.NewVersionHandler = OnUpdateFound;
            ThemeManager.ApplySkin(cfg.Skin);   // 建 UI 前置位：流光/霜面/素底
            ThemeManager.ApplyLayout(cfg.Layout);

            // 页面契约接线
            Navigator.Handler = Navigate;
            PageBase.ToastHandler = ShowToast;
            PageBase.VersionSelectHandler = RefreshVersionText;
            PageBase.LaunchHandler = delegate(string id)
            {
                if (!string.IsNullOrEmpty(id))
                {
                    cfg.VersionId = id;
                    cfg.Save();
                    RefreshVersionText();
                }
                LaunchGame();
            };
            // 「?」小圆钮出口：帮助泡比聊天泡高（220 vs 100），关闭时 CloseBubble 复位
            HelpCatalog.BubbleHandler = delegate(HelpEntry e)
            {
                bubbleScroll.MaxHeight = 220;
                ShowBubble("【" + e.Title + "】\n" + e.Body, false, null);
            };

            Title = BrandName;
            // 从 exe 自身提取嵌入图标当窗口图标（任务栏/Alt+Tab 用），拷到哪都跟着 exe 走
            try
            {
                using (var ico = System.Drawing.Icon.ExtractAssociatedIcon(
                    System.Reflection.Assembly.GetExecutingAssembly().Location))
                {
                    if (ico != null)
                        Icon = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            ico.Handle, Int32Rect.Empty,
                            System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                }
            }
            catch { }
            Width = 1040;
            Height = 600;
            // 高 DPI 屏物理像素放大后可能超出工作区（底边被切出屏幕）：按工作区钳制，留 20px 边距
            var wa = SystemParameters.WorkArea;
            if (Width > wa.Width - 40) Width = wa.Width - 40;
            if (Height > wa.Height - 40) Height = wa.Height - 40;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Colors.Transparent);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            BuildUi();
            ThemeManager.SkinChanged += delegate { Frost.Refresh(); RebuildUi(); };
            ThemeManager.LayoutChanged += delegate { RebuildUi(); };
            WireAiSteve();
            Loaded += OnLoaded;
            // [临时探针] --bubble-demo：启动后弹一条固定气泡，供截图排查玻璃折射错位
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--bubble-demo") >= 0)
                Loaded += delegate
                {
                    ShowBubble("折射探针：气泡正下方应是史蒂夫的腿，看到头=透镜源错位。AAAAAAAAAAAAAAAAAA", false, null);
                    var t = new System.Windows.Threading.DispatcherTimer();
                    t.Interval = TimeSpan.FromSeconds(2.5);
                    int probeTick = 0;
                    t.Tick += delegate
                    {
                        probeTick++;
                        if (probeTick >= 2) t.Stop();
                        Glass.DumpLensImages("lens_imgs");
                        try
                        {
                            var bmpSrc = steveView.MirrorImg.Source as System.Windows.Media.Imaging.BitmapSource;
                            if (bmpSrc != null)
                            {
                                var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                                enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bmpSrc));
                                using (var fs = System.IO.File.Create("mirror_dump.png")) enc.Save(fs);
                            }
                        }
                        catch { }
                        try
                        {
                            var sb = new System.Text.StringBuilder();
                            sb.Append(Glass.DumpLenses());
                            sb.AppendLine("lenses=" + Glass.LensCount);
                            sb.AppendLine("steveLayer: " + steveLayer.ActualWidth + "x" + steveLayer.ActualHeight +
                                " pos in window: " + steveLayer.TransformToAncestor(this).Transform(new Point(0, 0)));
                            sb.AppendLine("steveView pos in window: " + steveView.TransformToAncestor(this).Transform(new Point(0, 0)) +
                                " size: " + steveView.ActualWidth + "x" + steveView.ActualHeight);
                            sb.AppendLine("mirror pos in steveLayer: " + steveView.MirrorImg.TransformToAncestor(steveLayer).Transform(new Point(0, 0)) +
                                " size: " + steveView.MirrorImg.ActualWidth + "x" + steveView.MirrorImg.ActualHeight);
                            sb.AppendLine("bubble pos in steveLayer: " + bubble.TransformToVisual(steveLayer).Transform(new Point(0, 0)) +
                                " size: " + bubble.ActualWidth + "x" + bubble.ActualHeight);
                            // 按钮链诊断：每个环节的可见性/尺寸/窗口坐标/不透明度
                            sb.AppendLine("btnStack: vis=" + btnStack.Visibility + " op=" + btnStack.Opacity +
                                " size=" + btnStack.ActualWidth + "x" + btnStack.ActualHeight +
                                " pos=" + btnStack.TransformToAncestor(this).Transform(new Point(0, 0)));
                            foreach (System.Windows.UIElement ch in btnStack.Children)
                            {
                                var fe = ch as FrameworkElement;
                                if (fe == null) continue;
                                object content = null;
                                try { content = ((System.Windows.Controls.ContentControl)ch).Content; } catch { }
                                sb.AppendLine("btn: " + fe.GetType().Name +
                                    " vis=" + fe.Visibility + " op=" + fe.Opacity +
                                    " size=" + fe.ActualWidth + "x" + fe.ActualHeight +
                                    " pos=" + fe.TransformToAncestor(this).Transform(new Point(0, 0)) +
                                    " content=" + (content == null ? "null" : content.GetType().Name));
                            }
                            System.IO.File.WriteAllText("lens_dump" + probeTick + ".txt", sb.ToString());
                        }
                        catch (Exception ex) { System.IO.File.WriteAllText("lens_dump" + probeTick + ".txt", ex.ToString()); }
                    };
                    t.Start();
                };
            Closing += OnClosing;
            // 主窗最小化/还原 → 悬浮球模式同步（用户手动最小化也会出现球）
            StateChanged += delegate { SyncBallMode(); };
            // 转球倒计时的"用户在操作"判定：点击/按键/鼠标移动都刷新时刻
            PreviewMouseDown += delegate { lastInput = DateTime.Now; };
            PreviewKeyDown += delegate { lastInput = DateTime.Now; };
            PreviewMouseMove += delegate { lastInput = DateTime.Now; };
        }

        // ===== AI 史蒂夫接线：事件 → 气泡/聊天框/操作锁 =====
        // handler 全部存根：换肤重建时先退订再订阅，防止重复弹泡
        Action<bool> hActing;
        Action<string> hAiReply, hAiError, hAiConfirm, hSrvCrash;
        Action hAiTurnDone;
        EventHandler hAiCfgChanged;

        void WireAiSteve()
        {
            // 退订旧的（重建时）
            SteveDirector.ActingChanged -= hActing;
            AiSteve.ReplyReceived -= hAiReply;
            AiSteve.ErrorOccurred -= hAiError;
            AiSteve.ConfirmationRequested -= hAiConfirm;
            AiSteve.TurnFinished -= hAiTurnDone;
            AiSteve.ConfigChanged -= hAiCfgChanged;
            ServerManager.StateChanged -= hSrvCrash;

            AiSteve.Cfg = cfg;
            AiTools.Cfg = cfg;
            SteveDirector.Steve = steveView;
            SteveDirector.PageFinder = delegate(string key)
            {
                UIElement p;
                if (!pages.TryGetValue(key, out p))
                {
                    p = CreatePage(key);
                    if (p == null) return null;
                    pages[key] = p;
                }
                return p as FrameworkElement;
            };
            SteveDirector.LaunchButton = delegate { return btnLaunch; };
            SteveDirector.FloatBall = delegate { return floatBall; };   // 下载管理悬浮球（剧本视线定位用）
            FloatBall.RestoreMainWindow = delegate { RestoreFull(); };   // 双击球 → 全窗
            SteveDirector.SayBubble = delegate(string text) { ShowBubble(text, false, null); };
            hActing = delegate(bool acting)
            {
                Dispatcher.BeginInvoke(new Action(delegate { SetLockOverlay(acting); }));
            };
            SteveDirector.ActingChanged += hActing;
            hAiReply = delegate(string text)
            {
                Dispatcher.BeginInvoke(new Action(delegate { ShowBubble(text, false, null); }));
            };
            AiSteve.ReplyReceived += hAiReply;
            hAiError = delegate(string msg)
            {
                Dispatcher.BeginInvoke(new Action(delegate { ShowBubble("H史：" + msg, false, null); }));
            };
            AiSteve.ErrorOccurred += hAiError;
            hAiConfirm = delegate(string desc)
            {
                Dispatcher.BeginInvoke(new Action(delegate { ShowBubble(desc, true, delegate(bool d) { AiSteve.Confirm(d); }); }));
            };
            AiSteve.ConfirmationRequested += hAiConfirm;
            hAiTurnDone = delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    SetChatBusy(false);
                    ReturnChatFocus();
                }));
            };
            AiSteve.TurnFinished += hAiTurnDone;
            hAiCfgChanged = delegate(object s, EventArgs ea)
            {
                Dispatcher.BeginInvoke(new Action(RefreshAiVisibility));
            };
            AiSteve.ConfigChanged += hAiCfgChanged;
            // 服务器崩溃主动关怀：进程退出码非 0 且非用户主动停服 → 弹确认泡提议排查
            hSrvCrash = delegate(string name)
            {
                try
                {
                    if (!cfg.AiEnabled) return;
                    if (ServerManager.IsRunning(name)) return;   // 启动成功的 StateChanged 不管
                    int code;
                    bool userStop;
                    if (!ServerManager.TakeLastExit(name, out code, out userStop)) return;
                    if (userStop || code == 0) return;
                    HilLog.Write("H史 服务器崩溃关怀触发: " + name + " 退出码 " + code);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        ShowBubble("H史：服务器「" + name + "」好像崩了（退出码 " + code + "），需要我看看原因吗？", true,
                            delegate(bool agree) { if (agree) BeginServerCrashCare(name); });
                    }));
                }
                catch (Exception ex) { HilLog.Write("H史 崩溃关怀异常: " + ex.Message); }
            };
            ServerManager.StateChanged += hSrvCrash;
        }

        // 崩溃关怀第二步：读日志尾部→分类→按因给建议→问是否重启（OOM 先顺手调大内存）
        void BeginServerCrashCare(string name)
        {
            var th = new Thread(new ThreadStart(delegate
            {
                string tail = ServerManager.GetLogTail(name, 80);
                bool oom = tail != null && tail.Contains("OutOfMemoryError");
                string cls = AiTools.ClassifyServerCrash(tail);
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    string advice = cls + "。";
                    if (oom) advice += "我可以顺手把内存调大 1024MB。";
                    ShowBubble("H史：" + advice + "要我帮你重启「" + name + "」吗？", true, delegate(bool agree)
                    {
                        if (!agree) return;
                        try
                        {
                            var inst = ServerCore.Load(name);
                            if (oom)
                            {
                                inst.MemoryMb = Math.Min(inst.MemoryMb + 1024, 32768);
                                ServerCore.Save(inst);
                            }
                            ServerManager.StartExisting(inst);
                            ShowBubble("H史：好，正在重启「" + name + "」，稍等。", false, null);
                        }
                        catch (Exception ex)
                        {
                            ShowBubble("H史：重启失败了：" + ex.Message, false, null);
                        }
                    });
                }));
            }));
            th.IsBackground = true;
            th.Start();
        }

        void BuildUi()
        {
            // 两卡分缝：整窗透明，背景层只画在两张卡的圆角矩形里，缝里直接透桌面
            rootBorder = new Border();
            rootBorder.CornerRadius = new CornerRadius(12);
            rootBorder.Background = null;
            rootBorder.BorderBrush = ThemeManager.Accent;
            rootBorder.BorderThickness = new Thickness(0);
            rootBorder.Margin = new Thickness(10);
            enterTransform = new TranslateTransform(0, 14);
            rootBorder.RenderTransform = enterTransform;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });
            root.RowDefinitions.Add(UiHelpers.StarRow());

            var bgLayer = new Grid();
            bgLayer.IsHitTestVisible = false;
            Grid.SetRowSpan(bgLayer, 2);
            if (Slime.On)
            {
                bgLayer.Children.Add(new SlimeView());
                var veil = new Border();
                veil.Background = Palette.WindowBgVeil();
                veil.IsHitTestVisible = false;
                bgLayer.Children.Add(veil);
            }
            else if (Frost.On)
            {
                bgLayer.Background = Frost.Backdrop;
            }
            else
            {
                bgLayer.Background = Palette.Light
                    ? new SolidColorBrush(Color.FromArgb(122, 255, 255, 255))
                    : new SolidColorBrush(Color.FromArgb(122, 0, 0, 0));
            }
            Action clipCards = delegate
            {
                double w = root.ActualWidth, h = root.ActualHeight;
                if (w < 32 || h < 32) return;
                var geo = new GeometryGroup();
                geo.Children.Add(new RectangleGeometry(new Rect(0, 6, 280, Math.Max(0, h - 16)), 12, 12));
                geo.Children.Add(new RectangleGeometry(new Rect(290, 6, Math.Max(0, w - 300), Math.Max(0, h - 16)), 12, 12));
                bgLayer.Clip = geo;
            };
            root.SizeChanged += delegate { clipCards(); };
            root.Children.Add(bgLayer);
            Glass.BgLensSource = bgLayer;
            Dispatcher.BeginInvoke(clipCards, DispatcherPriority.Loaded);

            var content = BuildContent();
            Grid.SetRow(content, 0);
            Grid.SetRowSpan(content, 2);
            root.Children.Add(content);

            // Toast 浮层（覆盖全窗，顶部居中，不挡鼠标）
            toastText = new TextBlock();
            toastText.Foreground = Palette.B(Palette.Text);
            toastText.FontSize = 12;
            toastText.HorizontalAlignment = HorizontalAlignment.Center;
            // Toast 底色跟随明暗：亮模式=白（用户点名要白的），暗模式=原深青（保持原样）
            toastBox = new Border();
            toastBox.Background = Palette.B(Palette.Light ? Palette.Panel : Color.FromRgb(0x2A, 0x2E, 0x3A));
            toastBox.BorderBrush = ThemeManager.Accent;
            toastBox.BorderThickness = new Thickness(1);
            toastBox.CornerRadius = new CornerRadius(6);
            toastBox.Padding = new Thickness(14, 8, 14, 8);
            toastBox.Margin = new Thickness(0, 46, 0, 0);
            toastBox.HorizontalAlignment = HorizontalAlignment.Center;
            toastBox.VerticalAlignment = VerticalAlignment.Top;
            toastBox.MaxWidth = 700;
            toastBox.Opacity = 0;
            toastBox.IsHitTestVisible = false;
            toastBox.Child = toastText;
            Grid.SetRowSpan(toastBox, 2);
            root.Children.Add(toastBox);

            // H史操作锁：ACTING 期间盖全窗浅遮罩，吞掉所有鼠标按下；任意点击 = 打断剧本
            lockOverlay = new Border();
            lockOverlay.Background = Palette.B(Color.FromArgb(70, 0, 0, 0));
            var lockText = new TextBlock();
            lockText.Text = "H史操作中…（点击任意处可打断）";
            lockText.Foreground = ThemeManager.Accent;
            lockText.FontSize = 12;
            lockText.HorizontalAlignment = HorizontalAlignment.Center;
            lockText.VerticalAlignment = VerticalAlignment.Bottom;
            lockText.Margin = new Thickness(0, 0, 0, 46);
            lockOverlay.Child = lockText;
            lockOverlay.Opacity = 0;
            lockOverlay.Visibility = Visibility.Collapsed;
            lockOverlay.PreviewMouseDown += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                SteveDirector.Cancel();
            };
            Grid.SetRowSpan(lockOverlay, 2);
            root.Children.Add(lockOverlay);

            // 换肤渐变罩：切亮/暗色时盖住全窗，底下重建完再揭开（渐变过渡 + 遮重建闪烁）
            skinOverlay = new Border();
            skinOverlay.Background = Palette.WindowBg();
            skinOverlay.Opacity = 0;
            skinOverlay.IsHitTestVisible = false;
            Grid.SetRowSpan(skinOverlay, 2);
            root.Children.Add(skinOverlay);

            rootBorder.Child = root;
            Content = rootBorder;
        }

        // 亮/暗色切换（带渐变动画）：罩子盖下 → 换色板 + 整套 UI 重建 → 揭开
        public void SwitchSkin(bool light)
        {
            if (Palette.Light == light) return;
            if (Ani.IsRun("skinSwap")) return;
            cfg.LightMode = light;
            cfg.Save();
            HilLog.Write("换肤: " + (light ? "亮色" : "暗色"));
            if (!ThemeManager.AnimOn)
            {
                Palette.Apply(light);
                Frost.Refresh();   // 霜面底图烘死了亮/暗纱，必须随色板重烘
                RebuildUi();
                return;
            }
            skinOverlay.IsHitTestVisible = true;
            skinOverlay.Background = Palette.WindowBg();
            Ani.Start("skinSwap",
                Ani.AaOpacity(skinOverlay, 1 - skinOverlay.Opacity, 180, new AniEaseInFluent(Ani.Weak)),
                Ani.AaCode(delegate
                {
                    Palette.Apply(light);
                    Frost.Refresh();   // 重烘霜面底图，否则亮色模式仍显示暗纱
                    skinOverlay.Background = Palette.WindowBg();   // 揭开时已是新肤色
                    RebuildUi();
                    Ani.Start("skinReveal",
                        Ani.AaOpacity(skinOverlay, -1, 320, new AniEaseOutFluent(Ani.Weak)),
                        Ani.AaCode(delegate { skinOverlay.IsHitTestVisible = false; }));
                }));
        }

        // 整套 UI 重建（换肤用）：页面缓存清空、当前页按记录恢复
        void RebuildUi()
        {
            Ani.Stop("pageSwap");
            pages.Clear();
            currentPage = null;
            activeGroup = -1;   // 让 SelectGroup 重新建二级边栏
            bubbleVisible = false;
            Content = null;
            BuildUi();
            WireAiSteve();      // 幂等（先退订再订阅）
            RefreshAiVisibility();
            Glass.Init(rootBorder);   // 玻璃活底换到新根
            DarkScrollBar.Register(Application.Current, Glass.On);   // 全局滚动条按新色板重注册
            Navigate(curNavKey ?? "download:install", curNavParam);
        }

        // 操作锁显隐（淡入淡出；关上即全场恢复）
        void SetLockOverlay(bool on)
        {
            HilLog.Write("剧本 操作锁 " + (on ? "盖上" : "撤掉"));
            if (on)
            {
                lockOverlay.Visibility = Visibility.Visible;
                Ani.Start("ailock", Ani.AaOpacity(lockOverlay, 1 - lockOverlay.Opacity, 150));
            }
            else
            {
                Ani.Start("ailock",
                    Ani.AaOpacity(lockOverlay, -lockOverlay.Opacity, 150),
                    Ani.AaCode(delegate { lockOverlay.Visibility = Visibility.Collapsed; }));
                ReturnChatFocus();   // 演戏结束，把键盘焦点还给聊天框
            }
        }

        // 焦点归还：演戏/思考结束后光标虽在框里但键盘焦点丢了，主动要回
        void ReturnChatFocus()
        {
            if (chatWrap != null && chatWrap.Visibility == Visibility.Visible)
            {
                try { Keyboard.Focus(chatInput); } catch { }
            }
        }

        // 自绘标题栏：左 Logo，右最小化/关闭，拖动移动窗口
        UIElement BuildTitleBar()
        {
            var bar = new Grid();
            bar.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };

            var titleBg = new Grid();        // 标题栏底层：品牌文字（右侧圆形按钮的活底来源）
            bar.Children.Add(titleBg);

            var left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.Margin = new Thickness(14, 0, 0, 0);
            left.VerticalAlignment = VerticalAlignment.Center;
            var appName = new TextBlock();
            appName.Text = BrandName;
            appName.Foreground = Palette.B(Palette.Text);
            appName.FontSize = 13;
            appName.FontWeight = FontWeights.SemiBold;
            var hil = new TextBlock();
            hil.Text = "HIL";
            hil.Foreground = Palette.B(Palette.Dim);
            hil.FontSize = 9;
            hil.Margin = new Thickness(6, 0, 0, 0);
            hil.Padding = new Thickness(0, 0, 0, 2);
            hil.VerticalAlignment = VerticalAlignment.Bottom;
            var ver = new TextBlock();
            ver.Text = Version;
            ver.Foreground = Palette.B(Palette.Dim);
            ver.FontSize = 9;
            ver.Margin = new Thickness(6, 0, 0, 0);
            ver.Padding = new Thickness(0, 0, 0, 2);
            ver.VerticalAlignment = VerticalAlignment.Bottom;
            // 渠道徽章（Alpha/Beta/Release）：主题色底白字，跟随换肤
            var betaBrush = new SolidColorBrush(ThemeManager.CurrentColor);
            ThemeManager.ColorChanged += delegate { betaBrush.Color = ThemeManager.CurrentColor; };
            var beta = new Border();
            beta.Background = betaBrush;
            beta.CornerRadius = new CornerRadius(4);
            beta.Padding = new Thickness(5, 1, 5, 2);
            beta.Margin = new Thickness(6, 0, 0, 0);
            beta.VerticalAlignment = VerticalAlignment.Bottom;
            var betaText = new TextBlock();
            betaText.Text = Channel;
            betaText.Foreground = Palette.B(Colors.White);
            betaText.FontSize = 8;
            betaText.FontWeight = FontWeights.Bold;
            beta.Child = betaText;
            left.Children.Add(appName);
            left.Children.Add(hil);
            left.Children.Add(ver);
            left.Children.Add(beta);
            titleBg.Children.Add(left);   // 品牌文字进底层（标题按钮的活底来源）

            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            var btnMin = MakeTitleButton("—", Palette.BtnHover, new CornerRadius(0));
            btnMin.Click += delegate { WindowState = WindowState.Minimized; };
            btnMin.Margin = new Thickness(0, 0, 10, 0);   // 与关闭按钮间距 10px
            btnMin.GlassSource = titleBg;   // 活底：刷标题栏底层（实时）
            var btnClose = MakeTitleButton("✕", Palette.Red, new CornerRadius(0, 12, 0, 0));
            btnClose.Click += delegate { Close(); };
            btnClose.GlassSource = titleBg;
            right.Children.Add(btnMin);
            right.Children.Add(btnClose);
            bar.Children.Add(right);
            return bar;
        }

        // 标题栏按钮：透明底，悬停 200ms 渐变（关闭变红 / 最小化变亮）
        // 玻璃模式：32×32 正圆（CircleInGlass），普通模式：46×36 工业风
        FlatButton MakeTitleButton(string text, Color hoverBg, CornerRadius radius)
        {
            var b = new FlatButton(text);
            b.SetColors(Color.FromArgb(0, 0, 0, 0), hoverBg, Palette.Text, Palette.Text);
            b.SetCornerRadius(radius);
            b.SetPadding(new Thickness(0));
            b.GlassFixed = true;    // 标题栏固定铬件：真快照透镜底 + 折射环
            b.CircleInGlass = true; // setter 触发重建脸，GlassFixed 一并生效
            b.SetFontSize(13);
            ApplyTitleBtnSize(b);
            ThemeManager.GlassChanged += delegate { ApplyTitleBtnSize(b); };
            return b;
        }

        void ApplyTitleBtnSize(FlatButton b)
        {
            if (Glass.On)
            {
                b.Width = 32; b.Height = 32;
                b.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                b.Width = 46; b.Height = 36;
                b.VerticalAlignment = VerticalAlignment.Stretch;
            }
        }

        UIElement BuildContent()
        {
            if (ThemeManager.IsVertical) InitNavGroups();
            return BuildTwoCardShell(ThemeManager.IsVertical ? BuildVerticalRightBody() : BuildClassicRightBody());
        }

        UIElement BuildClassicRightBody()
        {
            var rb = new Grid();
            var right = BuildRightSide();
            if (Slime.On && Slime.BlurFore > 0.01)
                right.Effect = new BlurEffect { Radius = Slime.BlurFore };
            rb.Children.Add(right);
            rb.Children.Add(BuildFloatBall());
            return rb;
        }

        UIElement BuildVerticalRightBody()
        {
            var railHost = new Grid();
            railHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(72) });
            railHost.ColumnDefinitions.Add(UiHelpers.StarCol());
            railHost.Children.Add(BuildRail());
            var right = BuildVerticalRight();
            Grid.SetColumn(right, 1);
            railHost.Children.Add(right);
            return railHost;
        }

        // ===== 纵向舞台：280 史蒂夫 + 72 竖轨 + 右上二级 + 页面 =====
        void InitNavGroups()
        {
            navGroups = new[]
            {
                new NavGroup { Name = "下载", Labels = new[] { "原版游戏", "模组", "数据包", "资源包", "光影包", "整合包" },
                    Keys = new[] { "download:install", "download:mod", "download:datapack", "download:resourcepack", "download:shader", "download:pack" } },
                new NavGroup { Name = "设置", Labels = new[] { "游戏设置", "启动器设置" },
                    Keys = new[] { "settings:game", "settings:launcher" } },
                new NavGroup { Name = "联机", Labels = new[] { "一键开服", "联机大厅", "服务器列表" },
                    Keys = new[] { "server:oneclick", "server:hall", "server:list" } },
                new NavGroup { Name = "账号", Labels = new string[0], Keys = new[] { "account:home" } },
            };
        }

        UIElement BuildBrandHeader()
        {
            var bar = new Grid();
            bar.Height = 40;
            bar.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            var left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            left.Margin = new Thickness(16, 0, 0, 0);
            left.VerticalAlignment = VerticalAlignment.Center;
            var appName = new TextBlock();
            appName.Text = BrandName;
            appName.Foreground = Palette.B(Palette.Text);
            appName.FontSize = 13;
            appName.FontWeight = FontWeights.SemiBold;
            var hil = new TextBlock();
            hil.Text = "HIL";
            hil.Foreground = Palette.B(Palette.Dim);
            hil.FontSize = 9;
            hil.Margin = new Thickness(6, 0, 0, 0);
            hil.VerticalAlignment = VerticalAlignment.Bottom;
            var ver = new TextBlock();
            ver.Text = Version;
            ver.Foreground = Palette.B(Palette.Dim);
            ver.FontSize = 9;
            ver.Margin = new Thickness(6, 0, 0, 0);
            ver.VerticalAlignment = VerticalAlignment.Bottom;
            var betaBrush = new SolidColorBrush(ThemeManager.CurrentColor);
            ThemeManager.ColorChanged += delegate { betaBrush.Color = ThemeManager.CurrentColor; };
            var beta = new Border();
            beta.Background = betaBrush;
            beta.CornerRadius = new CornerRadius(4);
            beta.Padding = new Thickness(5, 1, 5, 2);
            beta.Margin = new Thickness(6, 0, 0, 0);
            beta.VerticalAlignment = VerticalAlignment.Bottom;
            var betaText = new TextBlock();
            betaText.Text = Channel;
            betaText.Foreground = Palette.B(Colors.White);
            betaText.FontSize = 8;
            betaText.FontWeight = FontWeights.Bold;
            beta.Child = betaText;
            left.Children.Add(appName);
            left.Children.Add(hil);
            left.Children.Add(ver);
            left.Children.Add(beta);
            bar.Children.Add(left);
            return bar;
        }

        UIElement BuildWindowControls()
        {
            var bar = new Grid();
            bar.Height = 40;
            bar.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            var titleBg = new Grid();
            bar.Children.Add(titleBg);
            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Top;
            var btnMin = MakeTitleButton("—", Palette.BtnHover, new CornerRadius(0));
            btnMin.Click += delegate { WindowState = WindowState.Minimized; };
            btnMin.Margin = new Thickness(0, 0, 10, 0);
            btnMin.GlassSource = titleBg;
            var btnClose = MakeTitleButton("✕", Palette.Red, new CornerRadius(0, 12, 0, 0));
            btnClose.Click += delegate { Close(); };
            btnClose.GlassSource = titleBg;
            right.Children.Add(btnMin);
            right.Children.Add(btnClose);
            bar.Children.Add(right);
            return bar;
        }

        UIElement BuildTwoCardShell(UIElement rightBody)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            g.ColumnDefinitions.Add(UiHelpers.StarCol());

            var sidebar = (Grid)BuildSidebar();

            var leftStack = new Grid();
            leftStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftStack.RowDefinitions.Add(UiHelpers.StarRow());
            leftStack.Children.Add(BuildBrandHeader());
            Grid.SetRow(sidebar, 1);
            leftStack.Children.Add(sidebar);

            var leftPanel = FrameVerticalPanel(leftStack);
            leftPanel.Margin = new Thickness(0, 6, 0, 10);
            g.Children.Add(leftPanel);

            var rightStack = new Grid();
            rightStack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightStack.RowDefinitions.Add(UiHelpers.StarRow());
            rightStack.Children.Add(BuildWindowControls());
            Grid.SetRow(rightBody, 1);
            rightStack.Children.Add(rightBody);

            var rightPanel = FrameVerticalPanel(rightStack);
            rightPanel.Margin = new Thickness(0, 6, 10, 10);
            Grid.SetColumn(rightPanel, 2);
            g.Children.Add(rightPanel);
            return g;
        }

        Border FrameVerticalPanel(UIElement child)
        {
            var b = new Border();
            b.CornerRadius = new CornerRadius(12);
            b.BorderBrush = ThemeManager.Accent;
            b.BorderThickness = new Thickness(1);
            b.Background = null;   // 背景由 bgLayer 的圆角裁切提供，卡本身只留边框
            b.Child = child;
            b.SizeChanged += delegate
            {
                b.Clip = new RectangleGeometry(new Rect(0, 0, b.ActualWidth, b.ActualHeight), 12, 12);
            };
            return b;
        }

        UIElement BuildRail()
        {
            var host = new Grid();
            host.Margin = new Thickness(0, 6, 0, 10);
            var stack = new StackPanel();
            stack.VerticalAlignment = VerticalAlignment.Top;
            stack.Margin = new Thickness(0, 6, 0, 0);
            string[] icons = new string[]
            {
                "M12 3 L12 15 M7 10 L12 15 L17 10 M4 20 L20 20",
                "M12 2 L14.5 4 L17.5 3.5 L18.5 6.5 L21.5 7.5 L21 10.5 L22 12 L21 13.5 L21.5 16.5 L18.5 17.5 L17.5 20.5 L14.5 20 L12 22 L9.5 20 L6.5 20.5 L5.5 17.5 L2.5 16.5 L3 13.5 L2 12 L3 10.5 L2.5 7.5 L5.5 6.5 L6.5 3.5 L9.5 4 Z M12 8 A4 4 0 1 0 12 16 A4 4 0 1 0 12 8",
                "M8 11 A4 4 0 1 0 8 3 A4 4 0 1 0 8 11 M2 21 L2 19 A6 6 0 0 1 14 19 L14 21 M16 4 A4 4 0 0 1 16 12 M22 21 L22 19 A6 6 0 0 0 18 13",
                "M12 12 A4 4 0 1 0 12 4 A4 4 0 1 0 12 12 M4 21 L4 19 A4 4 0 0 1 8 15 L16 15 A4 4 0 0 1 20 19 L20 21"
            };
            railLabels = new TextBlock[navGroups.Length];
            railIcons = new Path[navGroups.Length];
            railPill = new Border();
            railPill.Width = 58;
            railPill.Height = 48;
            railPill.CornerRadius = new CornerRadius(12);
            railPill.HorizontalAlignment = HorizontalAlignment.Center;
            railPill.VerticalAlignment = VerticalAlignment.Top;
            railPill.Margin = new Thickness(0, 10, 0, 0);
            railPill.IsHitTestVisible = false;
            railPillTransform = new TranslateTransform(0, 0);
            railPill.RenderTransform = railPillTransform;
            host.Children.Add(railPill);
            for (int i = 0; i < navGroups.Length; i++)
            {
                int gi = i;
                var item = new Grid();
                item.Height = 56;
                item.Cursor = Cursors.Hand;
                item.Background = Brushes.Transparent;   // 整块 72×56 可点，不再只有文字和描边
                var box = new StackPanel();
                box.HorizontalAlignment = HorizontalAlignment.Center;
                box.VerticalAlignment = VerticalAlignment.Center;
                box.IsHitTestVisible = false;           // 点击统一交给 item
                var p = new Path();
                p.Data = Geometry.Parse(icons[i]);
                p.Stroke = Palette.B(Palette.Dim);
                p.StrokeThickness = 1.6;
                p.StrokeStartLineCap = PenLineCap.Round;
                p.StrokeEndLineCap = PenLineCap.Round;
                p.Width = 20; p.Height = 20;
                p.Stretch = Stretch.Uniform;
                p.HorizontalAlignment = HorizontalAlignment.Center;
                railIcons[i] = p;
                box.Children.Add(p);
                var lb = new TextBlock();
                lb.Text = navGroups[i].Name;
                lb.FontSize = 11;
                lb.Foreground = Palette.B(Palette.Dim);
                lb.HorizontalAlignment = HorizontalAlignment.Center;
                lb.Margin = new Thickness(0, 3, 0, 0);
                railLabels[i] = lb;
                box.Children.Add(lb);
                item.Children.Add(box);
                item.MouseLeftButtonUp += delegate
                {
                    SelectGroupVertical(gi, 0);
                    Navigate(navGroups[gi].Keys[0]);
                };
                UiMap.Register("tab:" + navGroups[i].Name, delegate { return item; });
                stack.Children.Add(item);
            }
            host.Children.Add(stack);
            return host;
        }

        UIElement BuildVerticalRight()
        {
            var g = new Grid();
            g.Margin = new Thickness(0, 6, 14, 10);
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.RowDefinitions.Add(UiHelpers.StarRow());
            verticalSecondaryBar = new Border();
            verticalSecondaryBar.Height = 40;
            verticalSecondaryBar.CornerRadius = new CornerRadius(8);
            verticalSecondaryBar.Visibility = Visibility.Collapsed;
            verticalSecondary = new StackPanel();
            verticalSecondary.Orientation = Orientation.Horizontal;
            verticalSecondary.VerticalAlignment = VerticalAlignment.Center;
            verticalSecondary.Margin = new Thickness(4, 0, 4, 0);
            verticalPill = new Border();
            verticalPill.Height = 30;
            verticalPill.CornerRadius = new CornerRadius(7);
            verticalPill.HorizontalAlignment = HorizontalAlignment.Left;
            verticalPill.VerticalAlignment = VerticalAlignment.Center;
            verticalPill.Visibility = Visibility.Collapsed;
            verticalPill.IsHitTestVisible = false;
            verticalPillTransform = new TranslateTransform(0, 0);
            verticalPill.RenderTransform = verticalPillTransform;
            var barGrid = new Grid();
            barGrid.Children.Add(verticalPill);
            barGrid.Children.Add(verticalSecondary);
            Panel.SetZIndex(verticalSecondary, 1);
            verticalSecondaryBar.Child = barGrid;
            g.Children.Add(verticalSecondaryBar);
            contentHost = new Grid();
            Grid.SetRow(contentHost, 1);
            g.Children.Add(contentHost);
            // 下载管理悬浮球照旧盖在内容右下角
            var ball = BuildFloatBall();
            Grid.SetRowSpan(ball, 2);
            g.Children.Add(ball);
            return g;
        }

        void SelectGroupVertical(int idx, int subIdx)
        {
            activeGroup = idx;
            if (railLabels != null)
            {
                for (int i = 0; i < railLabels.Length; i++)
                {
                    bool on = i == idx;
                    railLabels[i].Foreground = Palette.B(on ? Palette.Text : Palette.Dim);
                    railLabels[i].FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                    railIcons[i].Stroke = Palette.B(on ? ThemeManager.CurrentColor : Palette.Dim);
                }
            }
            StyleRailPill();
            MoveRailPill(idx);
            verticalButtons.Clear();
            verticalSecondary.Children.Clear();
            var labels = idx >= 0 ? navGroups[idx].Labels : new string[0];
            var keys = idx >= 0 ? navGroups[idx].Keys : new string[0];
            verticalSecondaryBar.Visibility = labels.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            for (int k = 0; k < labels.Length; k++)
            {
                int ki = k;
                var b = new FlatButton(labels[k]);
                b.TabInGlass = true;
                b.SetFontSize(13);
                b.SetPadding(new Thickness(12, 6, 12, 6));
                b.SetCornerRadius(new CornerRadius(6));
                b.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0),
                    k == subIdx ? Palette.Text : Palette.Dim, Palette.Text);
                b.Click += delegate { Navigate(keys[ki]); };
                verticalButtons.Add(b);
                verticalSecondary.Children.Add(b);
                string subKey = keys[k];
                string subLabel = labels[k];
                UiMap.Register("sub:" + subLabel, delegate { return b; });
            }
            StyleVerticalPill();
            int first = labels.Length == 0 ? -1 : Math.Max(0, Math.Min(subIdx, labels.Length - 1));
            Dispatcher.BeginInvoke(new Action(delegate { MoveVerticalPill(first, true); }), DispatcherPriority.Loaded);
        }

        void StyleRailPill()
        {
            if (railPill == null) return;
            if (Frost.On)
            {
                railPill.Background = Frost.TileStrong;
                railPill.BorderBrush = Frost.Edge;
                railPill.BorderThickness = new Thickness(1);
            }
            else if (Glass.On)
            {
                railPill.Background = Glass.SimulatedFace(0.10);
                railPill.BorderBrush = Glass.Edge();
                railPill.BorderThickness = new Thickness(1);
            }
            else
            {
                Color c = ThemeManager.CurrentColor;
                railPill.Background = new SolidColorBrush(Color.FromArgb(30, c.R, c.G, c.B));
                railPill.BorderThickness = new Thickness(0);
            }
        }

        void MoveRailPill(int idx)
        {
            if (railPill == null) return;
            if (idx < 0) { railPill.Visibility = Visibility.Collapsed; return; }
            railPill.Visibility = Visibility.Visible;
            double y = idx * 56.0;
            if (!railPillInit)
            {
                railPillSpring.SnapTo(y);
                railPillTransform.Y = y;
                railPillInit = true;
            }
            else
            {
                SpringEngine.Go(railPillSpring, y, delegate(double v, double vel) { railPillTransform.Y = v; });
            }
        }

        void StyleVerticalPill()
        {
            if (verticalPill == null) return;
            if (Frost.On)
            {
                verticalPill.Background = Frost.TileStrong;
                verticalPill.BorderBrush = Frost.Edge;
                verticalPill.BorderThickness = new Thickness(1);
            }
            else if (Glass.On)
            {
                verticalPill.Background = Glass.SimulatedFace(0.12);
                verticalPill.BorderBrush = Glass.Edge();
                verticalPill.BorderThickness = new Thickness(1);
            }
            else
            {
                Color c = ThemeManager.CurrentColor;
                verticalPill.Background = new SolidColorBrush(Color.FromArgb(30, c.R, c.G, c.B));
                verticalPill.BorderThickness = new Thickness(0);
            }
        }

        void MoveVerticalPill(int idx, bool animate)
        {
            if (verticalPill == null || verticalSecondaryBar == null) return;
            if (idx < 0 || idx >= verticalButtons.Count)
            {
                verticalPill.Visibility = Visibility.Collapsed;
                return;
            }
            var b = verticalButtons[idx];
            if (b.ActualWidth <= 0)
            {
                verticalPill.Visibility = Visibility.Collapsed;
                int again = idx; bool anim = animate;
                Dispatcher.BeginInvoke(new Action(delegate { MoveVerticalPill(again, anim); }), DispatcherPriority.Loaded);
                return;
            }
            double x = b.TranslatePoint(new Point(0, 0), verticalSecondaryBar).X;
            double w = b.ActualWidth;
            verticalPill.Width = w;
            verticalPill.Visibility = Visibility.Visible;
            if (!animate || !verticalPillInit)
            {
                verticalPillSpring.SnapTo(x);
                verticalPillTransform.X = x;
                verticalPillInit = true;
            }
            else
            {
                SpringEngine.Go(verticalPillSpring, x, delegate(double v, double vel) { verticalPillTransform.X = v; });
            }
        }

        void UpdateVerticalSelection(int ii)
        {
            for (int i = 0; i < verticalButtons.Count; i++)
            {
                var b = verticalButtons[i];
                b.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0),
                    i == ii ? Palette.Text : Palette.Dim, Palette.Text);
                if (i == ii) b.SetBold(true); else b.SetBold(false);
            }
            if (verticalSecondaryBar != null)
                verticalSecondaryBar.Visibility = verticalButtons.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StyleVerticalPill();
            MoveVerticalPill(ii, true);
        }

        // 左侧栏：品牌区 / 用户名 / 当前版本 / H史聊天框 / 三个圆形按钮
        UIElement BuildSidebar()
        {
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());   // 气泡
            g.RowDefinitions.Add(UiHelpers.AutoRow());   // 按钮组

            var brand = new StackPanel();
            brand.Margin = new Thickness(24, 15, 0, 0);
            var sub = new TextBlock();
            sub.Text = "社区驱动 · 离线模式 · HIL · By MAX";
            sub.Foreground = Palette.B(Palette.Dim);
            sub.FontSize = 11;
            brand.Children.Add(sub);
            g.Children.Add(brand);

            userCombo = new DarkComboBox();
            userCombo.IsEditable = true;
            userCombo.DisplayMemberPath = "Name";   // 账号选择器：条目是 Account，显示名字
            userCombo.Margin = new Thickness(24, 15, 24, 0);
            userCombo.ItemsSource = AccountStore.All();
            var selAcc = AccountStore.Selected();
            userCombo.Text = selAcc != null ? selAcc.Name : cfg.Username;
            userCombo.SelectionChanged += delegate
            {
                var a = userCombo.SelectedItem as Account;
                if (a != null) { AccountStore.Select(a); cfg.Username = a.Name; }
            };
            userCombo.LostFocus += delegate { if (userCombo.Text != null) cfg.Username = userCombo.Text; };
            Grid.SetRow(userCombo, 1);
            g.Children.Add(userCombo);

            versionText = new TextBlock();
            versionText.Foreground = Palette.B(Palette.Dim);
            versionText.FontSize = 10.5;
            versionText.Margin = new Thickness(24, 15, 24, 0);
            versionText.TextTrimming = TextTrimming.CharacterEllipsis;
            RefreshVersionText();
            Grid.SetRow(versionText, 2);
            g.Children.Add(versionText);

            // H史聊天框：用户名框正下方，同宽同高同风格；仅 AiEnabled 时显示
            chatWrap = BuildChatBox();
            Grid.SetRow(chatWrap, 3);
            g.Children.Add(chatWrap);

            // 史蒂夫层：只装 2D 镜像位图，作为左栏玻璃件的透镜源——
            // VisualBrush 刷 Viewport3D 在 GPU 实时路径会把整场景塞进画刷块（按钮里看到上半身而非正下方的腿），
            // 所以透镜吃镜像；显示层是下面这个实时 3D 史蒂夫，同矩形、后添加压在镜像上面
            steveLayer = new Grid();
            Grid.SetRow(steveLayer, 4);
            Grid.SetRowSpan(steveLayer, 3);   // 延伸到气泡/按钮行后面，按钮才能透出史蒂夫
            g.Children.Add(steveLayer);
            // 史蒂夫：填充按钮上方的空区，头跟鼠标转、点击挥臂
            steveView = new SteveView();
            steveView.Margin = new Thickness(0, 6, 0, 12);   // 只留一点余量：头不碰聊天框即可
            steveView.MirrorImg.Margin = steveView.Margin;
            steveLayer.Children.Add(steveView.MirrorImg);
            // 镜像只是玻璃透镜的取图源：关玻璃时它停在最后一帧，会从透明视口底下透出成「第二个史蒂夫」——必须藏掉
            steveView.MirrorImg.Visibility = Glass.On ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetRow(steveView, 4);
            Grid.SetRowSpan(steveView, 3);   // 与 steveLayer 同跨度
            g.Children.Add(steveView);   // 保持在按钮下层：按钮透出史蒂夫，脚也完整
            ThemeManager.GlassChanged += delegate
            {
                steveView.MirrorImg.Visibility = Glass.On ? Visibility.Visible : Visibility.Collapsed;
                if (Glass.On) steveView.RefreshMirror();
            };

            btnStack = new StackPanel();
            btnStack.VerticalAlignment = VerticalAlignment.Bottom;
            btnStack.Margin = new Thickness(22, 0, 22, 26);

            // 上面一个（实例管理：选择/详情/运行中全在里面），下面一个大的（启动），PCL 式圆角矩形
            // 两枚按钮同尺寸（字号/内边距/圆角一致），视觉对等
            var btnInstances = new FlatButton("实例管理");
            btnInstances.GlassFixed = true;   // 侧栏固定铬件：活底透镜（底下是史蒂夫层，实时）
            btnInstances.GlassSource = steveLayer;
            btnInstances.SetFontSize(18);
            btnInstances.SetBold(true);   // 脚会搭到按钮上：加粗防腿影干扰读字（与启动按钮对等）
            // 透明玻璃面：让下面的史蒂夫透出来，不再是一块实心白
            btnInstances.SetColors(Color.FromArgb(56, 255, 255, 255), Color.FromArgb(110, 255, 255, 255),
                Palette.Text, Palette.Text);
            btnInstances.SetPadding(new Thickness(0, 14, 0, 14));
            btnInstances.SetCornerRadius(new CornerRadius(10));
            btnInstances.Click += delegate { Navigate("instance:list"); };
            btnStack.Children.Add(btnInstances);

            btnLaunch = new FlatButton("启动！！");
            btnLaunch.GlassFixed = true;
            // 启动按钮在史蒂夫视口下方：steveLayer 在那里是空的（取了=纯平透明框）；
            // 史莱姆开时改取史莱姆活底（色块从按钮底下流过），史莱姆关才退回 steveLayer
            btnLaunch.GlassSource = Glass.BgLensSource != null ? Glass.BgLensSource : (Visual)steveLayer;
            btnLaunch.SetFontSize(18);
            btnLaunch.SetBold(true);
            btnLaunch.SetPadding(new Thickness(0, 14, 0, 14));
            btnLaunch.SetCornerRadius(new CornerRadius(10));
            // 文字色随明暗：浅色模式黑字（白字趴在浅磨砂上看不见），深色模式白字
            btnLaunch.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.15),
                Palette.Light ? Colors.Black : Colors.White, Palette.Light ? Colors.Black : Colors.White);
            btnLaunch.Margin = new Thickness(0, 12, 0, 0);
            // 主题换色跟随：重算按钮配色并渐变（主题色光晕投影已撤：核显帧率杀手）
            ThemeManager.ColorChanged += delegate
            {
                var c = ThemeManager.CurrentColor;
                btnLaunch.AnimateColors(c, Palette.Lighten(c, 0.15),
                    Palette.Light ? Colors.Black : Colors.White, Palette.Light ? Colors.Black : Colors.White, 300);
            };
            btnLaunch.Click += delegate { LaunchGame(); };
            btnStack.Children.Add(btnLaunch);
            Grid.SetRow(btnStack, 6);
            Panel.SetZIndex(btnStack, 10);
            g.Children.Add(btnStack);

            // H史回复气泡：浮在「实例管理」按钮上方 12px，最后添加在最上层
            bubble = BuildBubble();
            Grid.SetRow(bubble, 5);
            Panel.SetZIndex(bubble, 11);
            g.Children.Add(bubble);
            // 玻璃切换重建气泡脸（场上泡不动，下条新泡用新脸）
            ThemeManager.GlassChanged += delegate
            {
                if (bubbleVisible) return;
                g.Children.Remove(bubble);
                bubble = BuildBubble();
                Grid.SetRow(bubble, 5);
                Panel.SetZIndex(bubble, 11);
                g.Children.Add(bubble);
            };
            return g;
        }

        // 聊天输入框：黑底白字圆角，与用户名框同宽；回车发送
        Border BuildChatBox()
        {
            var wrap = new Border();
            wrap.Background = Palette.B(Palette.Input);
            wrap.BorderBrush = Palette.B(Palette.Line);
            wrap.BorderThickness = new Thickness(1);
            wrap.CornerRadius = new CornerRadius(4);
            wrap.Margin = new Thickness(24, 15, 24, 0);
            wrap.MinHeight = 30;
            var grid = new Grid();
            chatInput = new TextBox();
            chatInput.Background = new SolidColorBrush(Colors.Transparent);
            chatInput.Foreground = Palette.B(Palette.Text);
            chatInput.BorderThickness = new Thickness(0);
            chatInput.Padding = new Thickness(9, 6, 9, 6);
            chatInput.FontSize = 12;
            chatInput.CaretBrush = Palette.B(Palette.Text);
            chatInput.VerticalContentAlignment = VerticalAlignment.Center;
            chatHint = new TextBlock();
            chatHint.Text = "和 H史 聊天…";
            chatHint.Foreground = Palette.B(Palette.Dim);
            chatHint.FontSize = 12;
            chatHint.IsHitTestVisible = false;
            chatHint.VerticalAlignment = VerticalAlignment.Center;
            chatHint.Margin = new Thickness(10, 0, 0, 0);
            grid.Children.Add(chatInput);
            grid.Children.Add(chatHint);
            chatInput.TextChanged += delegate
            {
                chatHint.Visibility = chatInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            chatInput.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; SendChat(); }
            };
            wrap.Child = grid;
            wrap.Visibility = cfg.AiEnabled ? Visibility.Visible : Visibility.Collapsed;
            return wrap;
        }

        void SendChat()
        {
            if (chatBusy) return;
            string t = (chatInput.Text ?? "").Trim();
            if (t.Length == 0) return;
            chatInput.Text = "";
            SetChatBusy(true);
            AiSteve.Send(t);
        }

        // 发送中：输入框禁用 + 占位提示变「思考中…」
        void SetChatBusy(bool b)
        {
            chatBusy = b;
            chatInput.IsEnabled = !b;
            chatHint.Text = b ? "思考中…" : "和 H史 聊天…";
            chatHint.Visibility = chatInput.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 设置页改动 AI 配置后：聊天框动态显隐（不用重启）
        void RefreshAiVisibility()
        {
            chatWrap.Visibility = cfg.AiEnabled ? Visibility.Visible : Visibility.Collapsed;
            if (!cfg.AiEnabled && bubbleVisible) CloseBubble(true);
        }

        // 回复气泡：主题色底白字圆角，最大宽同左栏，最多 6 行省略；✕ 左淡出关闭
        Border BuildBubble()
        {
            Border b;
            Grid bubbleHost = null;   // 玻璃：多环最内层的内容位
            if (Glass.On)
            {
                // 活底气泡（多环折射）：底 = 史蒂夫层实时画面；源透明处 = 真透视（底下的按钮直接透出来）
                Grid host;
                var rings = Glass.LensRings(steveLayer, 20, Glass.BandPx, out host);
                if (rings != null)
                {
                    b = rings;   // 边缘受光由 LensRings 顶层 rim 负责（投影已全局撤除）
                    bubbleHost = host;
                }
                else
                {
                    b = new Border();
                    b.Background = ThemeManager.Accent;
                }
            }
            else
            {
                b = new Border();
                b.Background = ThemeManager.Accent;   // 共享画刷，换主题自动跟随
            }
            b.CornerRadius = Glass.On ? new CornerRadius(20) : new CornerRadius(8);   // 玻璃=2.5× 大圆角
            b.Padding = Glass.On ? new Thickness(0) : new Thickness(10, 8, 10, 8);
            b.Margin = new Thickness(24, 0, 24, 12);
            b.MaxWidth = 232;
            b.VerticalAlignment = VerticalAlignment.Bottom;
            b.Opacity = 0;
            b.Visibility = Visibility.Collapsed;

            var grid = new Grid();
            grid.RowDefinitions.Add(UiHelpers.AutoRow());
            grid.RowDefinitions.Add(UiHelpers.AutoRow());
            bubbleText = new TextBlock();
            bubbleText.Foreground = Palette.B(Colors.White);
            bubbleText.FontSize = 12;
            bubbleText.TextWrapping = TextWrapping.Wrap;
            // 超长不截断：外套平滑滚动区，约 6 行高，超出滚轮滚动（代替省略号）
            bubbleScroll = new SmoothScrollViewer();
            bubbleScroll.MaxHeight = 100;
            bubbleScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            bubbleScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            bubbleScroll.Content = bubbleText;
            bubbleScroll.Margin = new Thickness(0, 0, 14, 0);
            grid.Children.Add(bubbleScroll);
            var close = new TextBlock();
            close.Text = "✕";
            close.Foreground = Palette.B(Color.FromArgb(200, 255, 255, 255));
            close.FontSize = 10;
            close.Cursor = Cursors.Hand;
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.MouseLeftButtonUp += delegate { CloseBubble(true); };
            grid.Children.Add(close);

            // 确认按钮行（主题色气泡上的白描边小按钮）
            bubbleBtns = new StackPanel();
            bubbleBtns.Orientation = Orientation.Horizontal;
            bubbleBtns.HorizontalAlignment = HorizontalAlignment.Right;
            bubbleBtns.Margin = new Thickness(0, 8, 0, 0);
            bubbleBtns.Visibility = Visibility.Collapsed;
            var btnYes = MakeBubbleButton("好");
            btnYes.Click += delegate { DecideBubble(true); };
            var btnNo = MakeBubbleButton("算了");
            btnNo.Click += delegate { DecideBubble(false); };
            bubbleBtns.Children.Add(btnYes);
            bubbleBtns.Children.Add(btnNo);
            Grid.SetRow(bubbleBtns, 1);
            grid.Children.Add(bubbleBtns);

            if (bubbleHost != null)
            {
                // 玻璃叠层进多环最内层：主题色罩（跟随换肤）+ 高光罩，padding 用内容格 Margin 实现
                grid.Margin = new Thickness(10, 8, 10, 8);
                double ir = bubbleHost.Tag is double ? (double)bubbleHost.Tag : 16;
                var tintBrush = new SolidColorBrush(Color.FromArgb(205,
                    ThemeManager.CurrentColor.R, ThemeManager.CurrentColor.G, ThemeManager.CurrentColor.B));
                ThemeManager.ColorChanged += delegate
                {
                    var c = ThemeManager.CurrentColor;
                    tintBrush.Color = Color.FromArgb(205, c.R, c.G, c.B);
                };
                var tint = new SquircleBorder();   // 叠层与最内环同曲线，角落不露第二道边
                tint.Background = tintBrush;
                tint.CornerRadius = new CornerRadius(ir);
                var sheen = new SquircleBorder();
                sheen.Background = Glass.Sheen();
                sheen.CornerRadius = new CornerRadius(ir);
                sheen.IsHitTestVisible = false;
                bubbleHost.Children.Add(tint);
                bubbleHost.Children.Add(sheen);
                bubbleHost.Children.Add(grid);
            }
            else b.Child = grid;
            return b;
        }

        FlatButton MakeBubbleButton(string text)
        {
            var b = new FlatButton(text);
            b.SetFontSize(11);
            b.SetPadding(new Thickness(12, 4, 12, 4));
            b.SetCornerRadius(new CornerRadius(6));
            b.SetBorder(Colors.White);
            b.SetColors(Color.FromArgb(40, 255, 255, 255), Color.FromArgb(90, 255, 255, 255), Colors.White, Colors.White);
            b.Margin = new Thickness(8, 0, 0, 0);
            return b;
        }

        // 弹气泡：新泡关旧泡（场上最多一条）；进场 = 下滑 + 淡入
        void ShowBubble(string text, bool withConfirm, Action<bool> decision)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string, bool, Action<bool>>(ShowBubble), text, withConfirm, decision);
                return;
            }
            HilLog.Write("气泡: " + (withConfirm ? "[确认] " : "") +
                (text != null && text.Length > 80 ? text.Substring(0, 80) + "..." : text));
            try
            {
                ShowBubbleCore(text, withConfirm, decision);
            }
            catch (Exception ex)
            {
                // 动画链路出问题也不能让用户看不见回复：无动画兜底
                HilLog.Write("气泡动画失败，走无动画兜底: " + ex);
                try
                {
                    Ani.Stop("aibubble");
                    bubbleDecision = decision;
                    bubbleText.Text = text;
                    bubbleBtns.Visibility = withConfirm ? Visibility.Visible : Visibility.Collapsed;
                    Ani.Ts(bubble).X = 0;
                    Ani.Ts(bubble).Y = 0;
                    bubble.Opacity = 1;
                    bubble.Visibility = Visibility.Visible;
                    bubbleVisible = true;
                }
                catch (Exception ex2)
                {
                    HilLog.Write("气泡兜底也失败: " + ex2);
                }
            }
        }

        void ShowBubbleCore(string text, bool withConfirm, Action<bool> decision)
        {
            // 硬切：停掉旧动画直接换内容。不用"关旧泡→回调里开新泡"的回调链，杜绝中断后卡死
            Ani.Stop("aibubble");
            bubbleDecision = decision;
            bubbleText.Text = text;
            bubbleBtns.Visibility = withConfirm ? Visibility.Visible : Visibility.Collapsed;
            // 位置由 sidebar 的行结构保证：气泡 row5、按钮 row6，不再按按钮高度反推 margin
            bubbleVisible = true;
            bubble.Visibility = Visibility.Visible;
            var tf = Ani.Ts(bubble);
            tf.X = 0;
            tf.Y = 28;   // 从按钮那里淡入、向上浮起到位停住
            var bsc = Ani.Sc(bubble);
            bsc.ScaleX = 1;
            bsc.ScaleY = 1;
            bubble.Opacity = 0;
            if (Glass.On)
                Ani.Start("aibubble",
                    Ani.AaOpacity(bubble, 1, 200, new AniEaseOutFluent(Ani.Weak)),
                    Ani.AaY(bubble, -28, 300, new AniEaseOutBack(Ani.Weak)),
                    // 水珠落地：到位后竖向轻压扁再回稳
                    Ani.Aa(delegate(double d) { bsc.ScaleY += d; }, -0.05, 90, new AniEaseInFluent(Ani.Weak), 0, true),
                    Ani.Aa(delegate(double d) { bsc.ScaleY += d; }, 0.05, 240, new AniEaseOutBack(Ani.Weak), 0, true));
            else
                Ani.Start("aibubble",
                    Ani.AaOpacity(bubble, 1, 200, new AniEaseOutFluent(Ani.Weak)),
                    Ani.AaY(bubble, -28, 300, new AniEaseOutBack(Ani.Weak)));
        }

        void DecideBubble(bool agree)
        {
            var d = bubbleDecision;
            bubbleDecision = null;
            CloseBubble(true);
            if (d != null) d(agree);
        }

        // ✕ / 确认后关闭：向左淡出
        void CloseBubble(bool animated) { CloseBubble(animated, null); }

        void CloseBubble(bool animated, Action after)
        {
            if (!bubbleVisible) { if (after != null) after(); return; }
            bubbleVisible = false;
            bubbleDecision = null;
            bubbleScroll.MaxHeight = 100;   // 帮助泡的加高在关闭时复位，不影响后续聊天泡
            if (!animated)
            {
                Ani.Stop("aibubble");
                bubble.Opacity = 0;
                bubble.Visibility = Visibility.Collapsed;
                if (after != null) after();
                return;
            }
            Ani.Start("aibubble",
                Ani.AaX(bubble, -18 - Ani.Ts(bubble).X, 200, new AniEaseInFluent(Ani.Weak)),
                Ani.AaOpacity(bubble, -bubble.Opacity, 200),
                Ani.AaCode(delegate
                {
                    bubble.Visibility = Visibility.Collapsed;
                    Ani.Ts(bubble).X = 0;
                    if (after != null) after();
                }));
        }

        // 右侧：一级标签行 + (二级边栏 | 页面宿主)
        UIElement BuildRightSide()
        {
            var g = new Grid();
            g.Margin = new Thickness(0, 6, 14, 10);
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());

            navGroups = new[]
            {
                new NavGroup
                {
                    Name = "下载",
                    Labels = new[] { "原版游戏", "模组", "数据包", "资源包", "光影包", "整合包" },
                    Keys = new[] { "download:install", "download:mod", "download:datapack", "download:resourcepack", "download:shader", "download:pack" }
                },
                new NavGroup
                {
                    Name = "设置",
                    Labels = new[] { "游戏设置", "启动器设置" },
                    Keys = new[] { "settings:game", "settings:launcher" }
                },
                new NavGroup
                {
                    Name = "联机",
                    Labels = new[] { "一键开服", "联机大厅", "服务器列表" },
                    Keys = new[] { "server:oneclick", "server:hall", "server:list" }
                },
                new NavGroup
                {
                    Name = "账号",
                    Labels = new string[0],   // 无二级项：页内三栏自给自足，二级栏整列隐藏
                    Keys = new[] { "account:home" }
                },
            };

            // 一级标签行：固定宽度单元 + 底部滑动指示条（单元宽 72，指示条宽 30，居中偏移 21）
            var topRow = new Grid();
            topRow.Height = 36;
            topRow.Margin = new Thickness(6, 0, 0, 4);
            var tabStack = new StackPanel();
            tabStack.Orientation = Orientation.Horizontal;
            groupTabs = new TextBlock[navGroups.Length];
            groupCells = new Border[navGroups.Length];
            for (int i = 0; i < navGroups.Length; i++)
            {
                int gi = i;
                var cell = new Border();
                cell.Width = 72;
                cell.Cursor = Cursors.Hand;
                var t = new TextBlock();
                t.Text = navGroups[i].Name;
                t.FontSize = 15;
                t.Foreground = Palette.B(Palette.Dim);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                cell.Child = t;
                cell.MouseLeftButtonUp += delegate
                {
                    // 不早退：即使该组已是当前组，也要强制导航回组首页——
                    // 游离页（实例管理/详情）期间 activeGroup 不变，早退会让人回不去
                    SelectGroup(gi);
                    Navigate(navGroups[gi].Keys[0]);
                };
                groupTabs[i] = t;
                groupCells[i] = cell;
                tabStack.Children.Add(cell);
            }
            // H史 UiMap：一级标签 + 二级标签（二级随组切换重建，resolver 按当前侧栏索引实时取）
            for (int i = 0; i < navGroups.Length; i++)
            {
                int gi = i;
                UiMap.Register("tab:" + navGroups[gi].Name, delegate { return groupCells[gi]; });
                for (int k = 0; k < navGroups[gi].Labels.Length; k++)
                {
                    string subKey = navGroups[gi].Keys[k];
                    UiMap.Register("sub:" + navGroups[gi].Labels[k], delegate
                    {
                        int idx = sideKeys.IndexOf(subKey);
                        return idx >= 0 && idx < sideItems.Count ? (FrameworkElement)sideItems[idx] : null;
                    });
                }
            }
            this.tabStack = tabStack;   // 存字段：一级 pill 的活底来源（史莱姆关时）
            groupArena = topRow;        // 一级 pill 拖动取坐标的容器
            // 液态玻璃 pill：裹住标签的活底多环玻璃块，弹簧滑动+果冻形变+可抓取拖动（玻璃关时隐藏，用底部指示条）
            groupPill = new SquircleBorder();
            groupPill.Width = 58;
            groupPill.Height = 28;
            groupPill.CornerRadius = new CornerRadius(100);   // 全胶囊
            groupPill.HorizontalAlignment = HorizontalAlignment.Left;
            groupPill.VerticalAlignment = VerticalAlignment.Center;
            groupPill.Cursor = Cursors.Hand;
            groupPillTf = new TranslateTransform(7, 0);
            groupPillSc = new ScaleTransform(1, 1);
            var pillTg = new TransformGroup();
            pillTg.Children.Add(groupPillSc);
            pillTg.Children.Add(groupPillTf);
            groupPill.RenderTransform = pillTg;
            groupPill.RenderTransformOrigin = new Point(0.5, 0.5);
            groupPill.Visibility = (Glass.On || Frost.On) ? Visibility.Visible : Visibility.Collapsed;
            groupPill.Opacity = 1;
            groupPillSpring = new Spring(7) { Stiffness = 200, Damping = 13 };
            BuildGroupPillFace();
            // 流光：pill 压在文字上（透镜副本）；霜面：pill 在文字背后，文字保持清晰
            if (Frost.On) topRow.Children.Add(groupPill);
            topRow.Children.Add(tabStack);
            if (Frost.On)
            {
                Panel.SetZIndex(groupPill, 0);   // 霜面：磨砂框在文字背后
                Panel.SetZIndex(tabStack, 1);
            }
            else
            {
                Panel.SetZIndex(groupPill, 2);   // 流光：透镜 pill 必须压在文字上，否则折射副本重影
                Panel.SetZIndex(tabStack, 0);
            }
            // iOS 式抓取拖动：抓住放大、拖动果冻、松手弹簧吸附最近标签并切换
            groupPill.MouseLeftButtonDown += OnGroupPillDown;
            groupPill.MouseMove += OnGroupPillMove;
            groupPill.MouseLeftButtonUp += OnGroupPillUp;
            if (!Frost.On) topRow.Children.Add(groupPill);
            groupIndicator = new Border();
            groupIndicator.Height = 2.5;
            groupIndicator.Width = 30;
            groupIndicator.CornerRadius = new CornerRadius(1.5);
            groupIndicator.Background = ThemeManager.Accent;
            groupIndicator.HorizontalAlignment = HorizontalAlignment.Left;
            groupIndicator.VerticalAlignment = VerticalAlignment.Bottom;
            groupIndicator.IsHitTestVisible = false;
            groupIndicator.Visibility = (Glass.On || Frost.On) ? Visibility.Collapsed : Visibility.Visible;
            groupIndTf = new TranslateTransform(21, 0);
            groupIndicator.RenderTransform = groupIndTf;
            topRow.Children.Add(groupIndicator);
            g.Children.Add(topRow);
            // 玻璃切换：pill 与底部指示条互换可见性（pill 归位到当前组），并重挂活底脸
            ThemeManager.GlassChanged += delegate
            {
                bool on = Glass.On || Frost.On;
                groupPill.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                groupIndicator.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                if (on)
                {
                    BuildGroupPillFace();
                    if (activeGroup >= 0)
                    {
                        groupPillSpring.SnapTo(activeGroup * 72 + 7);
                        groupPillTf.X = groupPillSpring.Value;
                    }
                }
            };

            // 中部：二级边栏 + 页面宿主
            var mid = new Grid();
            var sideColDef = new ColumnDefinition { Width = new GridLength(118) };
            sideCol = sideColDef;   // 存字段：游离页（实例管理等）时归零
            mid.ColumnDefinitions.Add(sideColDef);
            mid.ColumnDefinitions.Add(UiHelpers.StarCol());
            sideList = new StackPanel();
            sideItems.Clear();
            sideKeys.Clear();
            sideList.Margin = new Thickness(2, 0, 8, 0);
            // 玻璃模式共享滑动选中 pill：全胶囊，快照透镜底 + 折射环 + 高光罩；可抓取拖动（iOS 式）
            sidePill = new SquircleBorder();
            sidePill.Height = 34;
            sidePill.CornerRadius = new CornerRadius(100);   // 拉满 → 胶囊分支（两端标准半圆）
            sidePill.VerticalAlignment = VerticalAlignment.Top;
            sidePill.Margin = new Thickness(2, 0, 8, 0);
            sidePill.Cursor = Cursors.Hand;
            sidePillTf = new TranslateTransform(0, 0);
            sidePillSc = new ScaleTransform(1, 1);
            var sidePillTg = new TransformGroup();
            sidePillTg.Children.Add(sidePillSc);
            sidePillTg.Children.Add(sidePillTf);
            sidePill.RenderTransform = sidePillTg;
            sidePill.RenderTransformOrigin = new Point(0.5, 0.5);
            if (Glass.On || Frost.On) BuildPillFace();
            sidePill.Visibility = (Glass.On || Frost.On) ? Visibility.Visible : Visibility.Collapsed;
            ThemeManager.GlassChanged += delegate
            {
                sidePill.Visibility = (Glass.On || Frost.On) ? Visibility.Visible : Visibility.Collapsed;
                if (Glass.On || Frost.On) BuildPillFace();
            };
            var sideWrap = new Grid();
            // 流光：pill 压文字上；霜面：pill 在文字背后
            sidePill.MouseLeftButtonDown += OnPillDown;
            sidePill.MouseMove += OnPillMove;
            sidePill.MouseLeftButtonUp += OnPillUp;
            if (Frost.On) sideWrap.Children.Add(sidePill);
            sideWrap.Children.Add(sideList);
            if (!Frost.On) sideWrap.Children.Add(sidePill);
            sideWrapPanel = sideWrap;   // 存字段：实例/账号详情等不在一级组的页面要整列隐藏
            mid.Children.Add(sideWrap);
            contentHost = new Grid();
            Grid.SetColumn(contentHost, 1);
            mid.Children.Add(contentHost);
            Grid.SetRow(mid, 1);
            g.Children.Add(mid);
            return g;
        }

        // 一级标签切换：指示条滑动 + 二级列表瀑布重建（25ms/项）
        void SelectGroup(int idx) { SelectGroup(idx, true); }

        void SelectGroup(int idx, bool buildSide)
        {
            if (ThemeManager.IsVertical) { SelectGroupVertical(idx, 0); return; }
            if (activeGroup == idx && sideItems.Count > 0) return;
            activeGroup = idx;
            for (int i = 0; i < groupTabs.Length; i++)
            {
                var brush = groupTabs[i].Foreground as SolidColorBrush;
                if (brush == null)
                {
                    brush = Palette.B(Palette.Dim);
                    groupTabs[i].Foreground = brush;
                }
                Ani.Start("gt" + i, Ani.AaColor(brush, i == idx ? Palette.Text : Palette.Dim, 150));
                groupTabs[i].FontWeight = i == idx ? FontWeights.SemiBold : FontWeights.Normal;
            }
            if (Glass.On || Frost.On)
            {
                // 玻璃/霜面 pill 弹簧滑动（速度接果冻：移动拉长、到位挤扁回弹）；活底透镜逐帧跟齐
                SpringEngine.Go(groupPillSpring, idx * 72 + 7, ApplyGroupPillSpring);
            }
            else
            {
                double targetX = idx * 72 + 21;
                Ani.Start("gind", Ani.Aa(delegate(double d) { groupIndTf.X += d; },
                    targetX - groupIndTf.X, 250, new AniEaseOutBack(Ani.Weak)));
            }
            if (!buildSide) return;   // 账号/游离页没有二级项：保留旧栏内容到 swap，避免 70ms 空栏闪
            sideItems.Clear();
            sideKeys.Clear();
            sideList.Children.Clear();
            var g = navGroups[idx];
            for (int k = 0; k < g.Labels.Length; k++)
            {
                string key = g.Keys[k];
                var item = new SideTabItem(g.Labels[k]);
                item.Click += delegate { Navigate(key); };
                item.Opacity = 0;
                Ani.Ts(item).X = -16;
                int delay = k * 25;
                Ani.Start("side" + item.GetHashCode(),
                    Ani.AaOpacity(item, 1, 100, new AniEaseOutFluent(Ani.Weak), delay),
                    Ani.AaX(item, 5, 200, new AniEaseOutFluent(Ani.Middle), delay),
                    Ani.AaX(item, 11, 300, new AniEaseOutBack(Ani.Weak), delay));
                sideItems.Add(item);
                sideKeys.Add(key);
                sideList.Children.Add(item);
            }
        }

        // 下载管理悬浮球：有任务时出现在侧栏右下角，白字显示总进度百分比，单击进下载管理页
        UIElement BuildFloatBall()
        {
            var g = new Grid();
            g.Width = 52;
            g.Height = 52;
            g.HorizontalAlignment = HorizontalAlignment.Right;
            g.VerticalAlignment = VerticalAlignment.Bottom;
            g.Margin = new Thickness(0, 0, 12, 12);   // 贴主窗右下角
            var el = new Ellipse();
            el.Fill = ThemeManager.Accent;   // 共享画刷，换主题自动跟随
            g.Children.Add(el);
            // 液态玻璃：径向高光罩（光源方向随鼠标在窗口里的位置视差偏移）
            ballSheenBrush = new RadialGradientBrush();
            ballSheenBrush.GradientStops.Add(new GradientStop(Color.FromArgb(96, 0xFF, 0xFF, 0xFF), 0));
            ballSheenBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 1));
            ballSheenBrush.RadiusX = 0.75;
            ballSheenBrush.RadiusY = 0.75;
            ballSheenBrush.Center = new Point(0.35, 0.3);
            ballSheenBrush.GradientOrigin = new Point(0.35, 0.3);
            ballSheen = new Ellipse();
            ballSheen.Fill = ballSheenBrush;
            ballSheen.IsHitTestVisible = false;
            ballSheen.Visibility = Glass.On ? Visibility.Visible : Visibility.Collapsed;
            g.Children.Add(ballSheen);
            ThemeManager.GlassChanged += delegate
            { ballSheen.Visibility = Glass.On ? Visibility.Visible : Visibility.Collapsed; };
            MouseMove += delegate(object s, MouseEventArgs e)
            {
                if (!ballVisible || !Glass.On || rootBorder == null) return;
                try
                {
                    var mp = e.GetPosition(rootBorder);
                    var bp = g.TransformToAncestor(rootBorder).Transform(new Point(26, 26));
                    double dx = (mp.X - bp.X) / 400, dy = (mp.Y - bp.Y) / 400;
                    if (dx < -0.5) dx = -0.5; if (dx > 0.5) dx = 0.5;
                    if (dy < -0.5) dy = -0.5; if (dy > 0.5) dy = 0.5;
                    ballSheenBrush.Center = new Point(0.5 + dx * 0.4, 0.5 + dy * 0.4);
                    ballSheenBrush.GradientOrigin = ballSheenBrush.Center;
                }
                catch { }
            };
            floatText = new TextBlock();
            floatText.Foreground = Palette.B(Colors.White);
            floatText.FontSize = 13;
            floatText.FontWeight = FontWeights.Bold;
            floatText.HorizontalAlignment = HorizontalAlignment.Center;
            floatText.VerticalAlignment = VerticalAlignment.Center;
            floatText.IsHitTestVisible = false;
            g.Children.Add(floatText);
            g.Cursor = Cursors.Hand;
            g.Visibility = Visibility.Collapsed;
            g.ToolTip = "下载中…单击查看下载管理";
            // 悬停微放大（独立动画组，不与出现/消失动画打架）
            g.MouseEnter += delegate { if (ballVisible) Ani.Start("fballh", Ani.AaScale(g, 1.06 - Ani.Sc(g).ScaleX, 120, new AniEaseOutFluent(Ani.Strong))); };
            g.MouseLeave += delegate { if (ballVisible) Ani.Start("fballh", Ani.AaScale(g, 1.0 - Ani.Sc(g).ScaleX, 250, new AniEaseOutBack(Ani.Weak))); };
            g.MouseLeftButtonUp += delegate { Navigate("download:speed"); };
            floatBall = g;
            // 任务事件可能密集：合并到最多一个挂起项，Background 优先级不插队输入
            TaskCenter.Changed += delegate
            {
                if (ballUpdatePending) return;
                ballUpdatePending = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    ballUpdatePending = false;
                    UpdateFloatBall();
                }));
            };
            return g;
        }

        void UpdateFloatBall()
        {
            double p, m;
            int files;
            TaskCenter.Overall(out p, out m, out files);
            int active = 0;
            lock (TaskCenter.Tasks)
                foreach (var t in TaskCenter.Tasks) if (!t.Finished) active++;
            if (active > 0 && !ballVisible)
            {
                ballVisible = true;
                floatBall.Visibility = Visibility.Visible;
                floatBall.Opacity = 0;
                var s = Ani.Sc(floatBall);
                s.ScaleX = 0.5;
                s.ScaleY = 0.5;
                Ani.Start("fball",
                    Ani.AaOpacity(floatBall, 1, 150),
                    Ani.Aa(delegate(double d) { s.ScaleX += d; s.ScaleY += d; }, 0.5, 350, new AniEaseOutBack(Ani.Middle)));
            }
            else if (active == 0 && ballVisible)
            {
                ballVisible = false;
                Ani.Start("fball",
                    Ani.AaOpacity(floatBall, -floatBall.Opacity, 150),
                    Ani.AaScale(floatBall, 0.5 - Ani.Sc(floatBall).ScaleX, 180, new AniEaseInFluent(Ani.Weak)),
                    Ani.AaCode(delegate { floatBall.Visibility = Visibility.Collapsed; }));
            }
            if (ballVisible && (DateTime.Now - lastBallText).TotalMilliseconds > 120)
            {
                lastBallText = DateTime.Now;
                floatText.Text = ((int)(p * 100)).ToString() + "%";
            }
        }

        // ===== 页面导航（Navigator.Handler 的实现）=====
        void Navigate(string key) { Navigate(key, null); }

        void Navigate(string key, object param)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string, object>(Navigate), key, param);
                return;
            }
            curNavKey = key;
            curNavParam = param;
            UIElement next;
            if (!pages.TryGetValue(key, out next))
            {
                next = CreatePage(key);
                if (next == null) { ShowToast("未知页面: " + key); return; }
                pages[key] = next;
            }
            UiWatchdog.Context = key;   // 哨兵日志带上页面上下文
            // 同一页且没有换页动画在跑 → 只刷新数据；动画在跑时点当前页不能走捷径，
            // 否则旧组到点会把页面 swap 成之前的目标页（点 A→B→A 会落回 B）
            if (ReferenceEquals(currentPage, next) && !Ani.IsRun("pageSwap"))
            {
                var same = next as PageBase;
                if (same != null)
                {
                    same.NavParam = param;
                    DeferredEnter(same);
                }
                return;
            }
            var oldPb = currentPage as PageBase;
            if (oldPb != null) oldPb.OnLeave();
            UIElement oldPage = currentPage;
            // 换页 + 入场（PCL 配方：旧页 70ms 淡出上飘退出，新页双段位移回弹进入）
            // 关键：退场在 "pageSwap" 组、入场在 "pageSwapEnter" 组，入场于 swap 内启动。
            // 狂点时旧组整组被替换/停止，新旧动画永不同时驱动同一元素的同一属性（掉帧元凶）
            Action swap = delegate
            {
                if (sideWrapPanel != null) sideWrapPanel.Visibility = pendingSideVisible ? Visibility.Visible : Visibility.Collapsed;
                if (sideCol != null) sideCol.Width = new GridLength(pendingSideWidth);
                contentHost.Children.Clear();
                next.Opacity = 0;
                next.RenderTransform = new TranslateTransform(0, 16);   // 统一从下往上，杜绝页面自带横向位移
                Ani.Ts(next).Y = 16;
                contentHost.Children.Add(next);
                currentPage = next;
                var pb = next as PageBase;
                if (pb != null)
                {
                    pb.Cfg = cfg;
                    pb.NavParam = param;
                    DeferredEnter(pb);
                }
                // 入场组在 swap 内启动：保证 Opacity/Y 复位后才驱动，且只作用于"新进页"
                Ani.Start("pageSwapEnter",
                    Ani.AaOpacity(next, 1, 100),
                    Ani.AaY(next, -5, 250, new AniEaseOutFluent(Ani.Middle)),
                    Ani.AaY(next, -11, 350, new AniEaseOutBack(Ani.Middle)));
            };
            UpdateNavHighlight(key);
            // 上一次换页的入场若还在跑，停掉（其目标即将退场）；退场组由同名 Start 替换
            Ani.Stop("pageSwapEnter");
            var seq = new List<AniData>();
            if (oldPage != null)
            {
                // 统一退场：先清掉页面可能残留的横向/缩放变换，只走 Y 轴，避免账号页等横向滑出
                oldPage.RenderTransform = new TranslateTransform(0, 0);
                seq.Add(Ani.AaOpacity(oldPage, -oldPage.Opacity, 70));
                seq.Add(Ani.AaY(oldPage, -6, 70, new AniEaseInFluent(Ani.Weak)));
            }
            seq.Add(Ani.AaCode(swap));
            Ani.Start("pageSwap", seq.ToArray());
        }

        // OnEnter 里常同步构建整页卡片（装版页/大厅/开服页），压到后台优先级：
        // 入场动画先跑起来，重活排在渲染之后执行；期间又切走则作废
        void DeferredEnter(PageBase pb)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                if (ReferenceEquals(currentPage, pb)) pb.OnEnter();
            }));
        }

        UIElement CreatePage(string key)
        {
            switch (key)
            {
                case "download:install": return new PageDownloadInstall();
                case "download:mod": return new PageDownloadMod();
                case "download:pack": return new PageDownloadPack();
                case "download:datapack": return new PageDownloadDataPack();
                case "download:resourcepack": return new PageDownloadResourcePack();
                case "download:shader": return new PageDownloadShader();
                case "download:speed": return new PageDownloadSpeed();
                case "download:detail": return new PageResourceDetail();
                case "instance:overall": return new PageInstanceOverall();
                case "instance:setup": return new PageInstanceSetup();
                case "instance:mod": return new PageInstanceMod();
                case "instance:saves": return new PageInstanceSaves();
                case "instance:export": return new PageInstanceExport();
                case "instance:list": return new PageInstanceList();
                case "account:home": return new PageAccount();
                case "settings:game": return new SettingsGamePanel(cfg);
                case "settings:launcher": return new SettingsLauncherPanel(cfg);
                case "server:oneclick": return new PageServerOneClick();
                case "server:hall": return new PageServerHall();
                case "server:list": return new PageServerLobby();
            }
            return null;
        }

        // 按页面 key 反查所属组：切换一级（如需）并高亮对应二级项
        void UpdateNavHighlight(string key)
        {
            int gi = -1, ii = -1;
            for (int g = 0; g < navGroups.Length && gi < 0; g++)
                for (int k = 0; k < navGroups[g].Keys.Length; k++)
                    if (navGroups[g].Keys[k] == key) { gi = g; ii = k; break; }
            // 资源详情/下载管理等无二级项的页面归入下载组
            if (gi < 0 && key != null && key.StartsWith("download:")) gi = 0;
            // 游离页面（实例管理/实例详情等不在任何一级组）：整列二级边栏隐藏并归零列宽；
            // 组存在但没有二级项（账号）同样隐藏
            // 纵向布局：竖轨 + 横向二级，不走 118 列
            if (ThemeManager.IsVertical)
            {
                if (gi < 0)
                {
                    if (verticalSecondaryBar != null) verticalSecondaryBar.Visibility = Visibility.Collapsed;
                    return;
                }
                bool vSide = navGroups[gi].Labels.Length > 0;
                if (gi != activeGroup || verticalButtons.Count == 0) SelectGroupVertical(gi, ii > 0 ? ii : 0);
                else UpdateVerticalSelection(ii);
                if (verticalSecondaryBar != null)
                    verticalSecondaryBar.Visibility = vSide ? Visibility.Visible : Visibility.Collapsed;
                return;
            }
            bool sideVisible = gi >= 0 && navGroups[gi].Labels.Length > 0;
            // 不在页面还在场上时改列宽：先记下来，等旧页淡出、swap 那一帧再收/放，
            // 新页直接以最终宽度入场，避免内容整体横移，也不给账号页留空列。
            pendingSideVisible = sideVisible;
            pendingSideWidth = sideVisible ? 118 : 0;
            // 游离页时顶部一级指示框（玻璃 pill / 底部指示条）也藏起来，离开才恢复
            bool topIndOn = gi >= 0;
            if (groupPill != null) groupPill.Visibility = topIndOn && (Glass.On || Frost.On) ? Visibility.Visible : Visibility.Collapsed;
            if (groupIndicator != null) groupIndicator.Visibility = topIndOn && !(Glass.On || Frost.On) ? Visibility.Visible : Visibility.Collapsed;
            if (gi >= 0 && (gi != activeGroup || sideItems.Count == 0)) SelectGroup(gi, sideVisible);
            if (gi >= 0 && navGroups[gi].Labels.Length == 0) ii = -1;   // 无二级项的组：选中 pill 也别亮
            for (int i = 0; i < sideItems.Count; i++) sideItems[i].SetSelected(i == ii);
            // 玻璃共享 pill 弹簧滑动到选中项（移动中纵向拉伸果冻）；无选中项或非玻璃模式隐藏
            if (sidePill != null)
            {
                if (ii >= 0 && (Glass.On || Frost.On))
                {
                    sidePill.Visibility = Visibility.Visible;
                    if (!pillDragging) SpringEngine.Go(sidePillSpring, ii * 34.0, ApplyPillSpring);
                }
                else sidePill.Visibility = Visibility.Collapsed;
            }
        }

        // 二级 pill 玻璃脸（活底多环）：刷侧栏文字层的实时画面——全透明无磨砂，
        // 只有透镜 + 顶部淡高光 + 边缘受光；拖动滑过文字时文字连续弯曲（无接缝环）
        void BuildPillFace()
        {
            if (Frost.On)
            {
                if (sidePill == null) return;
                sidePill.Child = null;
                sidePill.Background = Frost.TileStrong;
                sidePill.BorderBrush = Frost.Edge;
                sidePill.BorderThickness = new Thickness(1);
                return;
            }
            if (!Glass.On || sidePill == null) return;
            Grid host;
            var rings = Glass.LensRings(sideList, 100, Glass.BandPx, out host, true);
            if (rings == null) { sidePill.Background = Glass.SimulatedFace(0.08); return; }
            sidePill.Background = null;   // 投影已全局撤除，边缘受光靠 LensRings 顶层 rim
            var sheen = new SquircleBorder();
            sheen.Background = Glass.Sheen();
            sheen.CornerRadius = new CornerRadius(100);
            sheen.IsHitTestVisible = false;
            host.Children.Add(sheen);
            sidePill.Child = rings;
        }

        // 一级 pill 玻璃脸（活底多环）：刷一级标签文字层；倒影淡（新版 Sheen 10% 起 30% 收）
        void BuildGroupPillFace()
        {
            if (Frost.On)
            {
                if (groupPill == null) return;
                groupPill.Child = null;
                groupPill.Background = Frost.TileStrong;
                groupPill.BorderBrush = Frost.Edge;
                groupPill.BorderThickness = new Thickness(1);
                return;
            }
            if (!Glass.On || groupPill == null || tabStack == null) return;
            Grid host;
            var rings = Glass.LensRings(tabStack, 100, Glass.BandPx, out host, true);
            if (rings == null) { groupPill.Background = Glass.SimulatedFace(0.10); return; }
            groupPill.Background = null;   // 边缘受光由 LensRings 顶层 rim 负责
            var sheen = new SquircleBorder();
            sheen.Background = Glass.Sheen();
            sheen.CornerRadius = new CornerRadius(100);
            sheen.IsHitTestVisible = false;
            host.Children.Add(sheen);
            groupPill.Child = rings;
        }

        // ---- 一级 pill 拖动（iOS：抓住放大、拖动果冻、松手弹簧吸附最近标签并切换）----
        void ComposeGroupPillScale()
        {
            if (groupPillSc == null) return;
            groupPillSc.ScaleX = gPillGrabK * (1 + gPillJelly);
            groupPillSc.ScaleY = gPillGrabK * (1 - gPillJelly * 0.5);
        }

        void ApplyGroupPillSpring(double v, double vel)
        {
            groupPillTf.X = v;
            gPillJelly = Math.Min(0.14, Math.Abs(vel) * 0.0006);
            ComposeGroupPillScale();
        }

        void OnGroupPillDown(object sender, MouseButtonEventArgs e)
        {
            if (!Glass.On || navGroups == null || navGroups.Length == 0) return;
            gPillDragging = true;
            groupPill.CaptureMouse();
            gPillGrabX = e.GetPosition(groupArena).X - groupPillTf.X;
            gPillDownX = groupPillTf.X;
            gPillLastX = groupPillTf.X;
            gPillLastTick = e.Timestamp;
            SpringEngine.Detach(groupPillSpring);   // 拖动期间弹簧脱钩，位置全由手决定
            SpringEngine.Go(groupPillGrab, 1.08, delegate(double v, double vel) { gPillGrabK = v; ComposeGroupPillScale(); });
            e.Handled = true;
        }

        void OnGroupPillMove(object sender, MouseEventArgs e)
        {
            if (!gPillDragging) return;
            double maxX = Math.Max(0, (navGroups.Length - 1) * 72.0) + 7;
            double x = e.GetPosition(groupArena).X - gPillGrabX;
            // 端点阻力：越界 0.3 系数，最多 10px（iOS 橡皮筋手感）
            if (x < 7) x = Math.Max(-3, 7 - (7 - x) * 0.3);
            if (x > maxX) x = maxX + Math.Min(10, (x - maxX) * 0.3);
            int tick = e.Timestamp;
            double dt = (tick - gPillLastTick) / 1000.0;
            if (dt > 0.001)
                gPillJelly = Math.Min(0.18, Math.Abs((x - gPillLastX) / dt) * 0.0004);   // 拖动速度→横向拉伸
            gPillLastX = x; gPillLastTick = tick;
            groupPillSpring.SnapTo(x);   // 松手时弹簧从当前位置续走，不跳
            groupPillTf.X = x;
            ComposeGroupPillScale();
            e.Handled = true;
        }

        void OnGroupPillUp(object sender, MouseButtonEventArgs e)
        {
            if (!gPillDragging) return;
            gPillDragging = false;
            groupPill.ReleaseMouseCapture();
            SpringEngine.Go(groupPillGrab, 1.0, delegate(double v, double vel) { gPillGrabK = v; ComposeGroupPillScale(); });
            double maxX = Math.Max(0, (navGroups.Length - 1) * 72.0) + 7;
            double x = groupPillTf.X;
            if (x < 7) x = 7; if (x > maxX) x = maxX;
            int ii = (int)Math.Round((x - 7) / 72.0);
            if (ii < 0) ii = 0;
            if (ii >= navGroups.Length) ii = navGroups.Length - 1;
            SpringEngine.Go(groupPillSpring, ii * 72 + 7, ApplyGroupPillSpring);   // 弹簧吸附最近标签
            // 拖到别的标签才切换；原地点击（位移<4）不动
            if (Math.Abs(x - gPillDownX) > 4 && ii != activeGroup)
                SelectGroup(ii);
            e.Handled = true;
        }

        // ---- 一级 pill 拖动（史莱姆开时文字层版：pill 压在文字下面，手势改挂文字层）----
        // 阈值捕获：按下只记录，位移>4 才真正捕获进入拖动；普通点击照旧冒泡给 cell.Click 导航
        void OnGroupTabDown(object sender, MouseButtonEventArgs e)
        {
            if (!Glass.On || navGroups == null || navGroups.Length == 0) return;
            gTabPending = true;
            gTabStartX = e.GetPosition(groupArena).X;
            gPillDownX = groupPillTf.X;
            // 不 capture、不 Handled：让普通点击正常落到标签上
        }

        void OnGroupTabMove(object sender, MouseEventArgs e)
        {
            if (!gPillDragging)
            {
                if (!gTabPending || e.LeftButton != MouseButtonState.Pressed) return;
                if (Math.Abs(e.GetPosition(groupArena).X - gTabStartX) <= 4) return;
                // 超阈值：进入拖动，后续逻辑与 pill 直拖一致
                gTabPending = false;
                gPillDragging = true;
                tabStack.CaptureMouse();
                gPillGrabX = gTabStartX - gPillDownX;
                gPillLastX = groupPillTf.X;
                gPillLastTick = e.Timestamp;
                SpringEngine.Detach(groupPillSpring);   // 拖动期间弹簧脱钩，位置全由手决定
                SpringEngine.Go(groupPillGrab, 1.08, delegate(double v, double vel) { gPillGrabK = v; ComposeGroupPillScale(); });
            }
            double maxX = Math.Max(0, (navGroups.Length - 1) * 72.0) + 7;
            double x = e.GetPosition(groupArena).X - gPillGrabX;
            // 端点阻力：越界 0.3 系数，最多 10px（iOS 橡皮筋手感）
            if (x < 7) x = Math.Max(-3, 7 - (7 - x) * 0.3);
            if (x > maxX) x = maxX + Math.Min(10, (x - maxX) * 0.3);
            int tick = e.Timestamp;
            double dt = (tick - gPillLastTick) / 1000.0;
            if (dt > 0.001)
                gPillJelly = Math.Min(0.18, Math.Abs((x - gPillLastX) / dt) * 0.0004);   // 拖动速度→横向拉伸
            gPillLastX = x; gPillLastTick = tick;
            groupPillSpring.SnapTo(x);   // 松手时弹簧从当前位置续走，不跳
            groupPillTf.X = x;
            ComposeGroupPillScale();
            e.Handled = true;
        }

        void OnGroupTabUp(object sender, MouseButtonEventArgs e)
        {
            gTabPending = false;
            if (!gPillDragging) return;   // 普通点击：cell.Click 已处理导航
            gPillDragging = false;
            tabStack.ReleaseMouseCapture();
            SpringEngine.Go(groupPillGrab, 1.0, delegate(double v, double vel) { gPillGrabK = v; ComposeGroupPillScale(); });
            double maxX = Math.Max(0, (navGroups.Length - 1) * 72.0) + 7;
            double x = groupPillTf.X;
            if (x < 7) x = 7; if (x > maxX) x = maxX;
            int ii = (int)Math.Round((x - 7) / 72.0);
            if (ii < 0) ii = 0;
            if (ii >= navGroups.Length) ii = navGroups.Length - 1;
            SpringEngine.Go(groupPillSpring, ii * 72 + 7, ApplyGroupPillSpring);   // 弹簧吸附最近标签
            // 拖到别的标签才切换；原地点击（位移<4）不动
            if (Math.Abs(x - gPillDownX) > 4 && ii != activeGroup)
                SelectGroup(ii);
            e.Handled = true;
        }

        // 抓住放大倍率 × 果冻分量 → 合成 pill 缩放（两来源各存分量不打架）
        void ComposePillScale()
        {
            if (sidePillSc == null) return;
            sidePillSc.ScaleY = pillGrabK * (1 + pillJelly);
            sidePillSc.ScaleX = pillGrabK * (1 - pillJelly * 0.5);
        }

        // pill 弹簧应用：位移 + 果冻（活底透镜由逐帧对齐循环跟齐）
        void ApplyPillSpring(double v, double vel)
        {
            sidePillTf.Y = v;
            pillJelly = Math.Min(0.14, Math.Abs(vel) * 0.0006);
            ComposePillScale();
        }

        // ---- pill 拖动（iOS：抓住放大、拖动果冻、松手弹簧吸附最近标签并导航）----
        void OnPillDown(object sender, MouseButtonEventArgs e)
        {
            if (!Glass.On || sideItems.Count == 0) return;
            pillDragging = true;
            sidePill.CaptureMouse();
            pillGrabY = e.GetPosition(sideList).Y - sidePillTf.Y;
            pillDownY = sidePillTf.Y;
            pillLastY = sidePillTf.Y;
            pillLastTick = e.Timestamp;
            SpringEngine.Detach(sidePillSpring);   // 拖动期间弹簧脱钩，位置全由手决定
            SpringEngine.Go(sidePillGrab, 1.08, delegate(double v, double vel) { pillGrabK = v; ComposePillScale(); });
            e.Handled = true;
        }

        void OnPillMove(object sender, MouseEventArgs e)
        {
            if (!pillDragging) return;
            double maxY = Math.Max(0, (sideItems.Count - 1) * 34.0);
            double y = e.GetPosition(sideList).Y - pillGrabY;
            // 端点阻力：越界 0.3 系数，最多 10px（iOS 橡皮筋手感）
            if (y < 0) y = Math.Max(-10, y * 0.3);
            if (y > maxY) y = maxY + Math.Min(10, (y - maxY) * 0.3);
            int tick = e.Timestamp;
            double dt = (tick - pillLastTick) / 1000.0;
            if (dt > 0.001)
                pillJelly = Math.Min(0.18, Math.Abs((y - pillLastY) / dt) * 0.0004);   // 拖动速度→纵向拉伸
            pillLastY = y; pillLastTick = tick;
            sidePillSpring.SnapTo(y);   // 松手时弹簧从当前位置续走，不跳
            sidePillTf.Y = y;
            ComposePillScale();
            e.Handled = true;
        }

        void OnPillUp(object sender, MouseButtonEventArgs e)
        {
            if (!pillDragging) return;
            pillDragging = false;
            sidePill.ReleaseMouseCapture();
            SpringEngine.Go(sidePillGrab, 1.0, delegate(double v, double vel) { pillGrabK = v; ComposePillScale(); });
            double maxY = Math.Max(0, (sideItems.Count - 1) * 34.0);
            double y = sidePillTf.Y;
            if (y < 0) y = 0; if (y > maxY) y = maxY;
            int ii = (int)Math.Round(y / 34.0);
            if (ii < 0) ii = 0;
            if (ii >= sideItems.Count) ii = sideItems.Count - 1;
            SpringEngine.Go(sidePillSpring, ii * 34.0, ApplyPillSpring);   // 弹簧吸附最近标签
            // 拖到别的标签才导航；原地点击（位移<4）不动
            if (Math.Abs(y - pillDownY) > 4 && ii >= 0 && ii < sideKeys.Count)
                Navigate(sideKeys[ii]);
            e.Handled = true;
        }

        // ---- 二级 pill 拖动（史莱姆开时文字层版：pill 压在文字下面，手势改挂 sideList）----
        // 阈值捕获：位移>4 才进入拖动；普通点击照旧由 SideTabItem 自己处理
        void OnSideListDown(object sender, MouseButtonEventArgs e)
        {
            if (!Glass.On || sideItems.Count == 0) return;
            sTabPending = true;
            sTabStartY = e.GetPosition(sideList).Y;
            pillDownY = sidePillTf.Y;
            // 不 capture、不 Handled：让普通点击正常落到标签上
        }

        void OnSideListMove(object sender, MouseEventArgs e)
        {
            if (!pillDragging)
            {
                if (!sTabPending || e.LeftButton != MouseButtonState.Pressed) return;
                if (Math.Abs(e.GetPosition(sideList).Y - sTabStartY) <= 4) return;
                // 超阈值：进入拖动，后续逻辑与 pill 直拖一致
                sTabPending = false;
                pillDragging = true;
                sideList.CaptureMouse();
                pillGrabY = sTabStartY - pillDownY;
                pillLastY = sidePillTf.Y;
                pillLastTick = e.Timestamp;
                SpringEngine.Detach(sidePillSpring);   // 拖动期间弹簧脱钩，位置全由手决定
                SpringEngine.Go(sidePillGrab, 1.08, delegate(double v, double vel) { pillGrabK = v; ComposePillScale(); });
            }
            double maxY = Math.Max(0, (sideItems.Count - 1) * 34.0);
            double y = e.GetPosition(sideList).Y - pillGrabY;
            // 端点阻力：越界 0.3 系数，最多 10px（iOS 橡皮筋手感）
            if (y < 0) y = Math.Max(-10, y * 0.3);
            if (y > maxY) y = maxY + Math.Min(10, (y - maxY) * 0.3);
            int tick = e.Timestamp;
            double dt = (tick - pillLastTick) / 1000.0;
            if (dt > 0.001)
                pillJelly = Math.Min(0.18, Math.Abs((y - pillLastY) / dt) * 0.0004);   // 拖动速度→纵向拉伸
            pillLastY = y; pillLastTick = tick;
            sidePillSpring.SnapTo(y);   // 松手时弹簧从当前位置续走，不跳
            sidePillTf.Y = y;
            ComposePillScale();
            e.Handled = true;
        }

        void OnSideListUp(object sender, MouseButtonEventArgs e)
        {
            sTabPending = false;
            if (!pillDragging) return;   // 普通点击：SideTabItem.Click 已处理导航
            pillDragging = false;
            sideList.ReleaseMouseCapture();
            SpringEngine.Go(sidePillGrab, 1.0, delegate(double v, double vel) { pillGrabK = v; ComposePillScale(); });
            double maxY = Math.Max(0, (sideItems.Count - 1) * 34.0);
            double y = sidePillTf.Y;
            if (y < 0) y = 0; if (y > maxY) y = maxY;
            int ii = (int)Math.Round(y / 34.0);
            if (ii < 0) ii = 0;
            if (ii >= sideItems.Count) ii = sideItems.Count - 1;
            SpringEngine.Go(sidePillSpring, ii * 34.0, ApplyPillSpring);   // 弹簧吸附最近标签
            // 拖到别的标签才导航；原地点击（位移<4）不动
            if (Math.Abs(y - pillDownY) > 4 && ii >= 0 && ii < sideKeys.Count)
                Navigate(sideKeys[ii]);
            e.Handled = true;
        }

        // ===== Toast（PageBase.ToastHandler 的实现）=====
        void ShowToast(string msg)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action<string>(ShowToast), msg);
                return;
            }
            toastText.Text = msg;
            // 弹出：淡入 + 从下方 6px 回弹归位；同名组互斥，连续提示自然续接
            var ty = Ani.Ts(toastBox);
            if (!ThemeManager.AnimOn)
            {
                toastBox.Opacity = 1;
                ty.Y = 0;
            }
            else
            {
                if (toastBox.Opacity < 0.01) ty.Y = 6;
                Ani.Start("toast",
                    Ani.AaOpacity(toastBox, 1 - toastBox.Opacity, 150),
                    Ani.AaY(toastBox, -ty.Y, 300, new AniEaseOutBack(Ani.Weak)));
            }
            if (toastTimer == null)
            {
                toastTimer = new DispatcherTimer();
                toastTimer.Interval = TimeSpan.FromMilliseconds(2600);
                toastTimer.Tick += delegate
                {
                    toastTimer.Stop();
                    if (!ThemeManager.AnimOn)
                    {
                        toastBox.Opacity = 0;
                        Ani.Ts(toastBox).Y = 0;
                    }
                    else
                    {
                        Ani.Start("toast",
                            Ani.AaOpacity(toastBox, -toastBox.Opacity, 300),
                            Ani.AaY(toastBox, 6 - Ani.Ts(toastBox).Y, 300, new AniEaseInFluent(Ani.Weak)));
                    }
                };
            }
            toastTimer.Stop();
            toastTimer.Start();
        }

        // 入场动画：淡入 250ms + 整体上移 14px 回弹 600ms（PCL 主窗口配方）
        void OnLoaded(object sender, RoutedEventArgs e)
        {
            RefreshVersionText();
            Navigate("download:install");
            FloatBall.Sync(cfg);   // 启动=全窗模式，不出球；注册配置备用
            // 液态玻璃活底：启动逐帧对齐循环（幂等；不再需要截屏/去抖/排除注册）
            Glass.Init(rootBorder);
            if (ThemeManager.AnimOn)
            {
                Opacity = 0;
                enterTransform.Y = 14;
                Ani.Start("windowEnter",
                    Ani.AaOpacity(this, 1, 250, new AniEaseOutFluent(Ani.Weak), 100),
                    Ani.Aa(delegate(double d) { enterTransform.Y += d; }, -14, 600, new AniEaseOutBack(Ani.Weak), 100));
            }
            else
            {
                Opacity = 1;
                enterTransform.Y = 0;
            }
            // 自动调色：开张就对一次天色；之后定时器每 5 分钟看一眼
            if (cfg.AutoDayNight) EvalAutoDayNight();
            // 自动更新：后台静默比对，发现新版才弹气泡；失败不打扰
            if (cfg.AutoUpdate)
                Updater.CheckAsync(Version,
                    delegate(string v)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { Updater.NotifyNewVersion(v); }));
                    }, null, null);
        }

        // 自动调色：白天（7:00 - 19:00）亮色，其余暗色；关掉开关时顺手收掉定时器
        DispatcherTimer dayNightTimer;

        void EvalAutoDayNight()
        {
            if (!cfg.AutoDayNight)
            {
                if (dayNightTimer != null) dayNightTimer.Stop();
                return;
            }
            if (dayNightTimer == null)
            {
                dayNightTimer = new DispatcherTimer();
                dayNightTimer.Interval = TimeSpan.FromMinutes(5);
                dayNightTimer.Tick += delegate { EvalAutoDayNight(); };
            }
            dayNightTimer.Start();   // 幂等
            int h = DateTime.Now.Hour;
            SwitchSkin(h >= 7 && h < 19);
        }

        // 发现新版本：气泡先问，同意后后台下载 → bat 接力自替换重启
        void OnUpdateFound(string newVer)
        {
            ShowBubble("发现新版本 " + newVer + "（当前 " + Version + "）\n\n要现在更新吗？更新会自动下载、替换并重启启动器。", true,
                delegate(bool agree)
                {
                    if (!agree) return;
                    ShowToast("正在下载新版本 " + newVer + " ...");
                    Updater.DownloadAndSwapAsync(null,
                        delegate(string err)
                        {
                            Dispatcher.BeginInvoke(new Action(delegate { ShowToast("更新失败：" + err); }));
                        },
                        delegate
                        {
                            // bat 启动前的同步收尾（后台线程；存盘平时就在做，这里保服务器/隧道优雅收线）
                            try { ServerManager.StopAll(); } catch { }
                            try { TunnelManager.StopAll(); } catch { }
                            try { cfg.Save(); } catch { }
                        });
                });
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            cfg.Username = userCombo.Text.Trim();
            cfg.Save();
            AccountStore.Save();
            FloatBall.Shutdown();
            ServerManager.StopAll(); // 退出前通知所有服务器优雅关服
            TunnelManager.StopAll(); // Huge 隧道（腐竹/玩家两侧）一并收线
        }

        // 切回全窗模式：球消失（状态机保证），主窗恢复 + 淡入上移回弹
        void RestoreFull()
        {
            bool wasHidden = !IsVisible;
            HilLog.Write("切回全窗模式（之前" + (wasHidden ? "隐藏" : "最小化/可见") + "）");
            if (wasHidden)
            {
                Opacity = 0;
                Show();
            }
            if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
            Topmost = true;   // 闪一下置顶标记抢回 Z 序
            Topmost = false;
            Activate();
            SyncBallMode();
            if (wasHidden && enterTransform != null)
            {
                enterTransform.Y = 14;
                Ani.Start("ballmode",
                    Ani.AaOpacity(this, 1, 200, new AniEaseOutFluent(Ani.Weak)),
                    Ani.Aa(delegate(double d) { enterTransform.Y += d; }, -14, 500, new AniEaseOutBack(Ani.Weak)));
            }
        }

        // 进入悬浮球模式：主窗淡出隐藏 → 球弹入（球被设置关掉时退化为最小化）
        void EnterBallMode()
        {
            if (!IsVisible || WindowState == WindowState.Minimized) return;   // 已在球模式
            if (!cfg.FloatBallOn)
            {
                WindowState = WindowState.Minimized;   // StateChanged 会同步（球关=不出球）
                return;
            }
            HilLog.Write("进入悬浮球模式");
            Ani.Start("ballmode",
                Ani.AaOpacity(this, -Opacity, 140, new AniEaseInFluent(Ani.Weak)),
                Ani.AaCode(delegate
                {
                    Opacity = 1;   // 复位，RestoreFull 的入场从 0 自己跑
                    Hide();
                    SyncBallMode();
                }));
        }

        // 唯一真相：主窗可见且非最小化 = 全窗模式（无球），否则球模式
        void SyncBallMode()
        {
            FloatBall.SetMainVisible(IsVisible && WindowState != WindowState.Minimized, cfg);
        }

        // 启动后的转球守望：游戏窗口真的出现、且 30 秒没碰启动器，才退成球
        void ArmBallWatch(System.Diagnostics.Process proc)
        {
            watchedGame = proc;
            gameWindowSeen = false;
            lastInput = DateTime.Now;
            if (ballTimer == null)
            {
                ballTimer = new DispatcherTimer();
                ballTimer.Interval = TimeSpan.FromSeconds(1);
                ballTimer.Tick += delegate { BallWatchTick(); };
            }
            ballTimer.Start();
        }

        void BallWatchTick()
        {
            var p = watchedGame;
            if (p == null) { ballTimer.Stop(); return; }
            if (!gameWindowSeen)
            {
                bool exited;
                try { exited = p.HasExited; } catch { exited = true; }
                if (exited) { ballTimer.Stop(); return; }   // 进程没了（崩溃归 WatchGame 管）
                try
                {
                    p.Refresh();
                    gameWindowSeen = p.MainWindowHandle != IntPtr.Zero
                        && !string.IsNullOrEmpty(p.MainWindowTitle);
                    if (gameWindowSeen) HilLog.Write("游戏窗口已出现，开始 30 秒无操作倒计时");
                }
                catch { }
                return;
            }
            if (!IsVisible || WindowState == WindowState.Minimized) { ballTimer.Stop(); return; }   // 已手动最小化/转球
            if ((DateTime.Now - lastInput).TotalSeconds >= 30)
            {
                ballTimer.Stop();
                EnterBallMode();
            }
        }

        // 当前版本 = Config.VersionId，否则取最新已安装版本
        string CurrentVersion()
        {
            if (!string.IsNullOrEmpty(cfg.VersionId)) return cfg.VersionId;
            var inst = GameCore.GetInstalledVersions();
            if (inst.Count > 0) return inst[inst.Count - 1];
            return null;
        }

        void RefreshVersionText()
        {
            string v = CurrentVersion();
            versionText.Text = "当前版本: " + (v == null ? "未安装" : v);
        }

        // 日志只写文件（hil.log），界面不再铺日志面板
        public void AppendLog(string msg)
        {
            HilLog.Write(msg);
        }

        void LaunchGame()
        {
            string ver = CurrentVersion();
            if (ver == null)
            {
                MessageBox.Show(this, "请先在左侧「实例管理」里下载或选择一个版本。", BrandName,
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            // 账号解析：下拉框文本与选中账号同名用选中项；手输了新名字则就地建离线账号
            string user = userCombo.Text.Trim();
            if (user.Length == 0) user = "Player";
            Account acc = AccountStore.Selected();
            if (acc == null || acc.Name != user) acc = AccountStore.AddOffline(user);
            AccountStore.Select(acc);
            cfg.Username = acc.Name;
            btnLaunch.SetEnabled(false);
            AppendLog("正在启动 " + ver + " ...");
            string finalVer = ver;
            Account finalAcc = acc;
            var th = new Thread(new ThreadStart(delegate
            {
                RunningGames.Entry entry = null;
                try
                {
                    // 外置账号：先刷新令牌，过期则引导去账号页重登
                    if (finalAcc.Type == "external" && !Yggdrasil.Refresh(finalAcc))
                        throw new Exception("外置登录已过期，请到「账号」页重新登录 " + finalAcc.Name);

                    // 整合包实例：hil-instance.txt 存在时用其记录的启动版本，实例名为目录名
                    string launchId = Modrinth.GetInstanceLaunchId(finalVer);
                    string instanceName = null;
                    if (launchId != null) instanceName = finalVer;
                    else launchId = finalVer;

                    // 每版本设置覆盖（版本设置页保存的 hil.version.json）
                    Config launchCfg = cfg;
                    int memMb; string prio; bool? gcb;
                    if (VersionSettings.TryLoad(launchId, out memMb, out prio, out gcb))
                    {
                        launchCfg = CopyConfig(cfg);
                        launchCfg.MemoryMb = memMb;
                        launchCfg.Priority = prio;
                        if (gcb.HasValue) launchCfg.GcBoost = gcb.Value;
                    }

                    var proc = GameCore.Launch(launchId, finalAcc.Name, launchCfg, AppendLog, instanceName, finalAcc);
                    AppendLog("游戏进程已启动。");
                    entry = new RunningGames.Entry();
                    entry.InstanceName = instanceName ?? launchId;
                    entry.VersionId = launchId;
                    entry.PlayerName = finalAcc.Name;
                    entry.Proc = proc;
                    entry.StartTime = DateTime.Now;
                    RunningGames.Add(entry);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        runningGames++;
                        cfg.PushRecentLaunch(finalVer);
                        cfg.Save();
                        // 不立刻缩球：气泡报告 + 你知道吗；转球等游戏窗口出现且 30 秒无操作（或手动最小化）
                        ShowBubble("游戏进程已启动，等待窗口出现\n\n你知道吗：" + Tips.Pick(), false, null);
                        ArmBallWatch(proc);
                    }));
                    WatchGame(proc, instanceName ?? launchId, launchId, entry);
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    AppendLog("启动失败: " + msg);
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        MessageBox.Show(this, "启动失败：" + msg, BrandName, MessageBoxButton.OK, MessageBoxImage.Error);
                    }));
                }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    btnLaunch.SetEnabled(true);
                }));
            }));
            th.IsBackground = true;
            th.Start();
        }

        // 游戏崩溃守护：等待退出 → 退出码/日志关键词判定 → 崩溃分析对话框
        void WatchGame(System.Diagnostics.Process proc, string instanceName, string versionId, RunningGames.Entry entry)
        {
            if (proc == null) return;
            var th = new Thread(new ThreadStart(delegate
            {
                try { proc.WaitForExit(); } catch { return; }
                RunningGames.Remove(entry);
                bool killedByUser = entry != null && entry.KilledByUser;
                int code = -1;
                try { code = proc.ExitCode; } catch { }
                string instDir = System.IO.Path.Combine(Paths.InstancesDir, instanceName);
                if (killedByUser)
                {
                    // 用户从「运行中游戏」主动关闭：不算崩溃，不打扰
                    AppendLog("游戏已被用户手动关闭 (代码 " + code + ")");
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (runningGames > 0) runningGames--;
                        if (runningGames == 0) RestoreFull();
                    }));
                    return;
                }
                bool crash = code != 0;
                // 关键词兜底：退出码 0 也可能是崩溃（PCL 同款判据）；手动关闭排除
                // 时间闸：latest.log 必须写于本次运行期间——旧日志里的崩溃关键词会误报
                string logFile = System.IO.Path.Combine(instDir, "logs", "latest.log");
                bool logFresh = true;
                try
                {
                    DateTime started = entry != null ? entry.StartTime : DateTime.MinValue;
                    logFresh = System.IO.File.GetLastWriteTime(logFile) >= started.AddSeconds(-5);
                }
                catch { }
                string tail = logFresh ? CrashReport.ReadTail(logFile, 64 * 1024) : null;
                if (!crash && tail != null)
                {
                    if (tail.Contains("Crash report saved to") || tail.Contains("This crash report has been saved to")
                        || tail.Contains("An exception was thrown, the game will display an error screen and halt"))
                        crash = true;
                    if (tail.Contains("Someone is closing me!")) crash = false;
                }
                AppendLog("游戏已退出 (代码 " + code + ")" + (crash ? "，检测到崩溃" : ""));
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (runningGames > 0) runningGames--;
                    if (runningGames == 0) RestoreFull();   // 所有游戏窗口都关了 → 自动切回全窗
                }));
                if (!crash) return;
                CrashReport.CrashResult result = null;
                try { result = CrashReport.Analyze(instDir); }
                catch (Exception ex) { AppendLog("崩溃分析失败: " + ex.Message); }
                if (result == null) return;
                // H史挂钩：记录崩溃现场；若开启 AI 则主动弹泡询问是否帮忙（修复走 AiRepair 分类器）
                AiRepair.SetLastCrash(versionId, instanceName, instDir, result);
                bool aiAsk = cfg.AiEnabled;
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    try
                    {
                        var dlg = new CrashDialog(versionId, instanceName, result);
                        dlg.Owner = this;
                        dlg.ShowDialog();
                    }
                    catch { }
                    if (aiAsk)
                        ShowBubble("H史：你遇到了游戏崩溃！需要我帮你解决吗？", true,
                            delegate(bool agree) { if (agree) AiRepair.StartInteractiveFix(); });
                }));
            }));
            th.IsBackground = true;
            th.Start();
        }

        static Config CopyConfig(Config c)
        {
            var n = new Config();
            n.Username = c.Username;
            n.UserHistory = new List<string>(c.UserHistory);
            n.RecentLaunches = new List<string>(c.RecentLaunches);
            n.VersionId = c.VersionId;
            n.ShowSnapshots = c.ShowSnapshots;
            n.MemoryMb = c.MemoryMb;
            n.Priority = c.Priority;
            n.AnimSpeed = c.AnimSpeed;
            n.Theme = c.Theme;
            n.InstallJavaToSystemAsked = c.InstallJavaToSystemAsked;
            n.AiEnabled = c.AiEnabled;
            n.AiApiKey = c.AiApiKey;
            n.AiEndpoint = c.AiEndpoint;
            n.AiAnim = c.AiAnim;
            n.GcBoost = c.GcBoost;
            n.ModrinthMirror = c.ModrinthMirror;
            n.FloatBallOn = c.FloatBallOn;
            n.FloatBallX = c.FloatBallX;
            n.FloatBallY = c.FloatBallY;
            // 下面几个不拷贝会被 GameCore.Launch 里的 Save 重置回默认值（RecentLaunches 踩过的坑）
            n.LogExpanded = c.LogExpanded;
            n.DefenderTipShown = c.DefenderTipShown;
            n.LiquidGlass = c.LiquidGlass;
            n.LightMode = c.LightMode;
            n.AutoDayNight = c.AutoDayNight;
            n.AutoUpdate = c.AutoUpdate;
            return n;
        }

        // 账号库变更 → 重建账号选择器条目（可能在非 UI 线程触发）
        bool refreshingAccounts;
        void RefreshAccounts()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(RefreshAccounts)); return; }
            if (refreshingAccounts) return;
            refreshingAccounts = true;
            try
            {
                string keep = userCombo.Text;
                userCombo.ItemsSource = null;
                userCombo.ItemsSource = AccountStore.All();
                var sel = AccountStore.Selected();
                userCombo.Text = sel != null ? sel.Name : keep;
            }
            finally { refreshingAccounts = false; }
        }
    }
}
