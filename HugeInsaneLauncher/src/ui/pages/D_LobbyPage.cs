// HugeLauncher.Ui.Pages - 服务器列表页：公共服务器名录 + 分类切换 + 实时状态 ping（SLP）
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HugeLauncher;

namespace HugeLauncher.Ui.Pages
{
    // 导航 key: server:list。布局自上而下：搜索框 → 分类按钮行 → 置顶卡（固定不滚动）→ 状态卡片列表
    class PageServerLobby : PageBase
    {
        HintTextBox searchBox;
        FlatButton[] catButtons;
        int curCat;                            // 当前分类下标（默认 0 = 国内低延迟）
        Grid catArena;                         // 分类行容器（pill 与按钮行同坐标系）
        StackPanel catRow;
        SquircleBorder catPill;                // 三级标签：液态玻璃滑动指示框（二级同款活底多环）
        TranslateTransform catPillTf;
        ScaleTransform catPillSc;
        readonly Spring catPillX = new Spring(0) { Stiffness = 200, Damping = 16 };
        bool catPillPlaced;                    // 首次落位吸附不弹
        // 注意：pill 宽度不做弹簧——Width 是布局属性，逐帧改会让透镜对齐慢半帧（运动时文字重影）。
        // 与二级 pill 同结构：运动中尺寸冻结，只走 RenderTransform
        StackPanel listHost;
        LobbyServerCard pinnedCard;
        DispatcherTimer searchTimer;           // 搜索输入防抖 300ms

        public PageServerLobby()
        {
            LobbyServerCard.CopiedHandler = delegate(string addr)
            {
                if (Cfg != null) Cfg.PushRecentServer(addr);
            };
            RowDefinitions.Add(UiHelpers.AutoRow()); // 搜索框
            RowDefinitions.Add(UiHelpers.AutoRow()); // 分类按钮
            RowDefinitions.Add(UiHelpers.AutoRow()); // 置顶卡
            RowDefinitions.Add(UiHelpers.StarRow()); // 服务器列表

            // ===== 搜索框：实时过滤当前分类（防抖 300ms）=====
            searchBox = new HintTextBox("搜索：名称 / 地址 / 介绍");
            searchBox.Box.BorderBrush = Palette.B(Palette.Line);
            searchBox.Box.BorderThickness = new Thickness(1);
            searchBox.Margin = new Thickness(0, 4, 0, 0);
            searchTimer = new DispatcherTimer();
            searchTimer.Interval = TimeSpan.FromMilliseconds(300);
            searchTimer.Tick += delegate { searchTimer.Stop(); RebuildList(true); };
            searchBox.Box.TextChanged += delegate { searchTimer.Stop(); searchTimer.Start(); };
            Children.Add(searchBox);

            // ===== 五个分类按钮一排（玻璃模式 = 三级标签：纯文字 + 液态玻璃滑动 pill）=====
            catArena = new Grid();
            catArena.Margin = new Thickness(0, 10, 0, 0);
            catRow = new StackPanel();
            catRow.Orientation = Orientation.Horizontal;
            catButtons = new FlatButton[ServerCatalog.Categories.Length];
            for (int i = 0; i < ServerCatalog.Categories.Length; i++)
            {
                int idx = i;
                var b = new FlatButton(ServerCatalog.Categories[i]);
                b.SetFontSize(12);
                b.SetPadding(new Thickness(13, 6, 13, 6));
                b.SetCornerRadius(new CornerRadius(6));
                b.TabInGlass = true;   // 玻璃模式下纯文字，选中指示交给滑动 pill
                if (i > 0) b.Margin = new Thickness(8, 0, 0, 0);
                b.Click += delegate { SelectCategory(idx); };
                catButtons[i] = b;
                catRow.Children.Add(b);
            }
            // 滑动 pill：弹簧滑动 + 速度果冻（层叠顺序随史莱姆开关定，见下方 Add 分支）
            catPill = new SquircleBorder();
            catPill.CornerRadius = new CornerRadius(100);   // 全胶囊
            catPill.HorizontalAlignment = HorizontalAlignment.Left;
            catPill.VerticalAlignment = VerticalAlignment.Center;
            catPill.IsHitTestVisible = false;
            catPillTf = new TranslateTransform(0, 0);
            catPillSc = new ScaleTransform(1, 1);
            var catPillTg = new TransformGroup();
            catPillTg.Children.Add(catPillSc);
            catPillTg.Children.Add(catPillTf);
            catPill.RenderTransform = catPillTg;
            catPill.RenderTransformOrigin = new Point(0.5, 0.5);
            catPill.Visibility = (Glass.On || Frost.On) ? Visibility.Visible : Visibility.Collapsed;
            BuildCatPillFace();
            // 流光：pill 压文字上；霜面：pill 在文字背后
            if (Frost.On) catArena.Children.Add(catPill);
            catArena.Children.Add(catRow);
            if (!Frost.On) catArena.Children.Add(catPill);
            Grid.SetRow(catArena, 1);
            Children.Add(catArena);
            ApplyCatColors();
            // 玻璃开关切换：重建 pill 脸 + 换配色方案 + 重新落位
            ThemeManager.GlassChanged += delegate
            {
                catPill.Visibility = (Glass.On || Frost.On) ? Visibility.Visible : Visibility.Collapsed;
                catPillPlaced = false;
                if (Glass.On || Frost.On) BuildCatPillFace();
                ApplyCatColors();
                SlideCatPill(false);
            };

            // ===== 置顶卡：永远在场，不参与分类切换与搜索过滤，不随列表滚动 =====
            pinnedCard = new LobbyServerCard(ServerCatalog.Pinned, true);
            pinnedCard.Margin = new Thickness(0, 10, 0, 25); // 与列表间留 25px（普通卡间距 10px）
            Grid.SetRow(pinnedCard, 2);
            Children.Add(pinnedCard);

            // ===== 服务器列表（平滑滚动，卡片间距 10px）=====
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            listHost = new StackPanel();
            scroll.Content = listHost;
            Grid.SetRow(scroll, 3);
            Children.Add(scroll);
        }

