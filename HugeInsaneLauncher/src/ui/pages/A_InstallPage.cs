// HugeLauncher.Ui.Pages - 原版游戏下载页（子代理 A）：版本列表 + 页内安装确认视图（PCL 仿制，深色主题）
// 导航 key: download:install
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using HugeLauncher;
using HugeLauncher.Ui;
using HugeLauncher.Ai;

namespace HugeLauncher.Ui.Pages
{
    class PageDownloadInstall : PageBase
    {
        // ---------- 视图容器 ----------
        ScrollViewer _listView;
        StackPanel _listPanel;
        TextBlock _loadingText;
        ScrollViewer _confirmView;
        StackPanel _confirmPanel;

        // ---------- 版本列表数据 ----------
        bool _versionsLoaded;
        bool _versionsLoading;
        bool _cardsBuilt;   // 版本卡片已构建：之后进页只刷「已安装」标记，不再全量重建（狂点标签零开销）
        int _buildSeq;      // 构建代次：重建时自增，作废旧的分批构建任务
        List<GameCore.VersionEntry> _versions = new List<GameCore.VersionEntry>();
        HashSet<string> _installed = new HashSet<string>();
        MiniCheckBox _chkSnapshots;

        // ---------- 安装确认视图状态 ----------
        GameCore.VersionEntry _selected;
        LoaderCard _cardForge, _cardNeoForge, _cardFabric;
        MiniCheckBox _chkFabricApi;

        // ---------- H史剧本接口状态 ----------
        readonly Dictionary<string, Border> _rowByVersion = new Dictionary<string, Border>(); // 版本 id → 行元素
        FlatButton _btnInstall;          // 确认视图「开始安装」按钮（随视图重建）
        DownloadTaskItem _lastInstallItem;

        // 版本清单是否已加载（剧本 WaitFor 用）
        public bool VersionsLoaded { get { return _versionsLoaded; } }

        // 找版本行并滚动到可见（先滚动不挥手，滚动在此完成）；找不到返回 null
        public FrameworkElement FindVersionItem(string version)
        {
            Border row;
            if (version == null || !_rowByVersion.TryGetValue(version, out row)) return null;
            try { row.BringIntoView(); } catch { }
            return row;
        }

        // 确认视图「开始安装」按钮
        public FrameworkElement ConfirmButton { get { return _btnInstall; } }

        // 打开某版本的安装确认视图（UI 线程调用）；版本不在清单返回 false
        public bool AiOpenConfirm(string versionId)
        {
            foreach (var ve in _versions)
                if (ve.Id == versionId) { OpenConfirm(ve); return true; }
            return false;
        }

        LoaderCard CardFor(string loader)
        {
            switch ((loader ?? "").ToLowerInvariant())
            {
                case "forge": return _cardForge;
                case "neoforge": return _cardNeoForge;
                case "fabric": return _cardFabric;
                default: return null;
            }
        }

        public FrameworkElement LoaderCardElement(string loader)
        {
            var c = CardFor(loader);
            if (c == null) return null;
            try { c.Card.BringIntoView(); } catch { }
            return c.Card;
        }

        // 勾选加载器并选最新版本（展开卡片、触发版本列表加载；异步就绪，用 AiLoaderReady 轮询）
        public void AiSelectLoader(string loader)
        {
            var c = CardFor(loader);
            if (c != null) c.AiPickLatest();
        }

        public bool AiLoaderReady(string loader)
        {
            var c = CardFor(loader);
            return c != null && c.HasSelection;
        }

        // 真实触发安装（= 点「开始安装」），返回任务中心条目（WatchProgress 盯它）
        public DownloadTaskItem AiStartInstall()
        {
            StartInstall();
            return _lastInstallItem;
        }

