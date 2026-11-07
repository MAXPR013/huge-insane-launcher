// HugeLauncher.Ui.Pages - 更多·工具：种子地图 / 合成查询 / 游戏内新手引导
// 导航 key: more:tools
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    class PageTools : PageBase
    {
        Grid bodyHost;
        StackPanel tabs;
        string tab = Environment.GetEnvironmentVariable("HIL_TOOLS_TAB") ?? "seed";

        public PageTools()
        {
            RowDefinitions.Add(UiHelpers.AutoRow());
            RowDefinitions.Add(UiHelpers.StarRow());

            var head = new StackPanel();
            head.Margin = new Thickness(16, 14, 16, 6);
            var title = new TextBlock();
            title.Text = "工具";
            title.FontSize = 17;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Palette.B(Palette.Text);
            head.Children.Add(title);

            tabs = new StackPanel();
            tabs.Orientation = Orientation.Horizontal;
            tabs.Margin = new Thickness(0, 10, 0, 0);
            string[] keys = { "seed", "recipes", "guide" };
            string[] names = { "种子地图", "合成查询", "新手引导" };
            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                var b = new FlatButton(names[i]);
                b.SetFontSize(12);
                b.SetPadding(new Thickness(14, 5, 14, 5));
                b.SetCornerRadius(new CornerRadius(6));
                b.Margin = new Thickness(0, 0, 6, 0);
                b.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
                b.Click += delegate { tab = k; UpdateTabs(); BuildBody(); };
                tabs.Children.Add(b);
            }
            head.Children.Add(tabs);
            Children.Add(head);

            bodyHost = new Grid();
            Grid.SetRow(bodyHost, 1);
            Children.Add(bodyHost);

            BuildBody();
            UpdateTabs();
        }

        void UpdateTabs()
        {
            string[] keys = { "seed", "recipes", "guide" };
            for (int i = 0; i < keys.Length && i < tabs.Children.Count; i++)
            {
                var b = tabs.Children[i] as FlatButton;
                if (b == null) continue;
                bool on = keys[i] == tab;
                b.SetColors(on ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(0, 0, 0, 0),
                            Color.FromArgb(26, 255, 255, 255),
                            on ? Palette.Text : Palette.Dim, Palette.Text);
                b.SetBold(on);
            }
        }

        Border Card()
        {
            var b = new Border();
            b.Background = Palette.B(Palette.Panel);
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(14, 12, 14, 14);
            b.Margin = new Thickness(16, 4, 16, 10);
            return b;
        }

        TextBlock Cap(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 11;
            t.Foreground = Palette.B(Palette.Dim);
            return t;
        }

        void BuildBody()
        {
            bodyHost.Children.Clear();
            if (tab == "recipes") bodyHost.Children.Add(BuildRecipes());
            else if (tab == "guide") bodyHost.Children.Add(BuildGuide());
            else bodyHost.Children.Add(BuildSeedMap());
        }

        // ---------- 合成查询 ----------
        UIElement BuildRecipes()
        {
            var host = new Grid();

            var listView = new Grid();
            listView.RowDefinitions.Add(UiHelpers.AutoRow());
            listView.RowDefinitions.Add(UiHelpers.StarRow());
            host.Children.Add(listView);

            var detailView = new Grid();
            detailView.Visibility = Visibility.Collapsed;
            host.Children.Add(detailView);

            var top = Card();
            top.Margin = new Thickness(16, 4, 16, 6);
            var topStack = new StackPanel();
            top.Child = topStack;
            topStack.Children.Add(Cap("输入物品或材料，比如「镐」「铁锭」「红石」；点任意一条看 3×3 合成摆放"));

            var box = new TextBox();
            box.FontSize = 13;
            box.Padding = new Thickness(8, 6, 8, 6);
            box.Margin = new Thickness(0, 6, 0, 0);
            box.Background = Palette.B(Palette.Input);
            box.Foreground = Palette.B(Palette.Text);
            box.CaretBrush = Palette.B(Palette.Text);
            box.BorderBrush = Palette.B(Palette.Line);
            box.BorderThickness = new Thickness(1);
            topStack.Children.Add(box);
            listView.Children.Add(top);

            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var list = new StackPanel();
            list.Margin = new Thickness(16, 0, 16, 14);
            scroll.Content = list;
            Grid.SetRow(scroll, 1);
            listView.Children.Add(scroll);

            Action<Recipe> open = delegate(Recipe r)
            {
                BuildRecipeDetail(detailView, r, delegate
                {
                    detailView.Visibility = Visibility.Collapsed;
                    listView.Visibility = Visibility.Visible;
                });
                listView.Visibility = Visibility.Collapsed;
                detailView.Visibility = Visibility.Visible;
            };

            Action rebuild = null;
            rebuild = delegate
            {
                list.Children.Clear();
                List<Recipe> found = Recipes.Search(box.Text);
                var count = Cap("共 " + found.Count + " 条");
                count.Margin = new Thickness(2, 2, 0, 6);
                list.Children.Add(count);
                int max = Math.Min(found.Count, 80);
                for (int i = 0; i < max; i++) list.Children.Add(RecipeRow(found[i], open));
            };
            box.TextChanged += delegate { rebuild(); };
            rebuild();
            // 调试：HIL_RECIPE_DEMO=铁镐 直接展开第一条详情，便于离屏截图验收
            string demo = Environment.GetEnvironmentVariable("HIL_RECIPE_DEMO");
            if (!string.IsNullOrEmpty(demo))
            {
                List<Recipe> one = Recipes.Search(demo);
                if (one.Count > 0) open(one[0]);
            }
            return host;
        }

        // 物品图标：优先真实贴图；没有平面贴图的方块用程序画的等距方块；再没有就首字占位
        FrameworkElement ItemIcon(string id, double size)
        {
            BitmapSource bmp = null;
            try
            {
                // 方块一律按原版模型等距渲染；真物品（工具/材料/食物）用平面贴图
                bool block = false;
                try { block = BlockIcon.IsBlock(id); } catch { }
                if (block) bmp = BlockIcon.Get(id);
                if (bmp == null) bmp = Items.Get(id);
                if (bmp == null) bmp = Items.Procedural(id);     // 兜底：手绘图标
            }
            catch { }
            if (bmp != null)
            {
                var img = new Image();
                img.Source = bmp;
                img.Width = size;
                img.Height = size;
                img.Stretch = Stretch.Uniform;
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                return img;
            }
            var b = new Border();
            b.Width = size;
            b.Height = size;
            b.CornerRadius = new CornerRadius(4);
            b.Background = Palette.B(Palette.Btn);
            var t = new TextBlock();
            string nm = Items.Name(id);
            t.Text = string.IsNullOrEmpty(nm) ? "?" : nm.Substring(0, 1);
            t.FontSize = Math.Max(9, size * 0.45);
            t.Foreground = Palette.B(Palette.Dim);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            return b;
        }

        Border ItemCell(string id, double size)
        {
            var b = new Border();
            b.Width = size;
            b.Height = size;
            b.CornerRadius = new CornerRadius(6);
            b.Background = Palette.B(Palette.Btn);
            b.BorderThickness = new Thickness(1);
            b.BorderBrush = Palette.B(Palette.Line);
            var inner = new Grid();
            if (!string.IsNullOrEmpty(id))
            {
                var ic = ItemIcon(id, size - 10);
                ic.HorizontalAlignment = HorizontalAlignment.Center;
                ic.VerticalAlignment = VerticalAlignment.Center;
                inner.Children.Add(ic);
                b.ToolTip = Items.Name(id);
            }
            b.Child = inner;
            return b;
        }

        void BuildRecipeDetail(Grid host, Recipe r, Action back)
        {
            host.Children.Clear();
            host.RowDefinitions.Clear();
            host.RowDefinitions.Add(UiHelpers.AutoRow());
            host.RowDefinitions.Add(UiHelpers.StarRow());

            var top = Card();
            top.Margin = new Thickness(16, 4, 16, 6);
            var sp = new StackPanel();
            top.Child = sp;
            var row = new Grid();
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            row.ColumnDefinitions.Add(UiHelpers.StarCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            var btnBack = new FlatButton("← 返回");
            btnBack.SetPadding(new Thickness(10, 4, 10, 4));
            btnBack.Click += delegate { back(); };
            row.Children.Add(btnBack);
            var title = new TextBlock();
            title.Text = r.Line;
            title.FontSize = 15;
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(12, 0, 12, 0);
            title.VerticalAlignment = VerticalAlignment.Center;
            title.Foreground = Palette.B(Palette.Text);
            Grid.SetColumn(title, 1);
            row.Children.Add(title);
            var st = Cap(r.Station);
            st.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(st, 2);
            row.Children.Add(st);
            sp.Children.Add(row);
            host.Children.Add(top);

            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var body = new StackPanel();
            body.Margin = new Thickness(16, 0, 16, 14);
            scroll.Content = body;
            Grid.SetRow(scroll, 1);
            host.Children.Add(scroll);

            // 合成摆法卡
            var gridCard = Card();
            gridCard.Margin = new Thickness(0, 0, 0, 8);
            var wrap = new StackPanel();
            wrap.Orientation = Orientation.Horizontal;
            wrap.HorizontalAlignment = HorizontalAlignment.Center;
            gridCard.Child = wrap;

            const double Cell = 46;
            if (r.Grid != null)
            {
                var g = new Grid();
                for (int i = 0; i < 3; i++)
                {
                    g.RowDefinitions.Add(UiHelpers.AutoRow());
                    g.ColumnDefinitions.Add(UiHelpers.AutoCol());
                }
                for (int i = 0; i < 9; i++)
                {
                    var cell = ItemCell(r.Grid[i], Cell);
                    cell.Margin = new Thickness(3);
                    Grid.SetRow(cell, i / 3);
                    Grid.SetColumn(cell, i % 3);
                    g.Children.Add(cell);
                }
                wrap.Children.Add(g);
            }
            else
            {
                var cell = ItemCell(null, Cell);
                cell.Margin = new Thickness(3);
                wrap.Children.Add(cell);
            }

            var arrow = new TextBlock();
            arrow.Text = "→";
            arrow.FontSize = 22;
            arrow.Margin = new Thickness(18, 0, 18, 0);
            arrow.VerticalAlignment = VerticalAlignment.Center;
            arrow.Foreground = Palette.B(Palette.Dim);
            wrap.Children.Add(arrow);

            var outBox = new StackPanel();
            outBox.VerticalAlignment = VerticalAlignment.Center;
            var oc = ItemCell(r.Out, Cell + 14);
            oc.HorizontalAlignment = HorizontalAlignment.Center;
            outBox.Children.Add(oc);
            var cnt = new TextBlock();
            cnt.Text = r.Count > 1 ? "×" + r.Count : "";
            cnt.FontSize = 11;
            cnt.HorizontalAlignment = HorizontalAlignment.Center;
            cnt.Margin = new Thickness(0, 4, 0, 0);
            cnt.Foreground = Palette.B(Palette.Dim);
            outBox.Children.Add(cnt);
            wrap.Children.Add(outBox);
            body.Children.Add(gridCard);

            // 材料清单
            var matCard = Card();
            matCard.Margin = new Thickness(0, 0, 0, 8);
            var matSp = new StackPanel();
            matCard.Child = matSp;
            var matTitle = new TextBlock();
            matTitle.Text = "材料";
            matTitle.FontSize = 12;
            matTitle.Margin = new Thickness(0, 0, 0, 6);
            matTitle.Foreground = Palette.B(Palette.Dim);
            matSp.Children.Add(matTitle);
            if (r.Grid != null)
            {
                var count = new System.Collections.Generic.Dictionary<string, int>();
                var order = new System.Collections.Generic.List<string>();
                for (int i = 0; i < 9; i++)
                {
                    string id = r.Grid[i];
                    if (string.IsNullOrEmpty(id)) continue;
                    if (!count.ContainsKey(id)) { count[id] = 0; order.Add(id); }
                    count[id] = count[id] + 1;
                }
                for (int i = 0; i < order.Count; i++)
                {
                    string id = order[i];
                    var line = new StackPanel();
                    line.Orientation = Orientation.Horizontal;
                    line.Margin = new Thickness(0, 0, 0, 5);
                    var ic = ItemIcon(id, 20);
                    ic.Margin = new Thickness(0, 0, 8, 0);
                    ic.VerticalAlignment = VerticalAlignment.Center;
                    line.Children.Add(ic);
                    var nm = new TextBlock();
                    nm.Text = Items.Name(id) + "  ×" + count[id];
                    nm.FontSize = 12;
                    nm.VerticalAlignment = VerticalAlignment.Center;
                    nm.Foreground = Palette.B(Palette.Text);
                    line.Children.Add(nm);
                    matSp.Children.Add(line);
                }
            }
            else
            {
                var nm = new TextBlock();
                nm.Text = r.Mats;
                nm.FontSize = 12;
                nm.TextWrapping = TextWrapping.Wrap;
                nm.Foreground = Palette.B(Palette.Text);
                matSp.Children.Add(nm);
            }
            body.Children.Add(matCard);
        }

        UIElement RecipeRow(Recipe r, Action<Recipe> open)
        {
            var row = new Border();
            row.CornerRadius = new CornerRadius(6);
            row.Padding = new Thickness(10, 7, 10, 7);
            row.Margin = new Thickness(0, 0, 0, 4);
            row.Background = Palette.B(Palette.Tile);
            row.Cursor = Cursors.Hand;
            row.Background = Palette.B(Palette.Tile);
            row.MouseEnter += delegate { row.Background = Palette.B(Palette.BtnHover); };
            row.MouseLeave += delegate { row.Background = Palette.B(Palette.Tile); };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            var icon = ItemIcon(r.Out, 22);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            icon.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(icon);

            var name = new TextBlock();
            name.Text = r.Line;
            name.FontSize = 12.5;
            name.FontWeight = FontWeights.SemiBold;
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Foreground = Palette.B(Palette.Text);
            Grid.SetColumn(name, 1);
            g.Children.Add(name);

            var mats = new TextBlock();
            mats.Text = r.Mats;
            mats.FontSize = 11.5;
            mats.TextTrimming = TextTrimming.CharacterEllipsis;
            mats.VerticalAlignment = VerticalAlignment.Center;
            mats.Margin = new Thickness(10, 0, 10, 0);
            mats.Foreground = Palette.B(Palette.Dim);
            Grid.SetColumn(mats, 2);
            g.Children.Add(mats);

            var station = Cap(r.Station);
            station.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(station, 3);
            g.Children.Add(station);

            row.Child = g;
            Recipe target = r;
            row.MouseLeftButtonUp += delegate { open(target); };
            return row;
        }

        // ---------- 新手引导 ----------
        UIElement BuildGuide()
        {
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel();
            stack.Margin = new Thickness(16, 4, 16, 14);
            scroll.Content = stack;

            stack.Children.Add(GuideCard("第一天怎么活下来", new string[]
            {
                "1. 撸树。徒手打 4~6 个原木，做成木板和木棍。",
                "2. 做工作台、木镐，挖 20 个左右的圆石（石头要铁镐以上，圆石木镐就行）。",
                "3. 做石镐、石剑、石斧，顺手打点煤（黑色带斑点的矿石）。",
                "4. 天黑前做火把、挖个 3×3 的小屋或用土把自己围起来。",
                "5. 有羊就杀两只做床，睡一觉直接跳过夜晚。"
            }));

            stack.Children.Add(GuideCard("活过第一夜的关键", new string[]
            {
                "· 亮度：火把要插够，怪物只在亮度 0 的地方刷。",
                "· 门：有门就先放门，僵尸会砸门但不至于立刻破。",
                "· 床：没有床就挖三格深的洞，封顶，蹲一晚。",
                "· 别在洞口站着看，苦力怕会给你惊喜。"
            }));

            stack.Children.Add(GuideCard("常用的几件事", new string[]
            {
                "· 坐标：F3 打开调试屏，X/Z 是平面位置，Y 是高度。",
                "· 传送：/tp x y z；回出生点 /spawnpoint。",
                "· 找要塞：扔末影之眼，跟着它飞的方向走。",
                "· 下界：黑曜石 10 个做门，打火石点火。",
                "· 复活点：睡过的床就是复活点，床被拆了会回到世界出生点。"
            }));

            stack.Children.Add(GuideCard("开服/联机之前", new string[]
            {
                "· 正版服需要正版账号；离线服可以用离线登录。",
                "· 端口没开（TCP）就会连不上，先确认对方端口。",
                "· 版本要一致，模组也要一致，否则会被踢。"
            }));
            return scroll;
        }

        UIElement GuideCard(string head, string[] lines)
        {
            var card = Card();
            card.Margin = new Thickness(0, 0, 0, 8);
            var sp = new StackPanel();
            card.Child = sp;
            var t = new TextBlock();
            t.Text = head;
            t.FontSize = 13;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Palette.B(Palette.Text);
            t.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(t);
            for (int i = 0; i < lines.Length; i++)
            {
                var l = new TextBlock();
                l.Text = lines[i];
                l.FontSize = 11.5;
                l.TextWrapping = TextWrapping.Wrap;
                l.Margin = new Thickness(0, 0, 0, 5);
                l.Foreground = Palette.B(Palette.Dim);
                sp.Children.Add(l);
            }
            return card;
        }

        // ---------- 种子地图（cubiomes 本地引擎） ----------
        SeedMapView mapView;
        TextBox seedBox;

        UIElement BuildSeedMap()
        {
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());

            var bar = Card();
            bar.Margin = new Thickness(16, 4, 16, 6);
            var sp = new StackPanel();
            sp.Orientation = Orientation.Horizontal;
            bar.Child = sp;
            var lbl = Cap("种子");
            lbl.VerticalAlignment = VerticalAlignment.Center;
            lbl.Margin = new Thickness(0, 0, 8, 0);
            sp.Children.Add(lbl);

            seedBox = new TextBox();
            seedBox.Text = "12345";
            seedBox.Width = 200;
            seedBox.FontSize = 12;
            seedBox.Padding = new Thickness(8, 5, 8, 5);
            seedBox.VerticalAlignment = VerticalAlignment.Center;
            seedBox.Background = Palette.B(Palette.Input);
            seedBox.Foreground = Palette.B(Palette.Text);
            seedBox.CaretBrush = Palette.B(Palette.Text);
            seedBox.BorderBrush = Palette.B(Palette.Line);
            seedBox.BorderThickness = new Thickness(1);
            sp.Children.Add(seedBox);

            var go = new FlatButton("生成");
            go.SetFontSize(12);
            go.SetPadding(new Thickness(14, 5, 14, 5));
            go.SetCornerRadius(new CornerRadius(6));
            go.Margin = new Thickness(8, 0, 0, 0);
            go.Click += delegate
            {
                ulong s;
                if (!ulong.TryParse((seedBox.Text ?? "").Trim(), out s))
                {
                    // 非数字种子：MC 用字符串哈希，这里退化成简单的 FNV
                    s = 0;
                    string t = (seedBox.Text ?? "").Trim();
                    for (int i = 0; i < t.Length; i++) s = s * 131 + t[i];
                }
                if (mapView != null) mapView.SetSeed(s);
                else
                {
                    mapView = new SeedMapView(s, 0, 0);
                    Grid.SetRow(mapView, 1);
                    g.Children.Add(mapView);
                    mapView.Render();
                }
            };
            sp.Children.Add(go);

            // 从存档读种子
            Border savePop = null;
            Action closePop = delegate
            {
                if (savePop != null)
                {
                    g.Children.Remove(savePop);
                    savePop = null;
                }
            };
            var fromSave = new FlatButton("从存档读取");
            fromSave.SetFontSize(12);
            fromSave.SetPadding(new Thickness(12, 5, 12, 5));
            fromSave.SetCornerRadius(new CornerRadius(6));
            fromSave.Margin = new Thickness(8, 0, 0, 0);
            fromSave.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            fromSave.Click += delegate
            {
                closePop();
                var saves = Nbt.ListSaves();
                if (saves.Count == 0)
                {
                    Toast("没找到存档（.minecraft/saves 里没有 level.dat）");
                    return;
                }
                var card = new Border();
                card.Background = Palette.B(Palette.Panel);
                card.BorderBrush = Palette.B(Palette.Line);
                card.BorderThickness = new Thickness(1);
                card.CornerRadius = new CornerRadius(8);
                card.Padding = new Thickness(10);
                card.HorizontalAlignment = HorizontalAlignment.Right;
                card.VerticalAlignment = VerticalAlignment.Top;
                card.Margin = new Thickness(0, 8, 24, 0);
                card.Effect = new System.Windows.Media.Effects.DropShadowEffect
                { BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35 };
                var list = new StackPanel();
                card.Child = list;
                var cap = Cap("选一个存档");
                cap.Margin = new Thickness(2, 0, 0, 6);
                list.Children.Add(cap);
                for (int i = 0; i < saves.Count && i < 12; i++)
                {
                    string name = saves[i][0];
                    string path = saves[i][1];
                    var b = new FlatButton(name);
                    b.SetFontSize(12);
                    b.SetPadding(new Thickness(10, 4, 10, 4));
                    b.SetCornerRadius(new CornerRadius(6));
                    b.Margin = new Thickness(0, 0, 0, 3);
                    b.HorizontalAlignment = HorizontalAlignment.Stretch;
                    b.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Text, Palette.Text);
                    b.Click += delegate
                    {
                        long? sd = Nbt.ReadWorldSeed(path);
                        closePop();
                        if (!sd.HasValue) { Toast("这个存档读不出种子：" + name); return; }
                        seedBox.Text = sd.Value.ToString();
                        ulong us = unchecked((ulong)sd.Value);
                        if (mapView == null)
                        {
                            mapView = new SeedMapView(us, 0, 0);
                            Grid.SetRow(mapView, 1);
                            g.Children.Add(mapView);
                            mapView.Render();
                        }
                        else mapView.SetSeed(us);
                        Toast("已读取种子：" + sd.Value);
                    };
                    list.Children.Add(b);
                }
                savePop = card;
                Grid.SetRow(card, 1);
                g.Children.Add(card);
            };
            sp.Children.Add(fromSave);

            var hint = Cap("滚轮缩放 · 拖动平移");
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(12, 0, 0, 0);
            sp.Children.Add(hint);
            g.Children.Add(bar);

            var mapCard = new Border();
            mapCard.Margin = new Thickness(16, 0, 16, 12);
            mapCard.CornerRadius = new CornerRadius(8);
            mapCard.Background = Palette.B(Palette.Panel);
            mapCard.ClipToBounds = true;
            var waiting = Cap("输入种子后点「生成」。引擎会在本地算群系，不联网。");
            waiting.Margin = new Thickness(14);
            mapCard.Child = waiting;
            Grid.SetRow(mapCard, 1);
            g.Children.Add(mapCard);

            // 先放一个默认视图，省得空白
            mapView = new SeedMapView(12345, 0, 0);
            mapCard.Child = mapView;
            Loaded += delegate { if (mapView != null) mapView.Render(); };
            return g;
        }
    }
}
