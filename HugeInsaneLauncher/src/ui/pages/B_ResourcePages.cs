// HugeLauncher.Ui.Pages - 子代理B：社区资源下载列表页（Mod/整合包/数据包/资源包/光影包）
// 五个页面共享 PageResourceBase 骨架，子类仅提供项目类型与页面标题。
using System;
using System.Collections.Generic;
using System.IO;
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
    // B 组页面共享的小工具：下载量格式化 / 版本 id 反推 MC 版本号 / Modrinth 图标异步加载
    static class ResourceUiUtil
    {
        // >=1亿 → "x.x 亿"；>=1万 → "x.x 万"；否则原数字
        public static string FormatDownloads(long n)
        {
            if (n >= 100000000) return (n / 100000000.0).ToString("0.0") + " 亿";
            if (n >= 10000) return (n / 10000.0).ToString("0.0") + " 万";
            return n.ToString();
        }

        // 由已装版本 id 反推 MC 版本号（loader 参数保留以兼容调用方；实际以 json 的 inheritsFrom 为准）
        public static string ExtractGameVersion(string versionId, string loader)
        {
            return GameCore.BaseGameVersion(versionId);
        }

        // ---- 图标：缩略图后台解码缓存（UI 线程永不解码位图） ----

        static readonly Dictionary<string, BitmapSource> thumbs = new Dictionary<string, BitmapSource>();
        static BitmapImage eggPlaceholder;   // 占位图全进程只解码一次

        static BitmapImage Egg()
        {
            if (eggPlaceholder == null)
            {
                string p = Path.Combine(Paths.LauncherDir, "Images", "Blocks", "Egg.png");
                if (File.Exists(p))
                {
                    eggPlaceholder = new BitmapImage();
                    eggPlaceholder.BeginInit();
                    eggPlaceholder.UriSource = new Uri(p, UriKind.Absolute);
                    eggPlaceholder.DecodePixelWidth = 48;
                    eggPlaceholder.CacheOption = BitmapCacheOption.OnLoad;
                    eggPlaceholder.EndInit();
                    eggPlaceholder.Freeze();
                }
            }
            return eggPlaceholder;
        }

        // 后台线程把图标解码成 48px 缩略图并 Freeze，回 UI 只赋值；解码结果全进程缓存
        static void DecodeThumbAsync(string url, string path, Image img)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                BitmapSource t = null;
                lock (thumbs) { if (!thumbs.TryGetValue(url, out t)) t = null; }
                if (t == null)
                {
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(path, UriKind.Absolute);
                        bi.DecodePixelWidth = 48;
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        bi.Freeze();
                        t = bi;
                        lock (thumbs) thumbs[url] = t;
                    }
                    catch { }
                }
                if (t == null) return;
                var ready = t;
                img.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (object.Equals(img.Tag as string, url)) img.Source = ready;   // 防卡片复用串图
                }));
            });
        }

        // 先占位 Egg.png，缩略图就绪后回 UI 换图
        public static void LoadHitIcon(Image img, string url)
        {
            img.Tag = url;
            var egg = Egg();
            if (egg != null) img.Source = egg;
            if (string.IsNullOrEmpty(url)) return;
            lock (thumbs)
            {
                BitmapSource t;
                if (thumbs.TryGetValue(url, out t)) { img.Source = t; return; }
            }
            // IconCache 命中（文件已在本地）也绝不在 UI 线程解码，统一起后台任务
            string cached = IconCache.Get(url, delegate(string u, string p) { DecodeThumbAsync(u, p, img); });
            if (cached != null) DecodeThumbAsync(url, cached, img);
        }
    }

    // 五个社区资源列表页的共享骨架（PCL 风：搜索卡 + 结果卡列表 + 加载更多）
    abstract class PageResourceBase : PageBase
    {
        protected readonly string ProjectType;   // mod / modpack / datapack / resourcepack / shader
        protected readonly string TypeName;      // 中文类型名（Mod / 整合包 / ...）
        protected readonly string PageKey;       // download:xxx，进详情时作为来源 key 传出

        const int PageSize = 20;

        HintTextBox searchBox;
        // H史剧本演戏用：让 SteveDirector 能找到搜索框（LookAt）
        public FrameworkElement SearchBoxElement { get { return searchBox; } }
        DarkComboBox versionCombo;
        List<string> versionValues = new List<string>();   // 与 combo 项一一对应的游戏版本筛选值
        List<string> comboLabels = new List<string>();     // 显示文本（ItemsSource 一次性赋值，避免逐项 Add 触发布局）
        StackPanel listPanel;
        FlatButton moreButton;

        List<Modrinth.SearchHit> allResults;
        int shownCount;
        int renderSeq;   // 渲染代次：重渲/清列表时自增，作废旧的分批任务
        bool loading;
        bool searchedOnce;
        bool comboInit;
        bool populating;
        bool versionsStarted;

        protected PageResourceBase(string projectType, string typeName, string pageKey)
        {
            ProjectType = projectType;
            TypeName = typeName;
            PageKey = pageKey;
            BuildUi();
            // H史 UiMap：搜索框按项目类型注册（res:search:mod / res:search:modpack / ...）
            UiMap.Register("res:search:" + projectType, delegate { return searchBox; });
        }

        // ---- H史剧本接口 ----

        // 真实填入关键词并触发搜索（UI 线程调用）
        public void AiSearch(string query)
        {
            searchBox.Text = query ?? "";
            StartSearch();
        }

        // 搜索已结束（无论成败；结果有无看 AiFirstItem）
        public bool AiResultsReady { get { return !loading && searchedOnce; } }

        // 第一张结果卡片，顺便滚到可见（先滚动不挥手）；无结果返回 null
        public FrameworkElement AiFirstItem()
        {
            foreach (var c in listPanel.Children)
            {
                var card = c as Border;
                if (card == null) continue;   // 状态文本/加载更多按钮不是卡片
                try { card.BringIntoView(); } catch { }
                return card;
            }
            return null;
        }

        // 首张卡对应的搜索命中（进详情页的参数）；无结果返回 null
        public Modrinth.SearchHit AiFirstHit()
        {
            if (allResults == null || allResults.Count == 0) return null;
            return allResults[0];
        }

        void BuildUi()
        {
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel();
            stack.Margin = new Thickness(16);
            scroll.Content = stack;
            Children.Add(scroll);

            // ---- 搜索卡 ----
            var searchCard = MakeCard();
            searchCard.Margin = new Thickness(0, 0, 0, 10);
            var sc = new StackPanel();
            searchCard.Child = sc;
            var cardTitle = new TextBlock();
            cardTitle.Text = "搜索 " + TypeName;
            cardTitle.FontSize = 13;
            cardTitle.FontWeight = FontWeights.SemiBold;
            cardTitle.Foreground = Palette.B(Palette.Text);
            cardTitle.Margin = new Thickness(0, 0, 0, 10);
            sc.Children.Add(cardTitle);

            var row = new Grid();
            row.ColumnDefinitions.Add(UiHelpers.StarCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            searchBox = new HintTextBox("搜索名称...");
            searchBox.Margin = new Thickness(0, 0, 10, 0);
            searchBox.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; StartSearch(); }
            };
            Grid.SetColumn(searchBox, 0);
            row.Children.Add(searchBox);

            versionCombo = new DarkComboBox();
            versionCombo.Width = 200;
            versionCombo.Margin = new Thickness(0, 0, 10, 0);
            versionCombo.SelectionChanged += delegate { if (!populating && comboInit) StartSearch(); };
            Grid.SetColumn(versionCombo, 1);
            row.Children.Add(versionCombo);

            var btnSearch = new FlatButton("搜索");
            btnSearch.SetBold(true);
            btnSearch.Click += delegate { StartSearch(); };
            Grid.SetColumn(btnSearch, 2);
            row.Children.Add(btnSearch);
            sc.Children.Add(row);
            stack.Children.Add(searchCard);

            // ---- 结果列表 ----
            listPanel = new StackPanel();
            stack.Children.Add(listPanel);

            moreButton = new FlatButton("加载更多");
            moreButton.HorizontalAlignment = HorizontalAlignment.Center;
            moreButton.Margin = new Thickness(0, 4, 0, 10);
            moreButton.Click += delegate
            {
                shownCount += PageSize;
                RenderList();
            };
        }

        static Border MakeCard()
        {
            var b = new Border();
            b.Background = Palette.B(Palette.Panel);
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(14);
            return b;
        }

        public override void OnEnter()
        {
            // 版本筛选项初始化（需要 Cfg，主窗口注入后才能做）
            if (!comboInit)
            {
                comboInit = true;
                populating = true;
                string cur = Cfg != null ? Cfg.VersionId : null;
                if (!string.IsNullOrEmpty(cur))
                {
                    comboLabels.Add("当前版本 (" + cur + ")");
                    versionValues.Add(ResourceUiUtil.ExtractGameVersion(cur, Modrinth.DetectLoader(cur)));
                }
                comboLabels.Add("全部版本");
                versionValues.Add("");
                versionCombo.ItemsSource = comboLabels;
                versionCombo.SelectedIndex = 0;
                populating = false;
            }
            // 后台拉 release 版本清单（只拉一次）；回来一次性换 ItemsSource，零逐项布局
            if (!versionsStarted)
            {
                versionsStarted = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    List<string> releases = new List<string>();
                    Exception err = null;
                    try
                    {
                        foreach (var ve in GameCore.GetVersionList())
                            if (ve.Type == "release") releases.Add(ve.Id);
                    }
                    catch (Exception ex) { err = ex; }
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        if (err != null) { Toast("获取版本列表失败：" + err.Message); return; }
                        populating = true;
                        comboLabels = new List<string>(comboLabels);
                        versionValues = new List<string>(versionValues);
                        foreach (var r in releases)
                        {
                            comboLabels.Add(r);
                            versionValues.Add(r);
                        }
                        versionCombo.ItemsSource = comboLabels;
                        versionCombo.SelectedIndex = 0;
                        populating = false;
                    }));
                });
            }
            // 幂等：只有首次进入自动搜索；筛选/搜索词变化由控件事件触发重查
            if (!searchedOnce && !loading) StartSearch();
        }

        string SelectedGameVersion()
        {
            int i = versionCombo.SelectedIndex;
            if (i >= 0 && i < versionValues.Count) return versionValues[i];
            return "";
        }

        void StartSearch()
        {
            if (loading) return;
            loading = true;
            string q = (searchBox.Text ?? "").Trim();
            string v = SelectedGameVersion();
            string pt = ProjectType;
            ShowStatus("加载中...", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Modrinth.SearchHit> res = null;
                Exception err = null;
                try { res = Modrinth.Search(pt, q, v); }
                catch (Exception ex) { err = ex; }
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    loading = false;
                    searchedOnce = true;
                    if (err != null)
                    {
                        Toast(err.Message);
                        ShowStatus("加载失败：" + err.Message, true);
                        return;
                    }
                    allResults = res;
                    shownCount = PageSize;
                    RenderList();
                }));
            });
        }

        // 列表区状态：居中灰字 + 可选重试按钮
        void ShowStatus(string text, bool retry)
        {
            renderSeq++;   // 作废旧列表的分批渲染任务
            listPanel.Children.Clear();
            var tb = new TextBlock();
            tb.Text = text;
            tb.Foreground = Palette.B(Palette.Dim);
            tb.FontSize = 12;
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            tb.Margin = new Thickness(0, 40, 0, retry ? 10 : 40);
            listPanel.Children.Add(tb);
            if (retry)
            {
                var btn = new FlatButton("重试");
                btn.HorizontalAlignment = HorizontalAlignment.Center;
                btn.Margin = new Thickness(0, 0, 0, 40);
                btn.Click += delegate { StartSearch(); };
                listPanel.Children.Add(btn);
            }
        }

        void RenderList()
        {
            renderSeq++;
            int seq = renderSeq;
            listPanel.Children.Clear();
            if (allResults == null || allResults.Count == 0)
            {
                ShowStatus("未找到相关资源", false);
                return;
            }
            int n = Math.Min(shownCount, allResults.Count);
            // 分批构建：每个调度回合只建 4 张卡，输入与动画随时插队，杜绝长队堵死
            Action<int> batch = null;
            batch = delegate(int from)
            {
                if (seq != renderSeq) return;   // 已重渲或清空，作废旧任务
                int to = Math.Min(from + 4, n);
                for (int i = from; i < to; i++) listPanel.Children.Add(BuildHitCard(allResults[i]));
                if (to < n)
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { batch(to); }));
                else if (allResults != null && shownCount < allResults.Count)
                    listPanel.Children.Add(moreButton);
            };
            batch(0);
        }

        // 单个 SearchHit 卡片：图标 / 标题 / 作者 / 简介 / 下载量 / 详情按钮，整卡可点
        Border BuildHitCard(Modrinth.SearchHit hit)
        {
            var card = new Border();
            var cardBrush = Palette.B(Palette.Panel);
            card.Background = cardBrush;
            card.CornerRadius = new CornerRadius(8);
            card.Padding = new Thickness(14);
            card.Margin = new Thickness(0, 0, 0, 10);
            card.Cursor = Cursors.Hand;

            var grid = new Grid();
            grid.ColumnDefinitions.Add(UiHelpers.AutoCol());
            grid.ColumnDefinitions.Add(UiHelpers.StarCol());
            grid.ColumnDefinitions.Add(UiHelpers.AutoCol());
            card.Child = grid;

            var icon = new Image();
            icon.Width = 48;
            icon.Height = 48;
            icon.Margin = new Thickness(0, 0, 12, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            ResourceUiUtil.LoadHitIcon(icon, hit.IconUrl);
            Grid.SetColumn(icon, 0);
            grid.Children.Add(icon);

            var mid = new StackPanel();
            mid.VerticalAlignment = VerticalAlignment.Center;
            var title = new TextBlock();
            title.Text = string.IsNullOrEmpty(hit.Title) ? hit.Slug : hit.Title;
            title.FontSize = 13;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Palette.B(Palette.Text);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            mid.Children.Add(title);
            if (!string.IsNullOrEmpty(hit.Author))
            {
                var author = new TextBlock();
                author.Text = hit.Author;
                author.FontSize = 11;
                author.Foreground = Palette.B(Palette.Dim);
                author.Margin = new Thickness(0, 2, 0, 0);
                mid.Children.Add(author);
            }
            var desc = new TextBlock();
            desc.Text = hit.Description ?? "";
            desc.FontSize = 12;
            desc.Foreground = Palette.B(Palette.Dim);
            desc.TextTrimming = TextTrimming.CharacterEllipsis;
            desc.Margin = new Thickness(0, 4, 0, 0);
            mid.Children.Add(desc);
            Grid.SetColumn(mid, 1);
            grid.Children.Add(mid);

            var right = new StackPanel();
            right.VerticalAlignment = VerticalAlignment.Center;
            right.Margin = new Thickness(12, 0, 0, 0);
            var dls = new TextBlock();
            dls.Text = ResourceUiUtil.FormatDownloads(hit.Downloads) + " 次下载";
            dls.FontSize = 11;
            dls.Foreground = Palette.B(Palette.Dim);
            dls.HorizontalAlignment = HorizontalAlignment.Right;
            dls.Margin = new Thickness(0, 0, 0, 8);
            right.Children.Add(dls);
            var btnDetail = new FlatButton("详情");
            btnDetail.Click += delegate { OpenDetail(hit); };
            right.Children.Add(btnDetail);
            Grid.SetColumn(right, 2);
            grid.Children.Add(right);

            card.MouseEnter += delegate { ThemeManager.AnimateColor(cardBrush, Palette.BtnHover, 150); };
            card.MouseLeave += delegate { ThemeManager.AnimateColor(cardBrush, Palette.Panel, 180); };
            card.MouseLeftButtonUp += delegate { OpenDetail(hit); };
            return card;
        }

        void OpenDetail(Modrinth.SearchHit hit)
        {
            Navigator.Go("download:detail", new object[] { hit, PageKey });
        }
    }

    // ---- 五个列表页（无参构造，主窗口统一 new） ----

    class PageDownloadMod : PageResourceBase
    {
        public PageDownloadMod() : base("mod", "Mod", "download:mod") { }
    }

    class PageDownloadPack : PageResourceBase
    {
        public PageDownloadPack() : base("modpack", "整合包", "download:pack") { }
    }

    class PageDownloadDataPack : PageResourceBase
    {
        public PageDownloadDataPack() : base("datapack", "数据包", "download:datapack") { }
    }

    class PageDownloadResourcePack : PageResourceBase
    {
        public PageDownloadResourcePack() : base("resourcepack", "资源包", "download:resourcepack") { }
    }

    class PageDownloadShader : PageResourceBase
    {
        public PageDownloadShader() : base("shader", "光影包", "download:shader") { }
    }
}
