// HugeLauncher.Ui.Pages - 更多·找乐子：和首页同一套小部件引擎（可拖动/缩放/增删）
// 导航 key: more:fun
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    class PageFun : PageBase
    {
        Dashboard dash;
        FlatButton editBtn;
        FlatButton addBtn;
        Border picker;
        Grid root;
        string filter = "全部";
        System.Windows.Threading.DispatcherTimer dayTimer;

        public PageFun()
        {
            try { Ideas.Load(); } catch { }
            Margin = new Thickness(16, 12, 16, 12);
            RowDefinitions.Add(UiHelpers.AutoRow());
            RowDefinitions.Add(UiHelpers.StarRow());

            root = new Grid();
            Grid.SetRow(root, 1);
            Children.Add(root);

            // 顶栏
            var head = new Grid();
            var title = new TextBlock();
            title.Text = "找乐子";
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

            // 小部件盘（可滚动：行数按「所有部件都拉到最高、排成一列」预留）
            dash = new Dashboard(Path.Combine(Paths.LauncherDir, "fun.dat"));
            RegisterWidgets();
            dash.DefaultSetup = delegate
            {
                dash.AddDefaultSlot("idea", 0, 0, 5, 3);
                dash.AddDefaultSlot("library", 5, 0, 7, 3);
                dash.AddDefaultSlot("stats", 0, 3, 6, 2);
                dash.AddDefaultSlot("done", 6, 3, 6, 2);
            };
            dash.EnableTall(78);
            dash.Load();

            // 跨天刷新：已完成的点子按天算，0 点后重载界面（挂机过夜也能自动清空）
            dayTimer = new System.Windows.Threading.DispatcherTimer();
            dayTimer.Interval = TimeSpan.FromSeconds(30);
            dayTimer.Tick += delegate
            {
                bool wasEditing = editBtn.Text == "完成";
                if (Ideas.CheckDaily())
                {
                    dash.Load();
                    if (wasEditing) dash.BeginEdit(true);
                }
            };
            Loaded += delegate { if (!dayTimer.IsEnabled) dayTimer.Start(); };
            Unloaded += delegate { dayTimer.Stop(); };

            var scroll = new ScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.PanningMode = PanningMode.VerticalOnly;
            scroll.CanContentScroll = false;
            scroll.Content = dash;
            // 滚轮：累积目标 + 短补间。关键是「目标要累加」——每格都从当前位置重新起补间的话，
            // 连续滚时前一段没走完就被覆盖，实际每格只挪十几像素，手感又慢又黏。
            double wheelTarget = 0;
            scroll.PreviewMouseWheel += delegate(object s2, System.Windows.Input.MouseWheelEventArgs e)
            {
                e.Handled = true;
                double cur = scroll.VerticalOffset;
                if (Math.Abs(wheelTarget - cur) > 240) wheelTarget = cur;   // 用户拖动过滚动条/换页，重置
                wheelTarget = Math.Max(0, Math.Min(scroll.ScrollableHeight, wheelTarget + (e.Delta > 0 ? -56.0 : 56.0)));
                double from = cur, to = wheelTarget;
                if (Math.Abs(to - from) < 0.5) return;
                Ani.Start("funScroll", Ani.AaProgress(delegate(double t)
                {
                    scroll.ScrollToVerticalOffset(from + (to - from) * t);
                }, 140, new AniEaseOutFluent(Ani.Weak)));
            };
            root.Children.Add(scroll);
            dash.Scroller = scroll;

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
            picker.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35 };
            root.Children.Add(picker);
        }

        void ToggleEdit()
        {
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
                b.Click += delegate { dash.Add(key); picker.Visibility = Visibility.Collapsed; };
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

        // ---------- 小部件 ----------
        void RegisterWidgets()
        {
            var d1 = new WidgetDef();
            d1.Key = "idea"; d1.Title = "今日玩什么"; d1.W = 5; d1.H = 3; d1.MinW = 3; d1.MinH = 2;
            d1.Build = delegate { return BuildIdea(); };
            dash.Register(d1);

            var d2 = new WidgetDef();
            d2.Key = "library"; d2.Title = "点子库"; d2.W = 7; d2.H = 3; d2.MinW = 4; d2.MinH = 2;
            d2.Build = delegate { return BuildLibrary(); };
            dash.Register(d2);

            var d3 = new WidgetDef();
            d3.Key = "stats"; d3.Title = "游玩统计"; d3.W = 12; d3.H = 2; d3.MinW = 4; d3.MinH = 1;
            d3.Build = delegate { return BuildStats(); };
            dash.Register(d3);

            var d4 = new WidgetDef();
            d4.Key = "done"; d4.Title = "已完成的"; d4.W = 6; d4.H = 3; d4.MinW = 3; d4.MinH = 2;
            d4.Build = delegate { return BuildDone(); };
            dash.Register(d4);
        }

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

        UIElement BuildIdea()
        {
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());

            Idea cur = Ideas.Current();
            var idea = new TextBlock();
            idea.Text = cur != null ? cur.Text : "词库空了";
            idea.FontSize = 15;
            idea.TextWrapping = TextWrapping.Wrap;
            idea.Foreground = Palette.B(Palette.Text);
            g.Children.Add(idea);

            var tag = Cap(cur != null ? (cur.Tag + " · " + cur.Level + " · 已完成 " + Ideas.DoneCount + " 条") : "");
            Grid.SetRow(tag, 1);
            tag.VerticalAlignment = VerticalAlignment.Top;
            tag.Margin = new Thickness(0, 4, 0, 0);
            g.Children.Add(tag);

            var btns = new StackPanel();
            btns.Orientation = Orientation.Horizontal;
            Grid.SetRow(btns, 2);
            bool done = Ideas.IsDone(cur);
            var doneBtn = new FlatButton(done ? "已完成 ✓" : "完成了");
            doneBtn.SetFontSize(11);
            doneBtn.SetPadding(new Thickness(10, 4, 10, 4));
            doneBtn.SetCornerRadius(new CornerRadius(6));
            doneBtn.SetColors(done ? ThemeManager.CurrentColor : Color.FromArgb(0, 0, 0, 0),
                              done ? Palette.Lighten(ThemeManager.CurrentColor, 0.12) : Color.FromArgb(26, 255, 255, 255),
                              done ? Palette.Dark : Palette.Dim, Palette.Text);
            doneBtn.Click += delegate
            {
                Idea i = Ideas.Current();
                if (i == null) return;
                if (Ideas.IsDone(i)) Ideas.UnmarkDone(i); else Ideas.MarkDone(i);
                Reload();
            };
            btns.Children.Add(doneBtn);

            var next = new FlatButton("换一条");
            next.SetFontSize(11);
            next.SetPadding(new Thickness(10, 4, 10, 4));
            next.SetCornerRadius(new CornerRadius(6));
            next.Margin = new Thickness(8, 0, 0, 0);
            next.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            next.Click += delegate { Ideas.Next(); Reload(); };
            btns.Children.Add(next);

            var fav = new FlatButton(Ideas.IsFav(cur) ? "★" : "☆");
            fav.SetFontSize(12);
            fav.SetPadding(new Thickness(9, 3, 9, 3));
            fav.SetCornerRadius(new CornerRadius(6));
            fav.Margin = new Thickness(8, 0, 0, 0);
            fav.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            fav.Click += delegate { Ideas.ToggleFav(Ideas.Current()); Reload(); };
            btns.Children.Add(fav);
            g.Children.Add(btns);
            return Wrap("今日玩什么", g);
        }

        UIElement BuildLibrary()
        {
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());

            var chips = new StackPanel();
            chips.Orientation = Orientation.Horizontal;
            string[] tags = { "全部", "建筑", "探索", "红石", "生存", "整活" };
            for (int i = 0; i < tags.Length; i++)
            {
                string tag = tags[i];
                bool on = tag == filter;
                var b = new FlatButton(tag);
                b.SetFontSize(10.5);
                b.HoverScale = 1.006;   // 分类标签只要轻微反馈
                b.SetPadding(new Thickness(8, 3, 8, 3));
                b.SetCornerRadius(new CornerRadius(5));
                b.Margin = new Thickness(0, 0, 5, 0);
                b.SetColors(on ? Color.FromArgb(24, 255, 255, 255) : Color.FromArgb(0, 0, 0, 0),
                            Color.FromArgb(26, 255, 255, 255), on ? Palette.Text : Palette.Dim, Palette.Text);
                b.Click += delegate { filter = tag; Reload(); };
                chips.Children.Add(b);
            }
            g.Children.Add(chips);

            var list = new StackPanel();
            Grid.SetRow(list, 1);
            g.Children.Add(list);
            int shown = 0;
            for (int i = 0; i < Ideas.All.Count && shown < 5; i++)
            {
                Idea it = Ideas.All[i];
                if (filter != "全部" && it.Tag != filter) continue;
                list.Children.Add(IdeaRow(it));
                shown++;
            }
            if (shown == 0) list.Children.Add(Cap("这一类还没有条目"));
            return Wrap("点子库", g);
        }

        UIElement IdeaRow(Idea it)
        {
            var row = new Grid();
            row.Margin = new Thickness(0, 0, 0, 4);
            row.ColumnDefinitions.Add(UiHelpers.StarCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());

            var left = new StackPanel();
            var t = new TextBlock();
            t.Text = it.Text;
            t.FontSize = 12;
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            t.Foreground = Palette.B(Ideas.IsDone(it) ? Palette.Dim : Palette.Text);
            left.Children.Add(t);
            left.Children.Add(Cap(it.Tag + " · " + it.Level));
            row.Children.Add(left);

            var star = new FlatButton(Ideas.IsFav(it) ? "★" : "☆");
            star.SetFontSize(12);
            star.HoverScale = 1.01;
            star.SetPadding(new Thickness(7, 2, 7, 2));
            star.SetCornerRadius(new CornerRadius(5));
            star.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            star.Click += delegate { Ideas.ToggleFav(it); Reload(); };
            Grid.SetColumn(star, 1);
            row.Children.Add(star);
            return row;
        }

        UIElement BuildStats()
        {
            return Wrap("游玩统计", PlayStatsWidget.Build());
        }

        UIElement BuildDone()
        {
            var g = new Grid();
            var list = new StackPanel();
            g.Children.Add(list);
            int shown = 0;
            for (int i = Ideas.All.Count - 1; i >= 0 && shown < 8; i--)
            {
                Idea it = Ideas.All[i];
                if (!Ideas.IsDone(it)) continue;
                var t = new TextBlock();
                t.Text = "✓ " + it.Text;
                t.FontSize = 12;
                t.TextTrimming = TextTrimming.CharacterEllipsis;
                t.Foreground = Palette.B(Palette.Dim);
                t.Margin = new Thickness(0, 0, 0, 4);
                list.Children.Add(t);
                shown++;
            }
            if (shown == 0) list.Children.Add(Cap("还没完成过，去点子库挑一条吧"));
            return Wrap("已完成的", g);
        }

        void Reload()
        {
            if (dash != null) dash.Load();
        }

        public override void OnEnter()
        {
            Reload();
        }
    }
}
