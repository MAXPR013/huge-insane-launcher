// HugeLauncher.Ui.Pages - 首页：小部件仪表盘（默认落地页，整页不滚动）
// 导航 key: home:dashboard
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    class PageHome : PageBase
    {
        Dashboard dash;
        FlatButton editBtn;
        FlatButton addBtn;
        Border picker;
        Grid root;
        int newsOffset;   // 「换一批」的游标

        public PageHome()
        {
            Margin = new Thickness(16, 12, 16, 12);
            RowDefinitions.Add(UiHelpers.AutoRow());
            RowDefinitions.Add(UiHelpers.StarRow());

            root = new Grid();
            Grid.SetRow(root, 1);
            Children.Add(root);

            // ---- 顶栏 ----
            var head = new Grid();
            var title = new TextBlock();
            title.Text = "首页";
            title.FontSize = 17;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Palette.B(Palette.Text);
            title.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(title);

            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.HorizontalAlignment = HorizontalAlignment.Right;
            right.VerticalAlignment = VerticalAlignment.Center;

            addBtn = new FlatButton("＋ 添加小部件");
            addBtn.SetFontSize(12);
            addBtn.SetPadding(new Thickness(10, 4, 10, 4));
            addBtn.SetCornerRadius(new CornerRadius(7));
            addBtn.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(30, 255, 255, 255), Palette.Dim, Palette.Text);
            addBtn.Visibility = Visibility.Collapsed;
            addBtn.Click += delegate { ShowPicker(); };
            right.Children.Add(addBtn);

            editBtn = new FlatButton("✎ 编辑");
            editBtn.SetFontSize(12);
            editBtn.SetPadding(new Thickness(10, 4, 10, 4));
            editBtn.SetCornerRadius(new CornerRadius(7));
            editBtn.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(30, 255, 255, 255), Palette.Dim, Palette.Text);
            editBtn.Margin = new Thickness(8, 0, 0, 0);
            editBtn.Click += delegate { ToggleEdit(); };
            right.Children.Add(editBtn);

            head.Children.Add(right);
            Children.Add(head);

            // ---- 仪表盘 ----
            dash = new Dashboard(Path.Combine(Paths.LauncherDir, "dashboard.dat"));
            RegisterWidgets();
            try { ContentHub.EnsureFallback(); } catch { }   // 先有兜底内容，再建卡片
            dash.Load();
            root.Children.Add(dash);
            if (Environment.GetEnvironmentVariable("HIL_DASH_DEBUG") == "1")
            {
                var dbg = new TextBlock();
                dbg.FontSize = 10;
                dbg.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x40, 0x40));
                dbg.Background = new SolidColorBrush(Color.FromArgb(200, 0, 0, 0));
                dbg.HorizontalAlignment = HorizontalAlignment.Right;
                dbg.VerticalAlignment = VerticalAlignment.Bottom;
                dbg.IsHitTestVisible = false;
                root.Children.Add(dbg);
                Loaded += delegate { dbg.Text = dash.DebugDump(); };
            }
            try
            {
                ContentHub.Init();
                ContentHub.Updated += delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate { RefreshContent(); }));
                };
            }
            catch { }

            // 添加小部件浮层（默认隐藏）
            picker = new Border();
            picker.Background = Palette.B(Palette.Panel);
            picker.BorderBrush = Palette.B(Palette.Line);
            picker.BorderThickness = new Thickness(1);
            picker.CornerRadius = new CornerRadius(10);
            picker.Padding = new Thickness(10);
            picker.HorizontalAlignment = HorizontalAlignment.Right;
            picker.VerticalAlignment = VerticalAlignment.Top;
            picker.Margin = new Thickness(0, 34, 0, 0);
            picker.Visibility = Visibility.Collapsed;
            picker.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 2,
                Opacity = 0.35
            };
            root.Children.Add(picker);
        }

        // 内容更新后重建卡片（布局不变，沿用 dashboard.dat）
        void RefreshContent()
        {
            if (dash == null) return;
            try { dash.Load(); } catch { }
        }

        void ToggleEdit()        {
            bool on = editBtn.Text != "完成";
            editBtn.Text = on ? "完成" : "✎ 编辑";
            addBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (!on) picker.Visibility = Visibility.Collapsed;
            dash.BeginEdit(on);
        }

        void ShowPicker()
        {
            if (picker.Visibility == Visibility.Visible) { picker.Visibility = Visibility.Collapsed; return; }
            var stack = new StackPanel();
            var t = new TextBlock();
            t.Text = "添加小部件";
            t.FontSize = 12;
            t.Foreground = Palette.B(Palette.Dim);
            t.Margin = new Thickness(2, 0, 0, 6);
            stack.Children.Add(t);
            List<WidgetDef> avail = dash.Available();
            for (int i = 0; i < avail.Count; i++)
            {
                string key = avail[i].Key;
                var b = new FlatButton(avail[i].Title);
                b.SetFontSize(12);
                b.SetPadding(new Thickness(10, 5, 10, 5));
                b.SetCornerRadius(new CornerRadius(6));
                b.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Text, Palette.Text);
                b.Margin = new Thickness(0, 0, 0, 4);
                b.HorizontalAlignment = HorizontalAlignment.Stretch;
                b.Click += delegate
                {
                    dash.Add(key);
                    picker.Visibility = Visibility.Collapsed;
                };
                stack.Children.Add(b);
            }
            if (avail.Count == 0)
            {
                var none = new TextBlock();
                none.Text = "都放上了";
                none.FontSize = 11;
                none.Foreground = Palette.B(Palette.Dim);
                stack.Children.Add(none);
            }
            picker.Child = stack;
            picker.Visibility = Visibility.Visible;
        }

        // ===== 小部件注册 =====
        void RegisterWidgets()
        {
            var d1 = new WidgetDef();
            d1.Key = "checkin"; d1.Title = "签到"; d1.W = 4; d1.H = 3; d1.MinW = 3; d1.MinH = 2;
            d1.Build = delegate { return BuildCheckin(); };
            dash.Register(d1);

            var d2 = new WidgetDef();
            d2.Key = "idea"; d2.Title = "今日玩什么"; d2.W = 4; d2.H = 3; d2.MinW = 3; d2.MinH = 2;
            d2.Build = delegate { return BuildIdea(); };
            dash.Register(d2);

            var d3 = new WidgetDef();
            d3.Key = "servers"; d3.Title = "服务器动态"; d3.W = 4; d3.H = 3; d3.MinW = 3; d3.MinH = 2;
            d3.Build = delegate { return BuildServers(); };
            dash.Register(d3);

            var d4 = new WidgetDef();
            d4.Key = "news"; d4.Title = "MC 新闻"; d4.W = 12; d4.H = 2; d4.MinW = 4; d4.MinH = 2;
            d4.Build = delegate { return BuildNews(); };
            dash.Register(d4);

            var d5 = new WidgetDef();
            d5.Key = "notice"; d5.Title = "HIL 公告"; d5.W = 12; d5.H = 1; d5.MinW = 4; d5.MinH = 1;
            d5.Build = delegate { return BuildNotice(); };
            dash.Register(d5);

            var d6 = new WidgetDef();
            d6.Key = "stats"; d6.Title = "游玩统计"; d6.W = 4; d6.H = 2; d6.MinW = 3; d6.MinH = 2;
            d6.Build = delegate { return BuildStats(); };
            dash.Register(d6);
        }

        // ===== 各小部件内容 =====
        static TextBlock Cap(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 11;
            t.Foreground = Palette.B(Palette.Dim);
            return t;
        }

        UIElement Wrap(string title, UIElement body)
        {
            var g = new Grid();
            g.Margin = new Thickness(12, 8, 12, 6);
            var head = new TextBlock();
            head.Text = title;
            head.FontSize = 11;
            head.Foreground = Palette.B(Palette.Dim);
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());
            g.Children.Add(head);
            Grid.SetRow(body, 1);
            var fe = body as FrameworkElement;
            if (fe != null) fe.Margin = new Thickness(0, 4, 0, 0);
            g.Children.Add(body);
            return g;
        }

        UIElement BuildCheckin()
        {
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());

            var month = new TextBlock();
            DateTime now = DateTime.Now;
            month.Text = now.Year + " 年 " + now.Month + " 月";
            month.FontSize = 12;
            month.Foreground = Palette.B(Palette.Text);
            g.Children.Add(month);

            var grid = new UniformGrid();
            grid.Columns = 7;
            grid.Margin = new Thickness(0, 8, 0, 0);
            string[] wd = { "一", "二", "三", "四", "五", "六", "日" };
            for (int i = 0; i < 7; i++)
            {
                var c = Cap(wd[i]);
                c.HorizontalAlignment = HorizontalAlignment.Center;
                c.FontSize = 10;
                grid.Children.Add(c);
            }
            DateTime first = new DateTime(now.Year, now.Month, 1);
            int lead = ((int)first.DayOfWeek + 6) % 7;
            for (int i = 0; i < lead; i++) grid.Children.Add(new TextBlock());
            int days = DateTime.DaysInMonth(now.Year, now.Month);
            for (int d = 1; d <= days; d++)
            {
                var t = new TextBlock();
                t.Text = d.ToString();
                t.FontSize = 10;
                t.HorizontalAlignment = HorizontalAlignment.Center;
                t.VerticalAlignment = VerticalAlignment.Center;
                bool today = d == now.Day;
                t.Foreground = Palette.B(today ? ThemeManager.CurrentColor : Palette.Text);
                if (today) t.FontWeight = FontWeights.Bold;
                grid.Children.Add(t);
            }
            Grid.SetRow(grid, 1);
            g.Children.Add(grid);

            var line = Cap("● 连续签到 6 天 · 本月 12 天");
            line.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetRow(line, 2);
            g.Children.Add(line);
            return Wrap("签到", g);
        }

        UIElement BuildIdea()
        {
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());

            var idea = new TextBlock();
            Idea cur = null;
            try { cur = Ideas.Current(); } catch { }
            idea.Text = cur != null ? cur.Text : "用草方块搭一栋房子";
            idea.FontSize = 15;
            idea.TextWrapping = TextWrapping.Wrap;
            idea.Foreground = Palette.B(Palette.Text);
            g.Children.Add(idea);

            var tag = Cap(cur != null ? (cur.Tag + " · " + cur.Level) : "建筑 · 中等难度");
            tag.Margin = new Thickness(0, 4, 0, 0);
            Grid.SetRow(tag, 1);
            tag.VerticalAlignment = VerticalAlignment.Top;
            g.Children.Add(tag);

            var btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            Grid.SetRow(btns, 2);
            var done = new FlatButton("完成了");
            done.SetFontSize(11);
            done.SetPadding(new Thickness(10, 4, 10, 4));
            done.SetCornerRadius(new CornerRadius(6));
            done.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.12), Palette.Dark, Palette.Dark);
            done.Click += delegate
            {
                Idea nowIdea = Ideas.Current();
                if (nowIdea != null) Ideas.MarkDone(nowIdea);
                RefreshContent();
            };
            btns.Children.Add(done);
            var next = new FlatButton("换一条");
            next.SetFontSize(11);
            next.SetPadding(new Thickness(10, 4, 10, 4));
            next.SetCornerRadius(new CornerRadius(6));
            next.Margin = new Thickness(8, 0, 0, 0);
            next.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            next.Click += delegate
            {
                Ideas.Next();
                RefreshContent();
            };
            btns.Children.Add(next);
            g.Children.Add(btns);
            return Wrap("今日玩什么", g);
        }

        UIElement BuildServers()
        {
            var stack = new StackPanel();
            var recent = Cfg != null ? Cfg.RecentServers : null;
            if (recent == null || recent.Count == 0)
            {
                var none = Cap("暂无最近服务器");
                none.Margin = new Thickness(0, 2, 0, 0);
                stack.Children.Add(none);
                var hint = Cap("在「联机 → 联机大厅」复制地址后会记在这里");
                hint.Margin = new Thickness(0, 4, 0, 0);
                hint.TextWrapping = TextWrapping.Wrap;   // 卡片窄，不能按单行裁掉
                stack.Children.Add(hint);
                return Wrap("服务器动态", stack);
            }

            int n = Math.Min(3, recent.Count);
            for (int i = 0; i < n; i++)
            {
                string addr = recent[i];
                var r = new Grid();
                r.Margin = new Thickness(0, i == 0 ? 0 : 6, 0, 0);
                r.ColumnDefinitions.Add(UiHelpers.StarCol());
                r.ColumnDefinitions.Add(UiHelpers.AutoCol());
                var name = new TextBlock();
                name.Text = addr;
                name.FontSize = 12;
                name.TextTrimming = TextTrimming.CharacterEllipsis;
                name.Foreground = Palette.B(Palette.Text);
                r.Children.Add(name);

                var meta = new TextBlock();
                meta.Text = "探测中…";
                meta.FontSize = 11;
                meta.VerticalAlignment = VerticalAlignment.Center;
                meta.Margin = new Thickness(8, 0, 0, 0);
                meta.Foreground = Palette.B(Palette.Dim);
                Grid.SetColumn(meta, 1);
                r.Children.Add(meta);
                stack.Children.Add(r);

                TextBlock target = meta;
                try
                {
                    ServerPing.PingAsync(addr, delegate(ServerPingResult res)
                    {
                        target.Dispatcher.BeginInvoke(new Action(delegate
                        {
                            if (res != null && res.Ok)
                            {
                                string cnt = res.Max > 0 ? (res.Online + "/" + res.Max) : (res.Online + " 人在线");
                                target.Text = cnt + "   " + res.LatencyMs + "ms";
                                target.Foreground = Palette.B(Palette.Green);
                            }
                            else
                            {
                                target.Text = "离线";
                                target.Foreground = Palette.B(Palette.Dim);
                            }
                        }));
                    });
                }
                catch
                {
                    meta.Text = "未知";
                }
            }
            return Wrap("服务器动态", stack);
        }

        UIElement BuildNews()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            var list = new StackPanel();
            List<NewsItem> all = ContentHub.News;
            if (all.Count == 0) { ContentHub.EnsureFallback(); all = ContentHub.News; }
            int start = newsOffset % Math.Max(1, all.Count);
            for (int i = 0; i < 3 && all.Count > 0; i++)
            {
                NewsItem it = all[(start + i) % all.Count];
                var row = new Grid();
                row.Margin = new Thickness(0, i == 0 ? 0 : 3, 0, 0);
                row.ColumnDefinitions.Add(UiHelpers.AutoCol());
                row.ColumnDefinitions.Add(UiHelpers.StarCol());
                row.ColumnDefinitions.Add(UiHelpers.AutoCol());

                var cover = new Border();
                cover.Width = 30; cover.Height = 17;
                cover.CornerRadius = new CornerRadius(4);
                cover.Background = new SolidColorBrush(Color.FromArgb(26, 0, 0, 0));
                cover.Margin = new Thickness(0, 0, 8, 0);
                var ph = new TextBlock();
                ph.Text = "图";
                ph.FontSize = 9;
                ph.Opacity = 0.5;
                ph.HorizontalAlignment = HorizontalAlignment.Center;
                ph.VerticalAlignment = VerticalAlignment.Center;
                ph.Foreground = Palette.B(Palette.Dim);
                cover.Child = ph;
                if (!string.IsNullOrEmpty(it.Cover))
                {
                    try
                    {
                        var bi = new System.Windows.Media.Imaging.BitmapImage();
                        bi.BeginInit();
                        bi.UriSource = new Uri(it.Cover);
                        bi.DecodePixelWidth = 80;
                        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bi.EndInit();
                        var im = new Image();
                        im.Source = bi;
                        im.Stretch = Stretch.UniformToFill;
                        cover.Child = im;
                        cover.ClipToBounds = true;
                    }
                    catch { }
                }
                row.Children.Add(cover);

                var col = new StackPanel();
                Grid.SetColumn(col, 1);
                var t = new TextBlock();
                t.Text = it.Title;
                t.FontSize = 12;
                t.Foreground = Palette.B(Palette.Text);
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                col.Children.Add(t);
                var sub = Cap(it.Sub);
                sub.TextTrimming = TextTrimming.CharacterEllipsis;
                col.Children.Add(sub);
                row.Children.Add(col);

                var date = Cap(it.Date);
                Grid.SetColumn(date, 2);
                date.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(date);

                string href = it.Url;
                if (!string.IsNullOrEmpty(href))
                {
                    row.Cursor = System.Windows.Input.Cursors.Hand;
                    row.MouseLeftButtonUp += delegate { try { System.Diagnostics.Process.Start(href); } catch { } };
                }
                list.Children.Add(row);
            }
            g.Children.Add(list);

            var swap = new FlatButton("换一批");
            swap.SetFontSize(11);
            swap.SetPadding(new Thickness(10, 4, 10, 4));
            swap.SetCornerRadius(new CornerRadius(6));
            swap.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            swap.VerticalAlignment = VerticalAlignment.Top;
            swap.Margin = new Thickness(10, 0, 0, 0);
            swap.Click += delegate
            {
                int total = ContentHub.News.Count;
                newsOffset += total > 3 ? 3 : 1;
                RefreshContent();
            };
            Grid.SetColumn(swap, 1);
            g.Children.Add(swap);
            return Wrap("MC 新闻", g);
        }

        UIElement BuildNotice()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            string noticeText = "26.12 更新预告：首页小部件、种子地图内测、账号页重做";
            string noticeDate = "2 天前";
            if (ContentHub.Notices.Count > 0)
            {
                noticeText = ContentHub.Notices[0].Title;
                noticeDate = ContentHub.Notices[0].Date;
            }
            var t = new TextBlock();
            t.Text = noticeText;
            t.FontSize = 12;
            t.Foreground = Palette.B(Palette.Text);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            g.Children.Add(t);
            var d = Cap(noticeDate);
            Grid.SetColumn(d, 1);
            d.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(d);
            return Wrap("HIL 公告", g);
        }

        UIElement BuildStats()
        {
            // 近 7 天条形图 + 右下角「清空本周」（自绘，清除后就地刷新）
            return Wrap("游玩统计", PlayStatsWidget.Build());
        }

        public override void OnEnter()
        {
            // 跨天：已完成的点子按天刷新，重载仪表盘让「今天玩什么」也换成新的一天
            try { if (Ideas.CheckDaily() && dash != null) dash.Load(); } catch { }
            if (dash != null && editBtn != null) dash.BeginEdit(editBtn.Text == "完成");
        }
    }
}