        public PageDownloadInstall()
        {
            // 版本列表视图
            _listPanel = new StackPanel();
            _listView = new SmoothScrollViewer();
            _listView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _listView.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _listView.Content = _listPanel;
            Children.Add(_listView);

            // 安装确认视图（页内切换，初始隐藏）
            _confirmPanel = new StackPanel();
            _confirmView = new SmoothScrollViewer();
            _confirmView.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _confirmView.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _confirmView.Content = _confirmPanel;
            _confirmView.Visibility = Visibility.Collapsed;
            Children.Add(_confirmView);

            BuildListSkeleton();
        }

        public override void OnEnter()
        {
            RefreshInstalled();
            if (!_versionsLoaded && !_versionsLoading) StartLoadVersions();
            else if (_versionsLoaded)
            {
                if (!_cardsBuilt) BuildVersionCards();
                else RefreshInstalledTags();   // 卡片复用：只刷标记，不重建不解码图标
            }
        }

        // ---------- 图标 ----------

        // 方块图标全进程缓存：只解码一次并 Freeze，几百个版本行共用同一 BitmapImage（UI 线程零磁盘 IO）
        static readonly Dictionary<string, BitmapImage> iconCache = new Dictionary<string, BitmapImage>();

        static Image BlockIcon(string relPath, double size)
        {
            var img = new Image();
            try
            {
                BitmapImage bi;
                if (!iconCache.TryGetValue(relPath, out bi))
                {
                    bi = new BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new Uri(Paths.LauncherDir + relPath, UriKind.Absolute);
                    bi.CacheOption = BitmapCacheOption.OnLoad;   // 解码完立刻撒手文件句柄
                    bi.EndInit();
                    bi.Freeze();
                    iconCache[relPath] = bi;
                }
                img.Source = bi;
            }
            catch { }
            img.Width = size;
            img.Height = size;
            return img;
        }

        static string IconForType(string type)
        {
            if (type == "release") return "/Images/Blocks/Grass.png";
            if (type == "snapshot") return "/Images/Blocks/CommandBlock.png";
            return "/Images/Blocks/CobbleStone.png";
        }

        static string LabelForType(string type)
        {
            if (type == "release") return "正式版";
            if (type == "snapshot") return "快照版";
            return "远古版";
        }

        // ---------- 卡片辅助 ----------

        static Border MakeCard(out StackPanel content)
        {
            var card = new Border();
            card.Background = Palette.B(Palette.Panel);
            card.CornerRadius = new CornerRadius(8);
            card.Padding = new Thickness(14);
            var sp = new StackPanel();
            card.Child = sp;
            content = sp;
            return card;
        }

        static TextBlock MakeText(string text, double size, Color color, bool bold)
        {
            var tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = size;
            tb.Foreground = Palette.B(color);
            if (bold) tb.FontWeight = FontWeights.SemiBold;
            tb.VerticalAlignment = VerticalAlignment.Center;
            return tb;
        }

        // ---------- 版本列表视图 ----------