        public override void OnEnter()
        {
            pinnedCard.BeginPing(); // 置顶卡进页面就探测（30 秒缓存内直接读缓存）
            RebuildList(false);     // 页面本身已有入场动画，列表不再叠动画
            SlideCatPill(catPillPlaced);   // 首次吸附落位；之后进页位置不变则原地不动
        }

        // 分类切换：重复点当前选中项不做任何事
        void SelectCategory(int idx)
        {
            if (idx == curCat) return;
            curCat = idx;
            ApplyCatColors();
            SlideCatPill(true);
            RebuildList(true);
        }

        // 选中项主题色高亮，未选中项暗色；玻璃模式 = 纯文字标签（pill 指示），选中亮白未选暗灰
        void ApplyCatColors()
        {
            for (int i = 0; i < catButtons.Length; i++)
            {
                if (Glass.On || Frost.On)
                {
                    catButtons[i].SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(0, 0, 0, 0),
                        i == curCat ? Palette.Text : Palette.Dim, Palette.Text);
                    catButtons[i].SetBold(false);   // 不加粗：宽度稳定，pill 目标不飘
                }
                else if (i == curCat)
                {
                    var c = ThemeManager.CurrentColor;
                    catButtons[i].SetColors(c, Palette.Lighten(c, 0.2), Palette.Dark, Palette.Dark);
                    catButtons[i].SetBold(true);
                }
                else
                {
                    catButtons[i].SetColors(Palette.Btn, Palette.BtnHover, Palette.Text, Palette.Text);
                    catButtons[i].SetBold(false);
                }
            }
        }

        // 三级 pill 玻璃脸（活底多环）：刷分类按钮行的实时画面——滑过文字时文字连续弯曲
        void BuildCatPillFace()
        {
            if (Frost.On)
            {
                if (catPill == null) return;
                catPill.Child = null;
                catPill.Background = Frost.TileStrong;
                catPill.BorderBrush = Frost.Edge;
                catPill.BorderThickness = new Thickness(1);
                return;
            }
            if (!Glass.On || catPill == null || catRow == null) return;
            Grid host;
            var rings = Glass.LensRings(catRow, 100, Glass.BandPx, out host, true);
            if (rings == null) { catPill.Background = Glass.SimulatedFace(0.08); return; }
            catPill.Background = null;
            var sheen = new SquircleBorder();
            sheen.Background = Glass.Sheen();
            sheen.CornerRadius = new CornerRadius(100);
            sheen.IsHitTestVisible = false;
            host.Children.Add(sheen);
            catPill.Child = rings;
        }

