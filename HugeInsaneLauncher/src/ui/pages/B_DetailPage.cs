// HugeLauncher.Ui.Pages - 社区资源详情页（download:detail）：PCL 式「加载器+游戏版本」分组版本列表
// NavParam 约定：object[]，[0]=Modrinth.SearchHit，[1]=来源页面 key（返回按钮 Navigator.Go 回去用）
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    class PageResourceDetail : PageBase
    {
        Modrinth.SearchHit hit;
        string fromKey;
        string projectType;
        DarkComboBox versionCombo;
        StackPanel listPanel;      // 加载态/筛选行/分组卡的容器（数据到达后重建）
        int fetchSeq;              // 防串页：每次进入 +1，后台回调比对

        List<Modrinth.MrVersion> allFiles;
        List<Group> groups;
        string loadedProjectId;    // 当前 groups 属于哪个项目（张冠李戴防护：就绪判定必须项目级）
        Dictionary<string, Modrinth.SearchHit> depInfo;
        readonly Dictionary<Modrinth.MrVersion, Border> fileRows = new Dictionary<Modrinth.MrVersion, Border>(); // 文件→行元素
        Modrinth.MrVersion recommended;    // 下拉实例的最新精确兼容文件（推荐徽标 + 一键下载的目标）
        FlatButton btnRecommend;
        string activeFilter;       // 当前筛选的版本组，null = 全部
        string selectedKey;        // "（所选版本）"组的 Key
        bool coarseGroups;         // 版本组是否按大版本聚合
        string fetchError;         // 拉取/分组失败原因（H史剧本报错用）
        int renderGen;             // RenderList 代际：分批建行的后台回调据此作废旧批

        // ---- H史剧本接口（全部须在 UI 线程调用） ----

        // 当前请求的项目 id（NavParam 在 Navigate 时即更新，不等 OnEnter——这正是防残留的关键）
        string RequestedProjectId
        {
            get
            {
                var arr = NavParam as object[];
                var h = arr != null && arr.Length > 0 ? arr[0] as Modrinth.SearchHit : null;
                return h == null ? null : h.ProjectId;
            }
        }

        // 分组加载完成标记：分组存在且属于当前请求的项目（旧项目残留永远骗不过这个判定）
        public bool GroupsReady { get { return GroupsReadyFor(RequestedProjectId); } }
        public bool GroupsReadyFor(string pid)
        {
            return groups != null && loadedProjectId != null && pid != null && loadedProjectId == pid;
        }
        public string FetchError { get { return fetchError; } }

        class Group
        {
            public string LoaderLabel;   // mod 专用；"" 表示不按加载器分组
            public string VersionGroup;  // "1.21" / "26.2" / "其他"
            public List<string> Loaders = new List<string>(); // 组内文件支持的加载器并集（小写）
            public List<Modrinth.MrVersion> Files = new List<Modrinth.MrVersion>();
            public string Key { get { return LoaderLabel + "|" + VersionGroup; } }
            // H史剧本用：最近一次渲染产生的分组标题/内容/箭头元素（RenderList 重建时刷新）
            public Grid Head;
            public StackPanel Content;
            public TextBlock Arrow;
            // 懒加载：折叠组不建文件行，首次展开才建（>40 个分批流入，防大卡组一口气建几千控件独占 UI）
            public bool RowsBuilt;
            public bool RowsBuilding;
        }

        public override void OnEnter()
        {
            // 页面被缓存复用，每次进入按 NavParam 重建
            Children.Clear();
            hit = null;
            fromKey = null;
            allFiles = null;
            groups = null;
            loadedProjectId = null;
            depInfo = null;
            activeFilter = null;
            selectedKey = null;
            fetchError = null;
            recommended = null;
            btnRecommend = null;
            fileRows.Clear();
            fetchSeq++;
            var arr = NavParam as object[];
            if (arr != null && arr.Length >= 2)
            {
                hit = arr[0] as Modrinth.SearchHit;
                fromKey = arr[1] as string;
            }
            projectType = ProjectTypeFromKey(fromKey);
            Build();
        }

        static string ProjectTypeFromKey(string key)
        {
            switch (key)
            {
                case "download:pack": return "modpack";
                case "download:datapack": return "datapack";
                case "download:resourcepack": return "resourcepack";
                case "download:shader": return "shader";
                default: return "mod";
            }
        }

        static Border MakeCard()
        {
            var b = new Border();
            b.Background = Palette.B(Palette.Panel);
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(14);
            b.Margin = new Thickness(0, 0, 0, 10);
            return b;
        }

        static TextBlock MakeText(string text, double size, Color color, bool bold)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.Foreground = Palette.B(color);
            if (bold) t.FontWeight = FontWeights.SemiBold;
            return t;
        }

        void GoBack()
        {
            Navigator.Go(string.IsNullOrEmpty(fromKey) ? "download:mod" : fromKey);
        }

        // ---------- 页面骨架 ----------

        void Build()
        {
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel();
            stack.Margin = new Thickness(16);
            scroll.Content = stack;
            Children.Add(scroll);

            if (hit == null)
            {
                var err = MakeText("参数错误", 13, Palette.Dim, false);
                err.HorizontalAlignment = HorizontalAlignment.Center;
                err.Margin = new Thickness(0, 40, 0, 12);
                stack.Children.Add(err);
                var back0 = new FlatButton("← 返回");
                back0.HorizontalAlignment = HorizontalAlignment.Center;
                back0.Click += delegate { GoBack(); };
                stack.Children.Add(back0);
                return;
            }

            // ---- 顶部返回栏 ----
            var bar = new Grid();
            bar.ColumnDefinitions.Add(UiHelpers.AutoCol());
            bar.ColumnDefinitions.Add(UiHelpers.StarCol());
            bar.Margin = new Thickness(0, 0, 0, 10);
            var back = new FlatButton("← 返回");
            back.Click += delegate { GoBack(); };
            Grid.SetColumn(back, 0);
            bar.Children.Add(back);
            var pageTitle = MakeText(string.IsNullOrEmpty(hit.Title) ? hit.Slug : hit.Title, 16, Palette.Text, true);
            pageTitle.VerticalAlignment = VerticalAlignment.Center;
            pageTitle.Margin = new Thickness(12, 0, 0, 0);
            pageTitle.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(pageTitle, 1);
            bar.Children.Add(pageTitle);
            stack.Children.Add(bar);

            // ---- 信息卡 ----
            var info = MakeCard();
            var ig = new Grid();
            ig.ColumnDefinitions.Add(UiHelpers.AutoCol());
            ig.ColumnDefinitions.Add(UiHelpers.StarCol());
            info.Child = ig;
            var icon = new Image();
            icon.Width = 64;
            icon.Height = 64;
            icon.Margin = new Thickness(0, 0, 14, 0);
            icon.VerticalAlignment = VerticalAlignment.Top;
            ResourceUiUtil.LoadHitIcon(icon, hit.IconUrl);
            Grid.SetColumn(icon, 0);
            ig.Children.Add(icon);
            var im = new StackPanel();
            im.VerticalAlignment = VerticalAlignment.Center;
            im.Children.Add(MakeText(pageTitle.Text, 15, Palette.Text, true));
            var meta = MakeText("作者：" + (string.IsNullOrEmpty(hit.Author) ? "未知" : hit.Author)
                + "    下载量：" + ResourceUiUtil.FormatDownloads(hit.Downloads), 11, Palette.Dim, false);
            meta.Margin = new Thickness(0, 3, 0, 0);
            im.Children.Add(meta);
            var desc = MakeText(hit.Description ?? "", 12, Palette.Text, false);
            desc.TextWrapping = TextWrapping.Wrap;
            desc.Margin = new Thickness(0, 8, 0, 0);
            im.Children.Add(desc);
            Grid.SetColumn(im, 1);
            ig.Children.Add(im);
            stack.Children.Add(info);

            // ---- 目标卡（mod 等选实例；整合包只提示） ----
            bool isModpack = projectType == "modpack";
            var target = MakeCard();
            var tsp = new StackPanel();
            target.Child = tsp;
            tsp.Children.Add(MakeText(isModpack ? "安装" : "安装到版本", 13, Palette.Text, true));
            if (isModpack)
            {
                var hint = MakeText("点击下面任意版本行，将把该整合包安装为新的游戏实例。", 12, Palette.Dim, false);
                hint.Margin = new Thickness(0, 6, 0, 0);
                tsp.Children.Add(hint);
            }
            else
            {
                versionCombo = new DarkComboBox();
                versionCombo.Width = 280;
                versionCombo.Margin = new Thickness(0, 8, 0, 0);
                versionCombo.HorizontalAlignment = HorizontalAlignment.Left;
                List<string> installed = null;
                try { installed = GameCore.GetInstalledVersions(); } catch { }
                if (installed != null)
                {
                    string cur = Cfg != null ? Cfg.VersionId : null;
                    foreach (var id in installed)
                    {
                        versionCombo.Items.Add(id);
                        if (id == cur) versionCombo.SelectedIndex = versionCombo.Items.Count - 1;
                    }
                }
                if (versionCombo.SelectedIndex < 0 && versionCombo.Items.Count > 0)
                    versionCombo.SelectedIndex = 0;
                versionCombo.SelectionChanged += delegate
                {
                    if (groups == null) return;
                    UpdateSelectedKey();
                    RenderList();
                };
                // 下拉框 + 一键下载推荐版本（目标 = 该实例的最新精确兼容文件，找不到就置灰明说）
                var targetRow = new StackPanel();
                targetRow.Orientation = Orientation.Horizontal;
                targetRow.Children.Add(versionCombo);
                btnRecommend = new FlatButton("一键完整下载（包括依赖项）");
                btnRecommend.SetPadding(new Thickness(12, 6, 12, 6));
                btnRecommend.Margin = new Thickness(8, 8, 0, 0);
                btnRecommend.VerticalAlignment = VerticalAlignment.Top;
                btnRecommend.SetEnabled(false);
                btnRecommend.Click += delegate
                {
                    var f = recommended;
                    if (f != null) InstallFile(f);
                };
                targetRow.Children.Add(btnRecommend);
                tsp.Children.Add(targetRow);
                if (versionCombo.Items.Count == 0)
                {
                    var none = MakeText("未检测到已安装版本，请先在下载页安装游戏。", 11, Palette.Dim, false);
                    none.Margin = new Thickness(0, 8, 0, 0);
                    tsp.Children.Add(none);
                }
            }
            stack.Children.Add(target);

            // ---- 版本列表区（筛选行 + 分组卡） ----
            listPanel = new StackPanel();
            stack.Children.Add(listPanel);
            StartFetch();
        }

        // ---------- 数据拉取 ----------

        void ShowLoading(string text)
        {
            listPanel.Children.Clear();
            var p = new StackPanel();
            p.Margin = new Thickness(0, 30, 0, 30);
            var spin = MakeText("◌", 26, ThemeManager.CurrentColor, false);
            spin.HorizontalAlignment = HorizontalAlignment.Center;
            spin.RenderTransformOrigin = new Point(0.5, 0.5);
            var tf = new RotateTransform();
            spin.RenderTransform = tf;
            if (ThemeManager.AnimOn)
            {
                var da = new DoubleAnimation(0, 360, ThemeManager.Dur(1000));
                da.RepeatBehavior = RepeatBehavior.Forever;
                tf.BeginAnimation(RotateTransform.AngleProperty, da);
            }
            p.Children.Add(spin);
            var t = MakeText(text, 11, Palette.Dim, false);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 8, 0, 0);
            p.Children.Add(t);
            listPanel.Children.Add(p);
        }

        void ShowError(string msg)
        {
            fetchError = msg;
            listPanel.Children.Clear();
            var t = MakeText("获取版本列表失败：" + msg, 12, Palette.Dim, false);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.Margin = new Thickness(0, 30, 0, 12);
            listPanel.Children.Add(t);
            var retry = new FlatButton("重试");
            retry.HorizontalAlignment = HorizontalAlignment.Center;
            retry.Click += delegate { StartFetch(); };
            listPanel.Children.Add(retry);
        }

        void StartFetch()
        {
            ShowLoading("正在获取版本列表...");
            int seq = fetchSeq;
            string pid = hit.ProjectId;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<Modrinth.MrVersion> files = null;
                Exception error = null;
                var deps = new Dictionary<string, Modrinth.SearchHit>();
                try
                {
                    files = Modrinth.GetVersionList(pid);
                    // 收集全部前置（封顶 20 个），批量取工程信息
                    var depIds = new List<string>();
                    foreach (var f in files)
                        foreach (var d in f.RequiredDeps)
                            if (!depIds.Contains(d)) depIds.Add(d);
                    if (depIds.Count > 20) depIds = depIds.GetRange(0, 20);
                    foreach (var h in Modrinth.GetProjects(depIds)) deps[h.ProjectId] = h;
                }
                catch (Exception ex) { error = ex; }
                // Background 优先级：数据到达不插队输入与渲染，避免回调把点击/动画饿停
                Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                {
                    if (seq != fetchSeq) return; // 已离开页面
                    if (error != null) { ShowError(error.Message); return; }
                    allFiles = files;
                    depInfo = deps;
                    loadedProjectId = pid;   // 归属标记：这批分组是为哪个项目拉的
                    BuildGroups();
                    RenderList();
                }));
            });
        }

        // ---------- 分组 ----------

        // 游戏版本比较：纯数字点分版本按段比（新→旧），其余（快照等）排在数字版之后
        static bool TryParseGv(string gv, out int[] nums)
        {
            nums = null;
            if (string.IsNullOrEmpty(gv)) return false;
            var parts = gv.Split('.');
            var l = new List<int>();
            foreach (var p in parts)
            {
                int n;
                if (!int.TryParse(p, out n)) return false;
                l.Add(n);
            }
            nums = l.ToArray();
            return true;
        }

        static int CompareGvDesc(string a, string b)
        {
            int[] na, nb;
            bool oka = TryParseGv(a, out na), okb = TryParseGv(b, out nb);
            if (oka && okb)
            {
                for (int i = 0; i < Math.Max(na.Length, nb.Length); i++)
                {
                    int xa = i < na.Length ? na[i] : 0;
                    int xb = i < nb.Length ? nb[i] : 0;
                    if (xa != xb) return xa > xb ? -1 : 1;
                }
                return 0;
            }
            if (oka) return -1;
            if (okb) return 1;
            return string.CompareOrdinal(b, a);
        }

        // "1.21.11" → "1.21"；已是两段或不可解析则原样
        static string MajorOf(string gv)
        {
            int[] nums;
            if (TryParseGv(gv, out nums) && nums.Length >= 3)
            {
                var parts = gv.Split('.');
                return parts[0] + "." + parts[1];
            }
            return gv;
        }

        static string NewestGv(List<string> gvs)
        {
            string best = null;
            if (gvs == null) return null;
            foreach (var gv in gvs)
                if (best == null || CompareGvDesc(gv, best) < 0) best = gv;
            return best;
        }

        static string CapitalizeLoader(string l)
        {
            switch (l)
            {
                case "fabric": return "Fabric";
                case "forge": return "Forge";
                case "neoforge": return "NeoForge";
                case "quilt": return "Quilt";
                case "liteloader": return "LiteLoader";
                default: return l;
            }
        }

        static string LoaderText(List<string> loaders)
        {
            var parts = new List<string>();
            foreach (var l in loaders) parts.Add(CapitalizeLoader(l));
            parts.Sort(StringComparer.Ordinal);
            return string.Join("、", parts.ToArray());
        }

        void BuildGroups()
        {
            groups = new List<Group>();
            // 版本组标签：不同游戏版本超过 9 个时按大版本聚合
            var distinct = new List<string>();
            foreach (var f in allFiles)
                foreach (var gv in f.GameVersions)
                    if (!distinct.Contains(gv)) distinct.Add(gv);
            coarseGroups = distinct.Count > 9;

            bool forMod = projectType == "mod";
            foreach (var f in allFiles)
            {
                if (f.File == null) continue; // 没有可下载文件的版本不进列表
                string newest = NewestGv(f.GameVersions);
                string vg = newest == null ? "其他" : (coarseGroups ? MajorOf(newest) : newest);
                string ll = "";
                if (forMod) ll = f.Loaders.Count == 0 ? "通用" : LoaderText(f.Loaders);
                Group g = null;
                foreach (var gg in groups)
                    if (gg.LoaderLabel == ll && gg.VersionGroup == vg) { g = gg; break; }
                if (g == null)
                {
                    g = new Group { LoaderLabel = ll, VersionGroup = vg };
                    groups.Add(g);
                }
                foreach (var l in f.Loaders)
                    if (!g.Loaders.Contains(l)) g.Loaders.Add(l);
                g.Files.Add(f);
            }
            groups.Sort(delegate(Group a, Group b)
            {
                if (a.VersionGroup == "其他" && b.VersionGroup != "其他") return 1;
                if (b.VersionGroup == "其他" && a.VersionGroup != "其他") return -1;
                int c = CompareGvDesc(a.VersionGroup, b.VersionGroup);
                if (c != 0) return c;
                return string.CompareOrdinal(a.LoaderLabel, b.LoaderLabel);
            });
            UpdateSelectedKey();
        }

        // 当前下拉实例对应的组 → 置顶展开并标注"（所选版本）"
        void UpdateSelectedKey()
        {
            selectedKey = null;
            if (groups != null && projectType != "modpack" && versionCombo != null)
            {
                string id = versionCombo.SelectedItem as string;
                if (!string.IsNullOrEmpty(id))
                {
                    string gv = GameCore.BaseGameVersion(id);
                    string loader = Modrinth.DetectLoader(id);
                    string vg = coarseGroups ? MajorOf(gv) : gv;
                    foreach (var g in groups)
                    {
                        if (g.VersionGroup != vg) continue;
                        if (projectType != "mod") { selectedKey = g.Key; break; }
                        if (loader == "vanilla")
                        {
                            if (g.LoaderLabel == "通用") { selectedKey = g.Key; break; }
                        }
                        else if (g.Loaders.Contains(loader)) { selectedKey = g.Key; break; }
                    }
                }
            }
            UpdateRecommendation();
        }

        // 推荐目标 = 下拉实例的最新精确兼容文件；按钮同步「可用 / 置灰+原因」（找不到不让你装错）
        void UpdateRecommendation()
        {
            recommended = null;
            if (btnRecommend == null) return;
            string id = versionCombo != null ? versionCombo.SelectedItem as string : null;
            if (groups == null || string.IsNullOrEmpty(id))
            {
                btnRecommend.Text = "一键完整下载（包括依赖项）";
                btnRecommend.ToolTip = null;
                btnRecommend.SetEnabled(false);
                return;
            }
            string gv = GameCore.BaseGameVersion(id);
            string loader = Modrinth.DetectLoader(id);
            var g = FindTargetGroup(loader, gv);
            if (g != null) recommended = NewestCompatibleIn(g, loader, gv);
            if (recommended != null)
            {
                btnRecommend.Text = "一键完整下载（包括依赖项）";
                btnRecommend.ToolTip = recommended.File != null ? recommended.File.Filename : null;
                btnRecommend.SetEnabled(true);
            }
            else
            {
                btnRecommend.Text = "无适配 " + gv + " 的版本";
                btnRecommend.ToolTip = "该资源还没有支持 " + gv
                    + (loader == "vanilla" ? "" : " " + CapitalizeLoader(loader)) + " 的文件";
                btnRecommend.SetEnabled(false);
            }
        }

        // 展示序：所选组内精确兼容的文件排前面（不改 Files 原序，H史选行逻辑不受影响）
        List<Modrinth.MrVersion> DisplayFiles(Group g)
        {
            if (g.Key != selectedKey || projectType == "modpack" || versionCombo == null) return g.Files;
            string id = versionCombo.SelectedItem as string;
            if (string.IsNullOrEmpty(id)) return g.Files;
            string gv = GameCore.BaseGameVersion(id);
            string loader = Modrinth.DetectLoader(id);
            var res = new List<Modrinth.MrVersion>(g.Files.Count);
            foreach (var f in g.Files) if (FileCompatible(f, loader, gv)) res.Add(f);
            foreach (var f in g.Files) if (!FileCompatible(f, loader, gv)) res.Add(f);
            return res;
        }

        // ---------- 渲染 ----------

        void RenderList()
        {
            renderGen++;   // 作废旧一代的分批建行回调
            listPanel.Children.Clear();
            fileRows.Clear();
            if (allFiles == null) return;
            if (allFiles.Count == 0)
            {
                var t = MakeText("该资源没有可用文件", 12, Palette.Dim, false);
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.Margin = new Thickness(0, 30, 0, 30);
                listPanel.Children.Add(t);
                return;
            }

            // 筛选行：全部 + 各版本组（≥2 个才显示）
            var filterValues = new List<string>();
            foreach (var g in groups)
                if (!filterValues.Contains(g.VersionGroup)) filterValues.Add(g.VersionGroup);
            filterValues.Sort(CompareGvDesc);
            if (filterValues.Count >= 2)
            {
                var fr = new WrapPanel();
                fr.Margin = new Thickness(0, 0, 0, 10);
                AddFilterButton(fr, "全部", null);
                foreach (var vg in filterValues) AddFilterButton(fr, vg, vg);
                listPanel.Children.Add(fr);
            }

            // 所选版本组置顶，其余按序
            var ordered = new List<Group>();
            foreach (var g in groups) if (g.Key == selectedKey) ordered.Add(g);
            foreach (var g in groups) if (g.Key != selectedKey) ordered.Add(g);
            foreach (var g in ordered)
            {
                if (activeFilter != null && g.VersionGroup != activeFilter) continue;
                listPanel.Children.Add(BuildGroupCard(g));
            }
        }

        void AddFilterButton(WrapPanel panel, string text, string value)
        {
            var b = new FlatButton(text);
            b.SetToggle(true);
            b.SetFontSize(11);
            b.SetPadding(new Thickness(10, 4, 10, 4));
            b.Margin = new Thickness(0, 0, 6, 6);
            bool on = activeFilter == value;
            if (on) b.SetChecked(true, false);
            string v = value;
            b.Click += delegate
            {
                activeFilter = activeFilter == v ? null : v; // 再点一次取消筛选
                RenderList();
            };
            panel.Children.Add(b);
        }

        Border BuildGroupCard(Group g)
        {
            var card = MakeCard();
            var sp = new StackPanel();
            card.Child = sp;

            bool isSelected = g.Key == selectedKey;
            string title = (g.LoaderLabel.Length > 0 ? g.LoaderLabel + " · " : "") + g.VersionGroup;
            if (isSelected) title += "（所选版本）";

            var head = new Grid();
            head.Height = 34;
            head.Cursor = Cursors.Hand;
            head.ColumnDefinitions.Add(UiHelpers.StarCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            var ht = MakeText(title, 13, isSelected ? ThemeManager.CurrentColor : Palette.Text, true);
            ht.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(ht);
            var hr = MakeText(g.Files.Count + " 个版本  ▾", 11, Palette.Dim, false);
            hr.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(hr, 1);
            head.Children.Add(hr);
            sp.Children.Add(head);

            var content = new StackPanel();
            sp.Children.Add(content);
            g.Head = head;
            g.Content = content;
            g.Arrow = hr;

            // 前置资源区块（仅 mod 且有前置时）：取组内并集，封顶 4 个
            if (projectType == "mod")
            {
                var deps = new List<string>();
                foreach (var f in g.Files)
                    foreach (var d in f.RequiredDeps)
                        if (!deps.Contains(d)) deps.Add(d);
                if (deps.Count > 0)
                {
                    var dt = MakeText("前置资源", 12, Palette.Text, true);
                    dt.Margin = new Thickness(0, 4, 0, 4);
                    content.Children.Add(dt);
                    int shown = 0;
                    foreach (var d in deps)
                    {
                        if (shown >= 4) break;
                        var row = BuildDepRow(d);
                        if (row == null) continue;
                        content.Children.Add(row);
                        shown++;
                    }
                    if (deps.Count > shown)
                    {
                        var more = MakeText("…共 " + deps.Count + " 项前置，安装时将自动补装", 10.5, Palette.Dim, false);
                        more.Margin = new Thickness(0, 2, 0, 4);
                        content.Children.Add(more);
                    }
                    var vt = MakeText("版本列表", 12, Palette.Text, true);
                    vt.Margin = new Thickness(0, 6, 0, 4);
                    content.Children.Add(vt);
                }
            }

            // 折叠组建卡时只建标题与前置区块；文件行等首次展开再建（懒加载，>40 个分批流入）
            g.RowsBuilt = false;
            g.RowsBuilding = false;
            bool expanded = isSelected || groups.Count == 1;
            if (expanded) EnsureRows(g);
            content.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            hr.Text = g.Files.Count + " 个版本  " + (expanded ? "▴" : "▾");
            head.MouseLeftButtonUp += delegate
            {
                bool show = content.Visibility != Visibility.Visible;
                hr.Text = g.Files.Count + " 个版本  " + (show ? "▴" : "▾");
                if (show)
                {
                    EnsureRows(g);
                    content.Opacity = 0;
                    content.Visibility = Visibility.Visible;
                    ThemeManager.AnimateDouble(content, UIElement.OpacityProperty, 1, 150);
                }
                else content.Visibility = Visibility.Collapsed;
            };
            return card;
        }

        // ---------- 文件行懒加载 ----------

        void AddRowIfMissing(Group g, Modrinth.MrVersion f)
        {
            if (fileRows.ContainsKey(f)) return;
            g.Content.Children.Add(BuildFileRow(f));   // BuildFileRow 内注册 fileRows[f]
        }

        // 同步建齐组内全部文件行（H史剧本与小分组用；与分批循环靠 RowsBuilt/ContainsKey 互不重复）
        void BuildRowsSync(Group g)
        {
            if (g.RowsBuilt) return;
            g.RowsBuilt = true;      // 先置位，进行中的分批循环下一步即停
            g.RowsBuilding = false;
            foreach (var f in DisplayFiles(g)) AddRowIfMissing(g, f);
        }

        // 大分组分批建行：每批 30 行，Background 优先级让位于输入与渲染；RenderList 换代即作废
        void BuildRowsBatched(Group g, int gen)
        {
            if (g.RowsBuilt || g.RowsBuilding) return;
            g.RowsBuilding = true;
            var files = DisplayFiles(g);   // 兼容置顶的展示序（选中组才有变化）
            int i = 0;
            Action step = null;
            step = delegate
            {
                if (gen != renderGen || g.RowsBuilt) { g.RowsBuilding = false; return; }
                int n = 0;
                while (i < files.Count && n < 30)
                {
                    AddRowIfMissing(g, files[i]);
                    i++; n++;
                }
                if (i < files.Count) Dispatcher.BeginInvoke(DispatcherPriority.Background, step);
                else { g.RowsBuilt = true; g.RowsBuilding = false; }
            };
            Dispatcher.BeginInvoke(DispatcherPriority.Background, step);
        }

        void EnsureRows(Group g)
        {
            if (g.RowsBuilt || g.RowsBuilding) return;
            if (g.Files.Count <= 40) BuildRowsSync(g);
            else BuildRowsBatched(g, renderGen);
        }

        UIElement BuildDepRow(string projectId)
        {
            Modrinth.SearchHit dh;
            if (depInfo == null || !depInfo.TryGetValue(projectId, out dh)) return null;
            var row = new Border();
            row.Height = 34;
            row.CornerRadius = new CornerRadius(6);
            row.Padding = new Thickness(6, 0, 6, 0);
            row.Background = Brushes.Transparent;
            row.Cursor = Cursors.Hand;
            row.ToolTip = "点击前往前置详情页";
            row.MouseEnter += delegate { row.Background = Palette.B(Palette.BtnHover); };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };

            var dock = new DockPanel();
            var img = new Image();
            img.Width = 22;
            img.Height = 22;
            img.VerticalAlignment = VerticalAlignment.Center;
            img.Margin = new Thickness(0, 0, 8, 0);
            ResourceUiUtil.LoadHitIcon(img, dh.IconUrl);
            dock.Children.Add(img);
            var name = MakeText(string.IsNullOrEmpty(dh.Title) ? dh.Slug : dh.Title, 12, Palette.Text, false);
            name.VerticalAlignment = VerticalAlignment.Center;
            dock.Children.Add(name);
            row.Child = dock;

            Modrinth.SearchHit target = dh;
            row.MouseLeftButtonUp += delegate
            {
                Navigator.Go("download:detail", new object[] { target, fromKey });
            };
            return row;
        }

        UIElement BuildFileRow(Modrinth.MrVersion f)
        {
            var row = new Border();
            row.Height = 50;
            row.CornerRadius = new CornerRadius(6);
            row.Padding = new Thickness(8, 4, 8, 4);
            row.Background = Brushes.Transparent;
            row.Cursor = Cursors.Hand;
            row.ToolTip = "点击安装此版本";
            row.MouseEnter += delegate { row.Background = Palette.B(Palette.BtnHover); };
            row.MouseLeave += delegate { row.Background = Brushes.Transparent; };

            var dock = new DockPanel();

            // 右侧发布类型徽标
            Color badgeBg, badgeFg;
            string badgeText;
            if (f.VersionType == "beta")
            { badgeBg = Color.FromRgb(0x3A, 0x33, 0x20); badgeFg = Palette.Yellow; badgeText = "Beta"; }
            else if (f.VersionType == "alpha")
            { badgeBg = Color.FromRgb(0x3A, 0x24, 0x24); badgeFg = Palette.Red; badgeText = "Alpha"; }
            else
            { badgeBg = Color.FromRgb(0x21, 0x36, 0x2A); badgeFg = Palette.Green; badgeText = "正式"; }
            var badge = new Border();
            badge.Background = Palette.B(badgeBg);
            badge.CornerRadius = new CornerRadius(3);
            badge.Padding = new Thickness(5, 1, 5, 1);
            badge.VerticalAlignment = VerticalAlignment.Center;
            badge.Child = MakeText(badgeText, 10, badgeFg, false);
            DockPanel.SetDock(badge, Dock.Right);
            dock.Children.Add(badge);

            // 推荐徽标：精确兼容下拉实例的最新文件（贴在发布类型徽标左侧）
            if (f == recommended)
            {
                var rec = new Border();
                rec.Background = Palette.B(Color.FromRgb(0x21, 0x36, 0x2A));
                rec.CornerRadius = new CornerRadius(3);
                rec.Padding = new Thickness(5, 1, 5, 1);
                rec.Margin = new Thickness(0, 0, 4, 0);
                rec.VerticalAlignment = VerticalAlignment.Center;
                rec.Child = MakeText("推荐", 10, Palette.Green, false);
                DockPanel.SetDock(rec, Dock.Right);
                dock.Children.Add(rec);
            }

            var left = new StackPanel();
            left.VerticalAlignment = VerticalAlignment.Center;
            var name = MakeText(f.Name ?? f.VersionNumber ?? f.File.Filename, 12.5, Palette.Text, true);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            left.Children.Add(name);

            var infoParts = new List<string>();
            infoParts.Add(f.File.Filename);
            if (projectType == "mod" && f.Loaders.Count > 0) infoParts.Add(LoaderText(f.Loaders));
            if (f.GameVersions.Count > 0)
            {
                var gvs = new List<string>(f.GameVersions.ToArray());
                gvs.Sort(CompareGvDesc);
                string s = "游戏版本 ";
                int n = Math.Min(gvs.Count, 3);
                for (int i = 0; i < n; i++) { if (i > 0) s += "、"; s += gvs[i]; }
                if (gvs.Count > 3) s += " 等";
                infoParts.Add(s);
            }
            infoParts.Add(ResourceUiUtil.FormatDownloads(f.Downloads) + " 下载");
            string rel = RelTime(f.DatePublished);
            if (rel != null) infoParts.Add(rel);
            if (projectType == "mod" && f.RequiredDeps.Count > 0) infoParts.Add(f.RequiredDeps.Count + " 项前置");
            var info = MakeText(string.Join(" · ", infoParts.ToArray()), 10.5, Palette.Dim, false);
            info.TextTrimming = TextTrimming.CharacterEllipsis;
            left.Children.Add(info);

            dock.Children.Add(left);
            row.Child = dock;
            row.MouseLeftButtonUp += delegate { InstallFile(f); };
            fileRows[f] = row;
            return row;
        }

        static string RelTime(string iso)
        {
            DateTime dt;
            if (!DateTime.TryParse(iso, out dt)) return null;
            var span = DateTime.Now - dt.ToLocalTime();
            if (span.TotalDays < 1) return "今天更新";
            if (span.TotalDays < 30) return "更新于 " + (int)span.TotalDays + " 天前";
            if (span.TotalDays < 365) return "更新于 " + (int)(span.TotalDays / 30) + " 个月前";
            return "更新于 " + (int)(span.TotalDays / 365) + " 年前";
        }

        // ---------- 安装 ----------

        // 返回任务条目（H史 WatchProgress 盯它）；入口校验失败返回 null
        DownloadTaskItem InstallFile(Modrinth.MrVersion f)
        {
            if (f == null || f.File == null) { Toast("该版本没有可下载的文件"); return null; }
            bool isModpack = projectType == "modpack";
            string versionId = null, gameVersion = null, loader = null;
            if (!isModpack)
            {
                versionId = versionCombo != null ? versionCombo.SelectedItem as string : null;
                if (string.IsNullOrEmpty(versionId))
                {
                    Toast("请先选择要安装到的版本");
                    return null;
                }
                loader = Modrinth.DetectLoader(versionId);
                gameVersion = GameCore.BaseGameVersion(versionId);
            }

            string taskName = "下载 " + hit.Title + " " + (f.VersionNumber ?? f.Name ?? "");
            var reporter = TaskCenter.Register(taskName);
            DownloadTaskItem item = null;
            lock (TaskCenter.Tasks)
            {
                foreach (var tsk in TaskCenter.Tasks)
                {
                    if (!tsk.Finished && tsk.Name == taskName) { item = tsk; break; }
                }
            }
            Navigator.Go("download:speed");

            string title = hit.Title;
            string ptype = projectType;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    if (isModpack)
                    {
                        // 整合包已注册为同名版本（inheritsFrom 基础版本），装完自动选中，回主界面直接启动
                        string newVer = Modrinth.InstallModpackFile(f, title, reporter);
                        if (Cfg != null)
                        {
                            Cfg.VersionId = newVer;
                            try { Cfg.Save(); } catch { }
                        }
                    }
                    else
                    {
                        // 一键完整下载：主项+前置摊平成计划，每个文件独立任务条目，全部出现在下载列表
                        Modrinth.BeginInstall();
                        reporter(StepEvent.Start(0, "解析前置依赖"));
                        var plan = Modrinth.ResolveInstallPlan(ptype, f, gameVersion, loader);
                        reporter(StepEvent.Done(0, true, null));
                        reporter(StepEvent.Start(1, "下载文件（共 " + plan.Count + " 个）"));

                        // 前置各注册独立任务条目（主项条目 = 入口已注册的 item）
                        var depItems = new DownloadTaskItem[plan.Count];
                        depItems[0] = item;
                        for (int i = 1; i < plan.Count; i++)
                        {
                            string dn = "下载前置 " + (plan[i].Name ?? plan[i].File.Filename);
                            TaskCenter.Register(dn);
                            lock (TaskCenter.Tasks)
                                foreach (var tsk in TaskCenter.Tasks)
                                    if (!tsk.Finished && tsk.Name == dn) { depItems[i] = tsk; break; }
                        }

                        bool anyDepFailed = false;
                        for (int i = 0; i < plan.Count; i++)
                        {
                            var pv = plan[i];
                            try
                            {
                                Modrinth.DownloadResolvedFile(ptype, pv, versionId);
                                if (depItems[i] != null) TaskCenter.Complete(depItems[i], true, "");
                            }
                            catch (Exception exd)
                            {
                                if (depItems[i] != null) TaskCenter.Complete(depItems[i], false, exd.Message);
                                if (i == 0) throw;   // 主文件失败 = 整体失败
                                anyDepFailed = true;
                                Modrinth.NoteFailedDep(!string.IsNullOrEmpty(pv.ProjectId)
                                    ? pv.ProjectId : (pv.Name ?? pv.File.Filename));
                            }
                        }
                        reporter(StepEvent.Done(1, true, null));
                        if (anyDepFailed) reporter(StepEvent.LogLine("部分前置下载失败，详见各任务条目"));
                    }
                    if (item != null) TaskCenter.Complete(item, true, "");
                    // 前置有没装上的要明示：toast 点出来，让用户知道要手动补
                    string depWarn = "";
                    if (Modrinth.FailedDeps.Count > 0)
                    {
                        depWarn = "（" + Modrinth.FailedDeps.Count + " 个前置没能自动安装，启动若报缺前置请手动补装）";
                        reporter(StepEvent.LogLine("前置补装失败: " + string.Join("、", Modrinth.FailedDeps.ToArray())));
                    }
                    UiToast("已安装 " + title + depWarn);
                }
                catch (Exception ex)
                {
                    string msg = ex.Message ?? "未知错误";
                    if (!msg.Contains("HIL 找不到可用的游戏版本")
                        && (msg.Contains("找不到") || msg.Contains("没有可用") || msg.Contains("匹配")))
                        msg = "HIL 找不到可用的游戏版本：" + msg;
                    if (item != null) TaskCenter.Complete(item, false, msg);
                    UiToast(msg);
                }
            });
            return item;
        }

        // ---------- H史剧本开口（全部须在 UI 线程调用；选组规则与 Bug4 加载器过滤打通） ----------

        // 按「加载器+基版本号」找全部候选分组；整合包取最新组
        List<Group> CandidateGroups(string loader, string gameVersion)
        {
            var res = new List<Group>();
            if (groups == null || groups.Count == 0) return res;
            if (projectType == "modpack") { res.Add(groups[0]); return res; }   // BuildGroups 已按新→旧排序
            string vg = coarseGroups ? MajorOf(gameVersion) : gameVersion;
            foreach (var g in groups)
            {
                if (g.VersionGroup != vg) continue;
                if (projectType != "mod") { res.Add(g); continue; }   // 资源包/光影/数据包只按版本组
                if (loader == "vanilla")
                {
                    if (g.LoaderLabel == "通用") res.Add(g);
                }
                else if (g.Loaders.Contains(loader)) res.Add(g);
            }
            return res;
        }

        // 组内最新兼容文件（Files 新的在前，首个兼容即最新）
        Modrinth.MrVersion NewestCompatibleIn(Group g, string loader, string gameVersion)
        {
            foreach (var f in g.Files)
                if (FileCompatible(f, loader, gameVersion)) return f;
            return null;
        }

        // 目标组 = 候选组中「最新兼容文件最新」的那个。加载器组合会拆出多个组（如 Fabric / Fabric、Quilt 并存），
        // 只按标签序取第一组会把真正兼容的文件挡在门外。都无兼容文件时退回首个候选组（选文件仍会落空，调用方报错，不装错）
        Group FindTargetGroup(string loader, string gameVersion)
        {
            var cands = CandidateGroups(loader, gameVersion);
            if (cands.Count == 0) return null;
            Group best = null;
            Modrinth.MrVersion bestFile = null;
            foreach (var g in cands)
            {
                var f = NewestCompatibleIn(g, loader, gameVersion);
                if (f == null) continue;
                if (best == null || string.CompareOrdinal(f.DatePublished ?? "", bestFile.DatePublished ?? "") > 0)
                {
                    best = g;
                    bestFile = f;
                }
            }
            return best ?? cands[0];
        }

        // 组内文件兼容性：游戏版本精确匹配；mod 另需加载器匹配（vanilla 只认无加载器标注的"通用"文件）
        bool FileCompatible(Modrinth.MrVersion f, string loader, string gameVersion)
        {
            if (projectType == "modpack") return true;
            if (!string.IsNullOrEmpty(gameVersion) && !f.GameVersions.Contains(gameVersion)) return false;
            if (projectType != "mod") return true;
            if (loader == "vanilla") return f.Loaders.Count == 0;
            return f.Loaders.Contains(loader);
        }

        // 滚动到目标分组标题（头跟滚动走，不挥手），返回标题元素；无匹配返回 null
        public FrameworkElement FindGroupHead(string loader, string gameVersion)
        {
            Group g = FindTargetGroup(loader, gameVersion);
            if (g == null) return null;
            // 目标组被筛选器挡掉时先清筛选重渲染，保证标题真在列表里
            if (activeFilter != null && g.VersionGroup != activeFilter)
            {
                activeFilter = null;
                RenderList();
            }
            if (g.Head == null) return null;
            try { g.Head.BringIntoView(); } catch { }
            return g.Head;
        }

        // 展开目标分组：与鼠标点标题的展开分支完全相同的操作（已展开则不动）；行必须同步建齐，下一步选行要用
        public bool AiExpandGroup(string loader, string gameVersion)
        {
            Group g = FindTargetGroup(loader, gameVersion);
            if (g == null || g.Content == null) return false;
            BuildRowsSync(g);
            if (g.Content.Visibility == Visibility.Visible) return true;
            if (g.Arrow != null) g.Arrow.Text = g.Files.Count + " 个版本  ▴";
            g.Content.Opacity = 0;
            g.Content.Visibility = Visibility.Visible;
            ThemeManager.AnimateDouble(g.Content, UIElement.OpacityProperty, 1, 150);
            return true;
        }

        // 组内选最新兼容文件行（Files 新的在前，首个兼容即最新），滚动到该行后返回文件与行元素
        public Modrinth.MrVersion AiPickFile(string loader, string gameVersion, out FrameworkElement row)
        {
            row = null;
            Group g = FindTargetGroup(loader, gameVersion);
            if (g == null) return null;
            foreach (var f in g.Files)
            {
                if (!FileCompatible(f, loader, gameVersion)) continue;
                Border r;
                if (fileRows.TryGetValue(f, out r) && r != null)
                {
                    row = r;
                    try { r.BringIntoView(); } catch { }
                }
                return f;
            }
            return null;
        }

        // 真实触发下载：非整合包先把目标实例同步进下拉框（InstallFile 读它），再调真实 InstallFile
        public DownloadTaskItem AiInstallFile(string loader, string gameVersion, string targetInstance, out Modrinth.MrVersion picked)
        {
            picked = AiPickFile(loader, gameVersion);
            if (picked == null) return null;
            if (projectType != "modpack" && versionCombo != null && !string.IsNullOrEmpty(targetInstance))
            {
                foreach (var it in versionCombo.Items)
                {
                    if ((string)it == targetInstance) { versionCombo.SelectedItem = it; break; }
                }
            }
            return InstallFile(picked);
        }

        // 重载：不看行元素时使用
        Modrinth.MrVersion AiPickFile(string loader, string gameVersion)
        {
            FrameworkElement row;
            return AiPickFile(loader, gameVersion, out row);
        }

        void UiToast(string msg)
        {
            Dispatcher.BeginInvoke(new Action(delegate { Toast(msg); }));
        }
    }
}