        void BuildListSkeleton()
        {
            _listPanel.Children.Clear();

            StackPanel head;
            var headCard = MakeCard(out head);
            headCard.Margin = new Thickness(16, 16, 16, 5);
            var row = new Grid();
            row.ColumnDefinitions.Add(UiHelpers.StarCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            var title = MakeText("选择要下载的 Minecraft 版本", 13, Palette.Text, true);
            row.Children.Add(title);

            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            _chkSnapshots = new MiniCheckBox();
            _chkSnapshots.VerticalAlignment = VerticalAlignment.Center;
            _chkSnapshots.Changed += delegate
            {
                if (Cfg != null)
                {
                    Cfg.ShowSnapshots = _chkSnapshots.IsChecked;
                    try { Cfg.Save(); } catch { }
                }
                if (_versionsLoaded) BuildVersionCards();
            };
            right.Children.Add(_chkSnapshots);
            var lbl = MakeText("显示快照版", 12, Palette.Dim, false);
            lbl.Margin = new Thickness(6, 0, 14, 0);
            right.Children.Add(lbl);
            var btnRefresh = new FlatButton("刷新");
            btnRefresh.SetPadding(new Thickness(14, 5, 14, 5));
            btnRefresh.Click += delegate
            {
                _versionsLoaded = false;
                StartLoadVersions();
            };
            right.Children.Add(btnRefresh);
            Grid.SetColumn(right, 1);
            row.Children.Add(right);
            head.Children.Add(row);
            _listPanel.Children.Add(headCard);

            _loadingText = MakeText("正在获取版本列表……", 13, Palette.Dim, false);
            _loadingText.HorizontalAlignment = HorizontalAlignment.Center;
            _loadingText.Margin = new Thickness(0, 60, 0, 0);
            _listPanel.Children.Add(_loadingText);
        }

        void RefreshInstalled()
        {
            try
            {
                _installed = new HashSet<string>(GameCore.GetInstalledVersions());
            }
            catch { }
        }

        // 只刷「已安装/点击下载」标记：遍历已建行改文本/可见性，不重建卡片（进页开销≈0）
        void RefreshInstalledTags()
        {
            foreach (var kv in _rowByVersion)
            {
                var tag = kv.Value.Tag as TextBlock;
                if (tag != null) ApplyInstalledState(kv.Value, tag, _installed.Contains(kv.Key));
            }
        }

        static void ApplyInstalledState(Border row, TextBlock tag, bool installed)
        {
            tag.Text = installed ? "已安装" : "点击下载";
            tag.Visibility = installed ? Visibility.Visible : Visibility.Hidden;
            row.ToolTip = installed ? "已安装，点击将再次安装为副本（名称后加 #）" : null;
        }

        void StartLoadVersions()
        {
            _versionsLoading = true;
            if (_loadingText != null) _loadingText.Visibility = Visibility.Visible;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<GameCore.VersionEntry> list = null;
                Exception error = null;
                try { list = GameCore.GetVersionList(); }
                catch (Exception ex) { error = ex; }
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    _versionsLoading = false;
                    if (error != null)
                    {
                        if (_loadingText != null) _loadingText.Text = "版本列表获取失败：" + error.Message;
                        Toast("版本列表获取失败：" + error.Message);
                        return;
                    }
                    _versions = list ?? new List<GameCore.VersionEntry>();
                    RefreshInstalled();
                    _versionsLoaded = true;
                    BuildVersionCards();
                }));
            });
        }

        void BuildVersionCards()
        {
            _buildSeq++;   // 作废在飞的分批构建
            // 保留头部两张（标题卡 + 加载提示），重建分组卡
            while (_listPanel.Children.Count > 2) _listPanel.Children.RemoveAt(2);
            _rowByVersion.Clear();
            _loadingText.Visibility = Visibility.Collapsed;
            if (Cfg != null) _chkSnapshots.SetChecked(Cfg.ShowSnapshots, false);

            var releases = new List<GameCore.VersionEntry>();
            var snapshots = new List<GameCore.VersionEntry>();
            var olds = new List<GameCore.VersionEntry>();
            foreach (var ve in _versions)
            {
                if (ve.Type == "release") releases.Add(ve);
                else if (ve.Type == "snapshot") snapshots.Add(ve);
                else olds.Add(ve);
            }

            bool showExtra = Cfg != null && Cfg.ShowSnapshots;
            _listPanel.Children.Add(BuildGroupCard("正式版 (" + releases.Count + ")", releases));
            if (showExtra && snapshots.Count > 0)
                _listPanel.Children.Add(BuildGroupCard("快照版 (" + snapshots.Count + ")", snapshots));
            if (showExtra && olds.Count > 0)
                _listPanel.Children.Add(BuildGroupCard("远古版 (" + olds.Count + ")", olds));
            _cardsBuilt = true;   // 之后进页走 RefreshInstalledTags 捷径
        }

        Border BuildGroupCard(string title, List<GameCore.VersionEntry> items)
        {
            StackPanel sp;
            var card = MakeCard(out sp);
            card.Margin = new Thickness(16, 5, 16, 5);
            var head = MakeText(title, 13, Palette.Text, true);
            head.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(head);
            // 几百行版本行分批流入：每个调度回合 30 行，狂点标签时输入随时插队
            int seq = _buildSeq;
            Action<int> batch = null;
            batch = delegate(int from)
            {
                if (seq != _buildSeq) return;   // 已重建，作废旧任务
                int to = Math.Min(from + 30, items.Count);
                for (int i = from; i < to; i++) sp.Children.Add(BuildVersionRow(items[i]));
                if (to < items.Count)
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { batch(to); }));
            };
            batch(0);
            return card;
        }

        UIElement BuildVersionRow(GameCore.VersionEntry ve)
        {
            var row = new Border();
            row.Height = 42;
            row.CornerRadius = new CornerRadius(6);
            row.Padding = new Thickness(8, 0, 8, 0);
            row.Background = Brushes.Transparent;
            row.MouseEnter += delegate { row.Background = Palette.B(Palette.BtnHover); };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };

            var dock = new DockPanel();
            // 标记统一建成一份存 row.Tag：已安装常驻「已安装」，未安装悬停才露「点击下载」
            // （状态变化由 RefreshInstalledTags 就地刷新，不重建行）
            var tag = MakeText("", 11, Palette.Dim, false);
            DockPanel.SetDock(tag, Dock.Right);
            dock.Children.Add(tag);
            row.MouseEnter += delegate { tag.Visibility = Visibility.Visible; };
            row.MouseLeave += delegate { if (!_installed.Contains(ve.Id)) tag.Visibility = Visibility.Hidden; };
            row.Tag = tag;
            ApplyInstalledState(row, tag, _installed.Contains(ve.Id));

            var left = new StackPanel();
            left.Orientation = Orientation.Horizontal;
            var img = BlockIcon(IconForType(ve.Type), 16);
            img.Margin = new Thickness(0, 0, 10, 0);
            img.VerticalAlignment = VerticalAlignment.Center;
            left.Children.Add(img);
            var name = MakeText(ve.Id, 13, Palette.Text, true);
            name.Margin = new Thickness(0, 0, 10, 0);
            left.Children.Add(name);
            left.Children.Add(MakeText(LabelForType(ve.Type), 11, Palette.Dim, false));
            dock.Children.Add(left);
            row.Child = dock;

            // 已安装行也可点击：再次安装会落成 # 副本（ToolTip 由 ApplyInstalledState 维护）
            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += delegate { OpenConfirm(ve); };
            _rowByVersion[ve.Id] = row;   // H史剧本：按版本 id 找回行元素
            return row;
        }

        // ---------- 安装确认视图 ----------

        void OpenConfirm(GameCore.VersionEntry ve)
        {
            _selected = ve;
            _confirmPanel.Children.Clear();
            _chkFabricApi = null;

            // 顶部：返回 + 版本信息
            StackPanel top;
            var topCard = MakeCard(out top);
            topCard.Margin = new Thickness(16, 16, 16, 5);
            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(UiHelpers.AutoCol());
            topRow.ColumnDefinitions.Add(UiHelpers.AutoCol());
            topRow.ColumnDefinitions.Add(UiHelpers.StarCol());
            var btnBack = new FlatButton("← 返回");
            btnBack.SetPadding(new Thickness(12, 6, 12, 6));
            btnBack.VerticalAlignment = VerticalAlignment.Center;
            btnBack.Click += delegate { ShowListView(); };
            topRow.Children.Add(btnBack);
            var logo = BlockIcon(IconForType(ve.Type), 32);
            logo.Margin = new Thickness(16, 0, 12, 0);
            logo.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(logo, 1);
            topRow.Children.Add(logo);
            var nameSp = new StackPanel();
            nameSp.VerticalAlignment = VerticalAlignment.Center;
            nameSp.Children.Add(MakeText(ve.Id, 15, Palette.Text, true));
            nameSp.Children.Add(MakeText(LabelForType(ve.Type) + " · 勾选需要的加载器，都不勾选则为纯原版", 11, Palette.Dim, false));
            Grid.SetColumn(nameSp, 2);
            topRow.Children.Add(nameSp);
            top.Children.Add(topRow);
            _confirmPanel.Children.Add(topCard);

            // 重复安装提示：将落到的副本 id 预览
            if (_installed != null && _installed.Contains(ve.Id))
            {
                StackPanel dupSp;
                var dupCard = MakeCard(out dupSp);
                dupCard.Margin = new Thickness(16, 5, 16, 5);
                dupSp.Children.Add(MakeText("此版本已安装，本次将安装为副本：" + GameCore.NextAvailableId(ve.Id), 11.5, Palette.Dim, false));
                _confirmPanel.Children.Add(dupCard);
            }

            // 加载器折叠卡
            _cardForge = new LoaderCard(this, "Forge", "/Images/Blocks/Anvil.png", LoaderKind.Forge);
            _cardNeoForge = new LoaderCard(this, "NeoForge", "/Images/Loaders/NeoForge.png", LoaderKind.NeoForge);
            _cardFabric = new LoaderCard(this, "Fabric", "/Images/Loaders/Fabric.png", LoaderKind.Fabric);
            _confirmPanel.Children.Add(_cardForge.Card);
            _confirmPanel.Children.Add(_cardNeoForge.Card);
            _confirmPanel.Children.Add(_cardFabric.Card);
            _confirmPanel.Children.Add(BuildDisabledCard("OptiFine", "/Images/Blocks/GrassPath.png"));
            _confirmPanel.Children.Add(BuildDisabledCard("OptiFabric", "/Images/Blocks/GrassPath.png"));
            _confirmPanel.Children.Add(BuildDisabledCard("LiteLoader", "/Images/Blocks/Egg.png"));

            // Fabric API 附加选项放在 Fabric 卡内容里（见 LoaderCard 构建）
            // 底部开始安装按钮
            var btnInstall = new FlatButton("开始安装");
            btnInstall.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.15), Colors.White, Colors.White);
            btnInstall.SetFontSize(16);
            btnInstall.SetBold(true);
            btnInstall.SetPadding(new Thickness(20, 13, 20, 13));
            btnInstall.SetCornerRadius(new CornerRadius(26));
            var btnWrap = new Border();
            btnWrap.Margin = new Thickness(16, 12, 16, 40);
            btnWrap.HorizontalAlignment = HorizontalAlignment.Center;
            btnWrap.Child = btnInstall;
            btnInstall.Click += delegate { StartInstall(); };
            _btnInstall = btnInstall;   // H史剧本 LookAt("install:confirm") 目标
            UiMap.Register("install:confirm", delegate { return _btnInstall; });
            _confirmPanel.Children.Add(btnWrap);

            _listView.Visibility = Visibility.Collapsed;
            _confirmView.Opacity = 0;
            _confirmView.Visibility = Visibility.Visible;
            ThemeManager.AnimateDouble(_confirmView, OpacityProperty, 1, 160);
        }

        void ShowListView()
        {
            _confirmView.Visibility = Visibility.Collapsed;
            _listView.Visibility = Visibility.Visible;
        }

        Border BuildDisabledCard(string title, string icon)
        {
            StackPanel sp;
            var card = MakeCard(out sp);
            card.Margin = new Thickness(16, 5, 16, 5);
            card.Opacity = 0.55;
            var row = new Grid();
            row.Height = 40;
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            row.ColumnDefinitions.Add(UiHelpers.StarCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            var img = BlockIcon(icon, 16);
            img.Margin = new Thickness(0, 0, 10, 0);
            img.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(img);
            var t = MakeText(title, 13, Palette.Dim, true);
            Grid.SetColumn(t, 1);
            row.Children.Add(t);
            var s = MakeText("暂不支持", 11, Palette.Dim, false);
            Grid.SetColumn(s, 2);
            row.Children.Add(s);
            sp.Children.Add(row);
            return card;
        }

        // ---------- 加载器折叠卡 ----------

        enum LoaderKind { Forge, NeoForge, Fabric }

        class LoaderCard
        {
            public Border Card;
            public LoaderKind Kind;
            public MiniCheckBox Chk;
            public DarkComboBox Combo;
            public List<Loaders.LoaderVersion> Vers;
            public bool Failed;
            public string Title;

            PageDownloadInstall _owner;
            TextBlock _status;
            Border _content;
            TextBlock _chevron;
            bool _expanded;
            bool _loading;
            bool _loaded;

            public LoaderCard(PageDownloadInstall owner, string title, string icon, LoaderKind kind)
            {
                _owner = owner;
                Title = title;
                Kind = kind;

                StackPanel sp;
                Card = MakeCard(out sp);
                Card.Margin = new Thickness(16, 5, 16, 5);

                // 标题行（点击展开/收起）
                var head = new Grid();
                head.Height = 40;
                head.Cursor = Cursors.Hand;
                head.ColumnDefinitions.Add(UiHelpers.AutoCol());
                head.ColumnDefinitions.Add(UiHelpers.StarCol());
                head.ColumnDefinitions.Add(UiHelpers.AutoCol());
                head.ColumnDefinitions.Add(UiHelpers.AutoCol());
                var img = BlockIcon(icon, 16);
                img.Margin = new Thickness(0, 0, 10, 0);
                img.VerticalAlignment = VerticalAlignment.Center;
                head.Children.Add(img);
                var t = MakeText(title, 13, Palette.Text, true);
                Grid.SetColumn(t, 1);
                head.Children.Add(t);
                _status = MakeText("可以添加", 11, Palette.Dim, false);
                _status.Margin = new Thickness(0, 0, 12, 0);
                Grid.SetColumn(_status, 2);
                head.Children.Add(_status);
                _chevron = MakeText("▾", 12, Palette.Dim, false);
                Grid.SetColumn(_chevron, 3);
                head.Children.Add(_chevron);
                head.MouseLeftButtonUp += delegate { Toggle(); };
                sp.Children.Add(head);

                // 内容区
                var inner = new StackPanel();
                inner.Margin = new Thickness(4, 4, 4, 6);

                var chkRow = new StackPanel();
                chkRow.Orientation = Orientation.Horizontal;
                chkRow.Margin = new Thickness(0, 0, 0, 8);
                Chk = new MiniCheckBox();
                Chk.VerticalAlignment = VerticalAlignment.Center;
                Chk.Changed += delegate { OnCheckChanged(); };
                chkRow.Children.Add(Chk);
                var chkLbl = MakeText("安装 " + title, 12, Palette.Text, false);
                chkLbl.Margin = new Thickness(8, 0, 0, 0);
                chkRow.Children.Add(chkLbl);
                inner.Children.Add(chkRow);

                Combo = new DarkComboBox();
                Combo.Height = 26;
                Combo.SelectionChanged += delegate { OnSelectionChanged(); };
                inner.Children.Add(Combo);

                if (kind == LoaderKind.Fabric)
                {
                    var apiRow = new StackPanel();
                    apiRow.Orientation = Orientation.Horizontal;
                    apiRow.Margin = new Thickness(0, 8, 0, 0);
                    owner._chkFabricApi = new MiniCheckBox();
                    owner._chkFabricApi.VerticalAlignment = VerticalAlignment.Center;
                    apiRow.Children.Add(owner._chkFabricApi);
                    var apiLbl = MakeText("同时安装 Fabric API（大多数 Mod 需要）", 12, Palette.Text, false);
                    apiLbl.Margin = new Thickness(8, 0, 0, 0);
                    apiRow.Children.Add(apiLbl);
                    inner.Children.Add(apiRow);
                }

                _content = new Border();
                _content.Child = inner;
                _content.Visibility = Visibility.Collapsed;
                sp.Children.Add(_content);
            }

            void Toggle()
            {
                _expanded = !_expanded;
                _chevron.Text = _expanded ? "▴" : "▾";
                if (_expanded)
                {
                    _content.Opacity = 0;
                    _content.Visibility = Visibility.Visible;
                    ThemeManager.AnimateDouble(_content, UIElement.OpacityProperty, 1, 150);
                    StartLoad();
                }
                else
                {
                    _content.Visibility = Visibility.Collapsed;
                }
            }

            void StartLoad()
            {
                if (_loaded || _loading) return;
                _loading = true;
                _status.Text = "获取中……";
                var kind = Kind;
                string mc = _owner._selected.Id;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    List<Loaders.LoaderVersion> list = null;
                    Exception error = null;
                    try
                    {
                        if (kind == LoaderKind.Forge) list = Loaders.GetForgeVersions(mc);
                        else if (kind == LoaderKind.NeoForge) list = Loaders.GetNeoForgeVersions(mc);
                        else list = Loaders.GetFabricLoaderList(mc);
                    }
                    catch (Exception ex) { error = ex; }
                    _owner.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        _loading = false;
                        if (error != null)
                        {
                            Failed = true;
                            _status.Text = "加载失败";
                            Toast(Title + " 版本列表获取失败：" + error.Message);
                            return;
                        }
                        _loaded = true;
                        Vers = list ?? new List<Loaders.LoaderVersion>();
                        Combo.Items.Clear();
                        foreach (var v in Vers) Combo.Items.Add(string.IsNullOrEmpty(v.Label) ? v.Version : v.Label);
                        if (Vers.Count == 0)
                        {
                            _status.Text = "无可用版本";
                        }
                        else
                        {
                            _status.Text = "可以添加";
                            if (Chk.IsChecked) Combo.SelectedIndex = 0;
                        }
                    }));
                });
            }

            void OnCheckChanged()
            {
                if (Chk.IsChecked)
                {
                    _owner.OnLoaderChecked(this); // 互斥：取消其它加载器
                    if (!_loaded && !_loading) StartLoad();
                    if (Vers != null && Vers.Count > 0 && Combo.SelectedIndex < 0) Combo.SelectedIndex = 0;
                }
                UpdateStatus();
            }

            void OnSelectionChanged()
            {
                if (Combo.SelectedIndex >= 0 && !Chk.IsChecked) Chk.SetChecked(true, true);
                else UpdateStatus();
            }

            void UpdateStatus()
            {
                if (Failed) { _status.Text = "加载失败"; return; }
                if (Vers != null && Vers.Count == 0) { _status.Text = "无可用版本"; return; }
                if (Chk.IsChecked && Combo.SelectedIndex >= 0)
                    _status.Text = Combo.SelectedItem as string;
                else
                    _status.Text = "可以添加";
            }

            // 已勾选且有可用选择
            public bool HasSelection
            {
                get { return Chk.IsChecked && Vers != null && Vers.Count > 0 && Combo.SelectedIndex >= 0; }
            }

            // H史剧本用：展开卡片 + 勾选 + 选最新版本（版本列表异步加载，就绪由 AiLoaderReady 轮询确认）
            public void AiPickLatest()
            {
                if (!_expanded) Toggle();
                Chk.SetChecked(true, true);
                if (Vers != null && Vers.Count > 0 && Combo.SelectedIndex < 0) Combo.SelectedIndex = 0;
            }

            public string SelectedVersion
            {
                get
                {
                    if (!HasSelection) return null;
                    return Vers[Combo.SelectedIndex].Version;
                }
            }
        }

        // ---------- 开始安装 ----------

        // 加载器互斥：勾选一个时，取消另外两张卡的勾选与版本选择
        void OnLoaderChecked(LoaderCard card)
        {
            if (card == null || !card.Chk.IsChecked) return;
            var all = new LoaderCard[] { _cardForge, _cardNeoForge, _cardFabric };
            foreach (var other in all)
            {
                if (other == null || other == card) continue;
                if (other.Chk.IsChecked) other.Chk.SetChecked(false, true);
                if (other.Combo.SelectedIndex >= 0) other.Combo.SelectedIndex = -1;
            }
        }

        void StartInstall()
        {
            if (_selected == null) return;
            GameCore.VersionEntry ve = _selected;
            string mc = ve.Id;
            HilLog.Write("剧本 StartInstall 入口：" + mc);

            // 互斥：Forge > NeoForge > Fabric
            var picked = new List<LoaderCard>();
            if (_cardForge.HasSelection) picked.Add(_cardForge);
            if (_cardNeoForge.HasSelection) picked.Add(_cardNeoForge);
            if (_cardFabric.HasSelection) picked.Add(_cardFabric);
            LoaderCard chosen = picked.Count > 0 ? picked[0] : null;
            if (picked.Count > 1) Toast("多个加载器互斥，已选择 " + chosen.Title);

            string loaderVer = chosen != null ? chosen.SelectedVersion : null;
            string loaderLabel = chosen != null ? (chosen.Combo.SelectedItem as string) : null;
            bool fabricApi = chosen == _cardFabric && _chkFabricApi != null && _chkFabricApi.IsChecked;

            string displayName = chosen == null ? mc : mc + "-" + chosen.Title + " " + (loaderLabel ?? loaderVer);

            var reporter = TaskCenter.Register("安装 " + displayName);
            DownloadTaskItem item = null;
            lock (TaskCenter.Tasks)
            {
                foreach (var t in TaskCenter.Tasks)
                    if (t.Name == "安装 " + displayName && !t.Finished) { item = t; break; }
            }
            _lastInstallItem = item;   // H史 WatchProgress 盯这个条目

            // 立即跳转下载管理页看进度
            HilLog.Write("剧本 StartInstall 跳下载管理页");
            Navigator.Go("download:speed");

            LoaderKind? kind = chosen != null ? (LoaderKind?)chosen.Kind : null;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string vid;
                    if (kind == null)
                    {
                        // 纯原版：重复下载自动重定向到 # 后缀，vid 为实际 id
                        vid = GameCore.DownloadVanilla(ve, reporter);
                    }
                    else
                    {
                        // 加载器继承原版：原版未装时先补装（否则 Resolve 抛“版本 xxx 未安装”）
                        GameCore.EnsureVanilla(ve, reporter);
                        if (kind == LoaderKind.Forge)
                        {
                            vid = Loaders.InstallForge(mc, loaderVer, reporter);
                        }
                        else if (kind == LoaderKind.NeoForge)
                        {
                            vid = Loaders.InstallNeoForge(mc, loaderVer, reporter);
                        }
                        else
                        {
                            vid = Loaders.InstallFabric(mc, loaderVer, reporter);
                        }
                    }

                    if (fabricApi)
                    {
                        try
                        {
                            Modrinth.InstallResource("mod", "fabric-api", mc, "fabric", vid, delegate(string s) { });
                        }
                        catch (Exception exApi)
                        {
                            string m = exApi.Message;
                            Dispatcher.BeginInvoke(new Action(delegate { Toast("Fabric API 安装失败：" + m); }));
                        }
                    }

                    TaskCenter.Complete(item, true, "");
                    if (Cfg != null)
                    {
                        Cfg.VersionId = vid;
                        try { Cfg.Save(); } catch { }
                    }
                    string done = vid;
                    Dispatcher.BeginInvoke(new Action(delegate { Toast("安装完成：" + done); }));
                }
                catch (Exception ex)
                {
                    TaskCenter.Complete(item, false, ex.Message);
                    string m = ex.Message;
                    Dispatcher.BeginInvoke(new Action(delegate { Toast("安装失败：" + m); }));
                }
            });
        }
    }
}