        // 滑动 pill 到当前分类按钮（宽度随文字宽；未布局完就排到布局后）
        void SlideCatPill(bool animate)
        {
            if ((!Glass.On && !Frost.On) || catPill == null) return;
            var b = catButtons[curCat];
            if (b.ActualWidth < 2)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { SlideCatPill(animate); }));
                return;
            }
            double tx;
            try { tx = b.TransformToAncestor(catRow).Transform(new Point(0, 0)).X; }
            catch { return; }
            catPill.Height = b.ActualHeight;
            catPill.Width = b.ActualWidth;   // 一次到位：运动中尺寸冻结（见字段注释：宽度弹簧会重影）
            if (!animate || !catPillPlaced)
            {
                SpringEngine.Detach(catPillX);
                catPillX.SnapTo(tx);
                catPillTf.X = tx;
                catPillSc.ScaleX = 1;
                catPillSc.ScaleY = 1;
                catPillPlaced = true;
            }
            else
            {
                SpringEngine.Go(catPillX, tx, ApplyCatPillX);
            }
        }

        // 位移 + 果冻（公式与二级 pill 完全一致：运动轴拉长、垂直轴压扁，幅度 0.14）
        void ApplyCatPillX(double v, double vel)
        {
            catPillTf.X = v;
            double j = Math.Min(0.14, Math.Abs(vel) * 0.0006);
            catPillSc.ScaleX = 1 + j;
            catPillSc.ScaleY = 1 - j * 0.5;
        }

        // 重建当前分类列表（搜索过滤只作用于当前分类；置顶卡不参与）
        void RebuildList(bool animate)
        {
            string f = searchBox.Text.Trim();
            listHost.Children.Clear();
            int shown = 0;
            foreach (var e in ServerCatalog.ByCategory(ServerCatalog.Categories[curCat]))
            {
                if (f.Length > 0
                    && IndexOf(e.DisplayName, f) < 0
                    && IndexOf(e.Address, f) < 0
                    && IndexOf(e.Intro, f) < 0)
                    continue;
                var card = new LobbyServerCard(e, false);
                card.Margin = new Thickness(0, 0, 0, 10);
                listHost.Children.Add(card);
                card.BeginPing(); // 引擎 30 秒会话缓存：已探测的直接回缓存，不重复发包
                shown++;
            }
            if (shown == 0)
            {
                var empty = new TextBlock();
                empty.Text = "没有匹配的服务器";
                empty.Foreground = Palette.B(Palette.Dim);
                empty.FontSize = 12;
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Margin = new Thickness(0, 30, 0, 0);
                listHost.Children.Add(empty);
            }
            if (animate)
            {
                // 切换动画：淡入 + 下方 10px 滑入（DarkTabControl 同款配方）
                listHost.Opacity = 0;
                Ani.Ts(listHost).Y = 10;
                Ani.Start("lobbyList",
                    Ani.AaOpacity(listHost, 1, 150),
                    Ani.AaY(listHost, -10, 300, new AniEaseOutFluent(Ani.Middle)));
            }
        }

        static int IndexOf(string hay, string needle)
        {
            if (string.IsNullOrEmpty(hay)) return -1;
            return hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
        }
    }

    // 服务器状态卡：图标 + 名称/地址/复制 + 中文介绍 + 右侧实时状态区
    class LobbyServerCard : Border
    {
        public static Action<string> CopiedHandler;   // 复制地址后的回调（页面用来记「最近服务器」）
        readonly ServerEntry entry;
        readonly string aniKey;
        Image iconImage;
        Border iconHolder;
        StackPanel statusBox;
        TextBlock statusText, latencyText, versionText;
        FlatButton copyBtn;
        DispatcherTimer copyTimer;
        bool hasResult;

        public LobbyServerCard(ServerEntry e, bool pinned)
        {
            entry = e;
            aniKey = "lob" + GetHashCode();
            Background = Palette.B(Palette.Panel);
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(14);

            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 图标
            g.ColumnDefinitions.Add(UiHelpers.StarCol()); // 文本区
            g.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 状态区

            // ---- 左侧 40×40 图标：默认主题色描边深色方块 +「?」，ping 到 favicon 后淡入覆盖 ----
            var iconCell = new Grid();
            iconCell.Width = 40;
            iconCell.Height = 40;
            iconCell.VerticalAlignment = VerticalAlignment.Top;
            iconHolder = new Border();
            iconHolder.Background = Palette.B(Palette.Input);
            iconHolder.BorderBrush = ThemeManager.Accent; // 共享画刷，换主题自动跟随
            iconHolder.BorderThickness = new Thickness(1.5);
            iconHolder.CornerRadius = new CornerRadius(4);
            var q = new TextBlock();
            q.Text = "?";
            q.Foreground = Palette.B(Palette.Dim);
            q.FontSize = 15;
            q.FontWeight = FontWeights.Bold;
            q.HorizontalAlignment = HorizontalAlignment.Center;
            q.VerticalAlignment = VerticalAlignment.Center;
            iconHolder.Child = q;
            iconCell.Children.Add(iconHolder);
            iconImage = new Image();
            iconImage.Width = 40;
            iconImage.Height = 40;
            iconImage.Stretch = Stretch.UniformToFill;
            iconImage.Opacity = 0;
            RenderOptions.SetBitmapScalingMode(iconImage, BitmapScalingMode.NearestNeighbor);
            iconCell.Children.Add(iconImage);
            iconCell.Margin = new Thickness(0, 0, 12, 0);
            g.Children.Add(iconCell);

            // ---- 中部：名称 + 徽章(置顶) + 地址 + 复制 / 介绍 ----
            var texts = new StackPanel();
            texts.VerticalAlignment = VerticalAlignment.Center;
            var line1 = new StackPanel();
            line1.Orientation = Orientation.Horizontal;
            var nameText = new TextBlock();
            nameText.Text = e.DisplayName;
            nameText.Foreground = Palette.B(Palette.Text);
            nameText.FontSize = 13;
            nameText.FontWeight = FontWeights.Bold;
            nameText.VerticalAlignment = VerticalAlignment.Center;
            line1.Children.Add(nameText);
            if (pinned)
            {
                // 主题色徽章「HIL作者在玩」
                var badge = new Border();
                badge.Background = ThemeManager.Accent; // 共享画刷，换主题自动跟随
                badge.CornerRadius = new CornerRadius(4);
                badge.Padding = new Thickness(6, 1, 6, 2);
                badge.Margin = new Thickness(8, 0, 0, 0);
                badge.VerticalAlignment = VerticalAlignment.Center;
                var bt = new TextBlock();
                bt.Text = "HIL作者在玩";
                bt.Foreground = Palette.B(Palette.Dark);
                bt.FontSize = 9.5;
                bt.FontWeight = FontWeights.SemiBold;
                badge.Child = bt;
                line1.Children.Add(badge);
            }
            var addrText = new TextBlock();
            addrText.Text = e.Address;
            addrText.Foreground = Palette.B(Palette.Dim);
            addrText.FontSize = 10.5;
            addrText.FontFamily = new FontFamily("Consolas");
            addrText.Margin = new Thickness(10, 0, 0, 0);
            addrText.VerticalAlignment = VerticalAlignment.Center;
            line1.Children.Add(addrText);
            // 最新支持版本标签（名录烤入数据；没有则由右侧实况探测的服务端自报版本补位）
            if (!string.IsNullOrEmpty(e.Ver))
            {
                var verTag = new Border();
                verTag.BorderBrush = Palette.B(Palette.Line);
                verTag.BorderThickness = new Thickness(1);
                verTag.CornerRadius = new CornerRadius(4);
                verTag.Padding = new Thickness(5, 0, 5, 1);
                verTag.Margin = new Thickness(8, 0, 0, 0);
                verTag.VerticalAlignment = VerticalAlignment.Center;
                verTag.ToolTip = "最新支持版本 " + e.Ver;
                var vt = new TextBlock();
                vt.Text = e.Ver;
                vt.Foreground = Palette.B(Palette.Dim);
                vt.FontSize = 9.5;
                vt.FontFamily = new FontFamily("Consolas");
                verTag.Child = vt;
                line1.Children.Add(verTag);
            }
            copyBtn = new FlatButton("复制");
            copyBtn.SetFontSize(10);
            copyBtn.SetPadding(new Thickness(7, 2, 7, 2));
            copyBtn.Margin = new Thickness(8, 0, 0, 0);
            copyBtn.VerticalAlignment = VerticalAlignment.Center;
            copyBtn.Click += delegate { CopyAddress(); };
            line1.Children.Add(copyBtn);
            texts.Children.Add(line1);
            var introText = new TextBlock();
            introText.Text = e.Intro;
            introText.Foreground = Palette.B(Palette.Dim);
            introText.FontSize = 11;
            introText.TextTrimming = TextTrimming.CharacterEllipsis;
            introText.Margin = new Thickness(0, 3, 0, 0);
            texts.Children.Add(introText);
            Grid.SetColumn(texts, 1);
            g.Children.Add(texts);

            // ---- 右侧状态区：探测中… → 在线 x/y + xxms + 版本名 ----
            statusBox = new StackPanel();
            statusBox.VerticalAlignment = VerticalAlignment.Center;
            statusBox.MinWidth = 90;
            statusText = new TextBlock();
            statusText.Text = "探测中…";
            statusText.Foreground = Palette.B(Palette.Dim);
            statusText.FontSize = 11.5;
            statusText.HorizontalAlignment = HorizontalAlignment.Right;
            statusBox.Children.Add(statusText);
            latencyText = new TextBlock();
            latencyText.FontSize = 11.5;
            latencyText.FontWeight = FontWeights.SemiBold;
            latencyText.Margin = new Thickness(0, 2, 0, 0);
            latencyText.HorizontalAlignment = HorizontalAlignment.Right;
            latencyText.Visibility = Visibility.Collapsed;
            statusBox.Children.Add(latencyText);
            versionText = new TextBlock();
            versionText.Foreground = Palette.B(Palette.Dim);
            versionText.FontSize = 9.5;
            versionText.Margin = new Thickness(0, 2, 0, 0);
            versionText.HorizontalAlignment = HorizontalAlignment.Right;
            versionText.MaxWidth = 170;
            versionText.TextTrimming = TextTrimming.CharacterEllipsis;
            versionText.Visibility = Visibility.Collapsed;
            statusBox.Children.Add(versionText);
            Grid.SetColumn(statusBox, 2);
            g.Children.Add(statusBox);

            Child = g;

            // 「已复制」1.5 秒后恢复「复制」
            copyTimer = new DispatcherTimer();
            copyTimer.Interval = TimeSpan.FromMilliseconds(1500);
            copyTimer.Tick += delegate { copyTimer.Stop(); copyBtn.Text = "复制"; };
        }

        // 发起（或读 30 秒缓存）一次状态 ping；引擎保证回调在 UI 线程
        public void BeginPing()
        {
            ServerPing.PingAsync(entry.Address, delegate(ServerPingResult r) { ApplyResult(r); });
        }

        void ApplyResult(ServerPingResult r)
        {
            bool first = !hasResult;
            hasResult = true;
            if (r.Ok)
            {
                Opacity = 1;
                statusText.Text = "在线 " + r.Online + "/" + r.Max;
                statusText.Foreground = Palette.B(Palette.Text);
                latencyText.Text = r.LatencyMs + "ms";
                // 延迟配色：<300ms 绿、<800ms 主题色、更高灰色
                latencyText.Foreground = Palette.B(r.LatencyMs < 300 ? Palette.Green
                    : r.LatencyMs < 800 ? ThemeManager.CurrentColor : Palette.Dim);
                latencyText.Visibility = Visibility.Visible;
                if (!string.IsNullOrEmpty(r.VersionName))
                {
                    versionText.Text = r.VersionName;
                    versionText.Visibility = Visibility.Visible;
                }
                ToolTip = string.IsNullOrEmpty(r.Motd) ? null : r.Motd;
                if (r.FaviconPng != null)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = new MemoryStream(r.FaviconPng);
                        bi.EndInit();
                        bi.Freeze();
                        iconImage.Source = bi;
                        if (first)
                        {
                            Ani.Start(aniKey + "i",
                                Ani.AaOpacity(iconImage, 1, 250),
                                Ani.AaCode(delegate { iconHolder.Visibility = Visibility.Collapsed; }));
                        }
                        else
                        {
                            iconImage.Opacity = 1;
                            iconHolder.Visibility = Visibility.Collapsed;
                        }
                    }
                    catch { }
                }
            }
            else
            {
                // 离线：整卡变灰，状态区显示「离线」
                Opacity = 0.5;
                statusText.Text = "离线";
                statusText.Foreground = Palette.B(Palette.Dim);
                latencyText.Visibility = Visibility.Collapsed;
                versionText.Visibility = Visibility.Collapsed;
                ToolTip = string.IsNullOrEmpty(r.Error) ? "无法连接到服务器" : r.Error;
            }
            if (first)
            {
                // 骨架 → 真实数据：淡入过渡，不生硬跳动
                statusBox.Opacity = 0;
                Ani.Start(aniKey + "s", Ani.AaOpacity(statusBox, 1, 200));
            }
            else statusBox.Opacity = 1;
        }

        // 复制地址到剪贴板；按钮文字短暂变「已复制」并轻弹一下
        void CopyAddress()
        {
            try { Clipboard.SetText(entry.Address); }
            catch { PageBase.Toast("复制失败"); return; }
            var copied = CopiedHandler;
            if (copied != null) copied(entry.Address);
            copyBtn.Text = "已复制";
            copyTimer.Stop();
            copyTimer.Start();
            // 缩放反馈（delta 从当前值算，打断不漂移）
            var s = Ani.Sc(copyBtn);
            double cur = s.ScaleX;
            Ani.Start(aniKey + "c",
                Ani.Aa(delegate(double d) { s.ScaleX += d; s.ScaleY += d; }, 1.12 - cur, 90, new AniEaseOutFluent(Ani.Strong)),
                Ani.Aa(delegate(double d) { s.ScaleX += d; s.ScaleY += d; }, -0.12, 240, new AniEaseOutBack(Ani.Weak), 0, true));
        }
    }
}
