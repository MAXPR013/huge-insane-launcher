// HugeLauncher.Ui - 首次启动新手引导（覆盖在主界面上，不另开窗口）
// 五步：主题（真切）/ 游戏在哪（扫描+迁移）/ 装个游戏 / 登录 / 完成
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using HugeLauncher;
using HugeLauncher.Ui.Pages;

namespace HugeLauncher.Ui
{
    static class Onboarding
    {
        public const int Steps = 7;

        // go(step) 切步；finish() 标记完成；openMigrate()/openDownload() 跳到对应页
        public static UIElement Build(Config cfg, int step, Action<int> go, Action finish,
                                      Action openMigrate, Action openDownload)
        {
            var layer = new Grid();
            var scrim = new Border();
            scrim.Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0));
            layer.Children.Add(scrim);

            var card = new Border();
            card.Width = 480;
            card.CornerRadius = new CornerRadius(14);
            card.Background = Palette.B(Palette.Light
                ? Color.FromArgb(252, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(252, 0x1C, 0x1F, 0x27));
            card.BorderThickness = new Thickness(1);
            card.BorderBrush = Palette.B(Palette.Line);
            card.Padding = new Thickness(26, 22, 26, 18);
            card.HorizontalAlignment = HorizontalAlignment.Center;
            card.VerticalAlignment = VerticalAlignment.Center;
            card.Effect = new System.Windows.Media.Effects.DropShadowEffect
            { BlurRadius = 28, ShadowDepth = 6, Opacity = 0.35 };
            layer.Children.Add(card);

            var root = new Grid();
            root.RowDefinitions.Add(UiHelpers.AutoRow());   // 步骤点
            root.RowDefinitions.Add(UiHelpers.AutoRow());   // 内容
            root.RowDefinitions.Add(UiHelpers.AutoRow());   // 按钮
            card.Child = root;

            // 步骤指示：短横条，当前拉长
            var dots = new StackPanel();
            dots.Orientation = Orientation.Horizontal;
            dots.Margin = new Thickness(0, 0, 0, 14);
            for (int i = 0; i < Steps; i++)
            {
                var d = new Border();
                d.Height = 4;
                d.Width = i == step ? 22 : 10;
                d.CornerRadius = new CornerRadius(2);
                d.Margin = new Thickness(0, 0, 6, 0);
                d.Background = Palette.B(i == step ? ThemeManager.CurrentColor : Palette.Line);
                dots.Children.Add(d);
            }
            root.Children.Add(dots);

            var body = new StackPanel();
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var foot = new Grid();
            foot.Margin = new Thickness(0, 0, 0, 0);

            var line = new Border();
            line.Height = 1;
            line.Background = Palette.B(Palette.Line);
            line.Opacity = 0.5;
            line.Margin = new Thickness(0, 0, 0, 14);
            var footWrap = new StackPanel();
            footWrap.Margin = new Thickness(0, 22, 0, 0);
            footWrap.Children.Add(line);
            footWrap.Children.Add(foot);
            Grid.SetRow(footWrap, 2);
            root.Children.Add(footWrap);

            var cancel = new FlatButton("跳过");
            cancel.SetFontSize(12);
            cancel.SetPadding(new Thickness(14, 5, 14, 5));
            cancel.SetCornerRadius(new CornerRadius(6));
            cancel.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            cancel.HorizontalAlignment = HorizontalAlignment.Left;
            cancel.Click += delegate { finish(); };
            foot.Children.Add(cancel);

            var nav = new StackPanel();
            nav.Orientation = Orientation.Horizontal;
            nav.HorizontalAlignment = HorizontalAlignment.Right;
            if (step > 0)
            {
                var back = new FlatButton("上一步");
                back.SetFontSize(12);
                back.SetPadding(new Thickness(14, 5, 14, 5));
                back.SetCornerRadius(new CornerRadius(6));
                back.Margin = new Thickness(0, 0, 8, 0);
                back.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
                back.Click += delegate { go(step - 1); };
                nav.Children.Add(back);
            }
            var next = new FlatButton(step == Steps - 1 ? "开始使用" : "下一步");
            next.SetFontSize(12);
            next.SetPadding(new Thickness(18, 5, 18, 5));
            next.SetCornerRadius(new CornerRadius(6));
            next.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.12), Palette.Dark, Palette.Dark);
            next.Click += delegate
            {
                if (step == Steps - 1) { finish(); return; }
                // 导航那一步：先落到下一步再重建界面，让新导航立刻生效且引导停在第 4 步
                if (step == 2)
                {
                    cfg.OnboardStep = 3;
                    try { cfg.Save(); } catch { }
                    HugeLauncher.Ui.Pages.PageBase.RequestNavRefresh();
                    return;
                }
                go(step + 1);
            };
            nav.Children.Add(next);
            foot.Children.Add(nav);

            switch (step)
            {
                case 0: BuildWelcome(cfg, body); break;
                case 1: BuildLayoutChoice(cfg, body); break;
                case 2: BuildNavChoice(cfg, body); break;
                case 3: BuildWhereIsGame(body, openMigrate); break;
                case 4: BuildInstall(body, openDownload); break;
                case 5: BuildLogin(cfg, body, go); break;
                default: BuildDone(body); break;
            }
            return layer;
        }

        static void Title(StackPanel body, string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = 18;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Palette.B(Palette.Text);
            body.Children.Add(t);
        }

        static void Desc(StackPanel body, string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = 12;
            t.TextWrapping = TextWrapping.Wrap;
            t.Margin = new Thickness(0, 8, 0, 0);
            t.Foreground = Palette.B(Palette.Dim);
            body.Children.Add(t);
        }

        // ===== 第 1 步：主题（选完立刻真切） =====
        static void BuildWelcome(Config cfg, StackPanel body)
        {
            Title(body, "先挑一个你喜欢的主题");
            Desc(body, "选完立刻生效，之后可以在设置里随时换。");
            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 14, 0, 0);
            string[] skins = { "liquid", "frost", "solid" };
            string[] names = { "流光", "霜面", "素底" };
            for (int i = 0; i < skins.Length; i++)
            {
                string sk = skins[i];
                var b = new FlatButton(names[i]);
                b.SetFontSize(12);
                b.SetPadding(new Thickness(16, 6, 16, 6));
                b.SetCornerRadius(new CornerRadius(7));
                b.Margin = new Thickness(0, 0, 8, 0);
                bool on = cfg.Skin == sk;
                b.SetColors(on ? ThemeManager.CurrentColor : Color.FromArgb(0, 0, 0, 0),
                            on ? Palette.Lighten(ThemeManager.CurrentColor, 0.12) : Color.FromArgb(26, 255, 255, 255),
                            on ? Palette.Dark : Palette.Text, Palette.Text);
                b.Click += delegate
                {
                    cfg.Skin = sk;
                    try { cfg.Save(); } catch { }
                    ThemeManager.ApplySkin(sk);   // 触发整窗重建，引导会按当前步重挂
                };
                row.Children.Add(b);
            }
            body.Children.Add(row);


        }

        // ===== 第 3 步：挑导航 =====
        static void BuildNavChoice(Config cfg, StackPanel body)
        {
            Title(body, "挑几个常用的入口");
            Desc(body, "拖动 ≡ 排序，× 去掉不用的（按住 Shift+S 可以随时偷看被去掉的项）。之后在「设置 → 导航」里也能改。");

            var order = NavCatalog.OrderFrom(cfg);
            var hidden = NavCatalog.HiddenFrom(cfg);

            var head = new Grid();
            head.Margin = new Thickness(0, 12, 0, 6);
            head.ColumnDefinitions.Add(UiHelpers.StarCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            var tip = new TextBlock();
            tip.Text = "当前导航";
            tip.FontSize = 11;
            tip.VerticalAlignment = VerticalAlignment.Center;
            tip.Foreground = Palette.B(Palette.Dim);
            head.Children.Add(tip);

            var add = new FlatButton("＋");
            add.SetFontSize(12);
            add.SetPadding(new Thickness(9, 1, 9, 2));
            add.SetCornerRadius(new CornerRadius(5));
            add.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            Grid.SetColumn(add, 1);
            head.Children.Add(add);
            body.Children.Add(head);

            var list = new StackPanel();
            body.Children.Add(list);

            NavListEditor editor = null;
            editor = new NavListEditor(list, order, hidden,
                delegate { NavCatalog.Save(cfg, order, hidden); },
                delegate { if (editor != null) editor.BuildRows(); });
            editor.BuildRows();

            add.Click += delegate
            {
                var pop = new Popup();
                pop.PlacementTarget = add;
                pop.Placement = PlacementMode.Bottom;
                pop.StaysOpen = false;
                pop.AllowsTransparency = true;
                var panel = new Border();
                panel.Background = Palette.B(Palette.Panel);
                panel.BorderBrush = Palette.B(Palette.Line);
                panel.BorderThickness = new Thickness(1);
                panel.CornerRadius = new CornerRadius(10);
                panel.Padding = new Thickness(8);
                panel.MinWidth = 140;
                panel.Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.35 };
                var col = new StackPanel();
                panel.Child = col;
                var cap = new TextBlock();
                cap.Text = "添加导航";
                cap.FontSize = 11;
                cap.Foreground = Palette.B(Palette.Dim);
                cap.Margin = new Thickness(4, 0, 0, 6);
                col.Children.Add(cap);
                bool any = false;
                for (int i = 0; i < NavCatalog.All.Count; i++)
                {
                    var it = NavCatalog.All[i];
                    if (!hidden.Contains(it.Key)) continue;
                    any = true;
                    string k = it.Key;
                    var b = new FlatButton(it.Name);
                    b.SetFontSize(12);
                    b.SetPadding(new Thickness(10, 4, 10, 4));
                    b.SetCornerRadius(new CornerRadius(6));
                    b.HorizontalAlignment = HorizontalAlignment.Stretch;
                    b.Margin = new Thickness(0, 0, 0, 3);
                    b.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(30, 255, 255, 255), Palette.Text, Palette.Text);
                    b.Click += delegate
                    {
                        hidden.Remove(k);
                        if (!order.Contains(k)) order.Add(k);
                        NavCatalog.Save(cfg, order, hidden);
                        editor.BuildRows();
                        pop.IsOpen = false;
                    };
                    col.Children.Add(b);
                }
                if (!any)
                {
                    var empty = new TextBlock();
                    empty.Text = "没有可添加的项";
                    empty.FontSize = 12;
                    empty.Margin = new Thickness(4, 2, 4, 2);
                    empty.Foreground = Palette.B(Palette.Dim);
                    col.Children.Add(empty);
                }
                pop.Child = panel;
                pop.IsOpen = true;
            };
        }
        // ===== 第 2 步：选布局 =====
        static void BuildLayoutChoice(Config cfg, StackPanel body)
        {
            Title(body, "选一个你喜欢的布局");
            Desc(body, "之后随时可以在「设置 → 启动器设置 → 外观」里改。点一下马上生效。");

            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 16, 0, 0);
            body.Children.Add(row);

            AddLayoutCard(row, cfg, "vertical", "纵向舞台", "左侧图标导航 + 史蒂夫看板，右侧放内容");
            AddLayoutCard(row, cfg, "classic", "经典横排", "顶部标签导航，横向铺开，信息密度更大");
        }

        static void AddLayoutCard(StackPanel row, Config cfg, string value, string name, string desc)
        {
            bool on = cfg.Layout == value;
            var card = new Border();
            card.Width = 132;
            card.Margin = new Thickness(0, 0, 12, 0);
            card.CornerRadius = new CornerRadius(10);
            card.Padding = new Thickness(12);
            card.Cursor = Cursors.Hand;
            card.BorderThickness = new Thickness(on ? 2 : 1);
            card.BorderBrush = Palette.B(on ? ThemeManager.CurrentColor : Palette.Line);
            card.Background = Palette.B(Palette.Tile);

            var sp = new StackPanel();
            sp.Children.Add(MakePreview(value, on));
            var t = new TextBlock();
            t.Text = name;
            t.FontSize = 13;
            t.FontWeight = FontWeights.SemiBold;
            t.Margin = new Thickness(0, 10, 0, 3);
            t.Foreground = Palette.B(Palette.Text);
            sp.Children.Add(t);
            var d = new TextBlock();
            d.Text = desc;
            d.FontSize = 11;
            d.TextWrapping = TextWrapping.Wrap;
            d.Foreground = Palette.B(Palette.Dim);
            sp.Children.Add(d);
            card.Child = sp;

            card.MouseLeftButtonUp += delegate
            {
                if (cfg.Layout == value) return;
                cfg.Layout = value;
                cfg.Save();
                ThemeManager.ApplyLayout(value);
            };
            row.Children.Add(card);
        }

        static UIElement MakePreview(string value, bool on)
        {
            var g = new Grid();
            g.Height = 64;
            g.ClipToBounds = true;
            var bg = new Border();
            bg.CornerRadius = new CornerRadius(6);
            bg.Background = Palette.B(Palette.Line);
            bg.Opacity = 0.35;
            g.Children.Add(bg);
            Color accent = on ? ThemeManager.CurrentColor : Palette.Dim;
            if (value == "vertical")
            {
                var left = new Border();
                left.Width = 14;
                left.HorizontalAlignment = HorizontalAlignment.Left;
                left.Margin = new Thickness(6, 6, 0, 6);
                left.CornerRadius = new CornerRadius(3);
                left.Background = Palette.B(accent);
                left.Opacity = 0.75;
                g.Children.Add(left);
                for (int i = 0; i < 3; i++)
                {
                    var b = new Border();
                    b.Height = 14;
                    b.Margin = new Thickness(28, 6 + i * 18, 6, 0);
                    b.VerticalAlignment = VerticalAlignment.Top;
                    b.CornerRadius = new CornerRadius(3);
                    b.Background = Palette.B(accent);
                    b.Opacity = 0.25;
                    g.Children.Add(b);
                }
            }
            else
            {
                for (int i = 0; i < 4; i++)
                {
                    var b = new Border();
                    b.Width = 18;
                    b.Height = 8;
                    b.Margin = new Thickness(8 + i * 24, 8, 0, 0);
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    b.VerticalAlignment = VerticalAlignment.Top;
                    b.CornerRadius = new CornerRadius(3);
                    b.Background = Palette.B(accent);
                    b.Opacity = 0.65;
                    g.Children.Add(b);
                }
                var bodyB = new Border();
                bodyB.Margin = new Thickness(8, 24, 8, 8);
                bodyB.CornerRadius = new CornerRadius(4);
                bodyB.Background = Palette.B(accent);
                bodyB.Opacity = 0.22;
                g.Children.Add(bodyB);
            }
            return g;
        }
        // ===== 第 2 步：游戏在哪（扫描其他启动器） =====
        static void BuildWhereIsGame(StackPanel body, Action openMigrate)
        {
            Title(body, "你的游戏文件在哪？");
            Desc(body, "如果你用过别的启动器，下面的版本可以直接搬过来，不用重新下载。");
            var info = new TextBlock();
            info.Text = "正在扫描…";
            info.FontSize = 12;
            info.Margin = new Thickness(0, 12, 0, 0);
            info.TextWrapping = TextWrapping.Wrap;
            info.Foreground = Palette.B(Palette.Text);
            body.Children.Add(info);

            ThreadPool.QueueUserWorkItem(delegate
            {
                int roots = 0, versions = 0;
                try
                {
                    var cand = LauncherScan.CandidateRoots();
                    for (int i = 0; i < cand.Count; i++)
                    {
                        var list = LauncherScan.ScanRoot(cand[i][0], cand[i][1]);
                        if (list.Count > 0) { roots++; versions += list.Count; }
                    }
                }
                catch { }
                info.Dispatcher.BeginInvoke(new Action(delegate
                {
                    info.Text = roots == 0
                        ? "没找到别的启动器目录。你的 HIL 游戏目录已经就绪，直接下一步即可。"
                        : "找到 " + roots + " 个目录，共 " + versions + " 个可用版本。";
                }));
            });

            var btn = new FlatButton("打开迁移向导");
            btn.SetFontSize(12);
            btn.SetPadding(new Thickness(14, 5, 14, 5));
            btn.SetCornerRadius(new CornerRadius(6));
            btn.Margin = new Thickness(0, 12, 0, 0);
            btn.HorizontalAlignment = HorizontalAlignment.Left;
            btn.Click += delegate { openMigrate(); };
            body.Children.Add(btn);
        }

        // ===== 第 3 步：装个游戏 =====
        static void BuildInstall(StackPanel body, Action openDownload)
        {
            Title(body, "装一个游戏");
            Desc(body, "最新正式版最稳；想尝鲜可以用快照版。Java 运行时会自动准备。");
            var info = new TextBlock();
            info.Text = "正在获取版本…";
            info.FontSize = 12;
            info.Margin = new Thickness(0, 12, 0, 0);
            info.Foreground = Palette.B(Palette.Text);
            body.Children.Add(info);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string rel = null, snap = null;
                try
                {
                    var list = GameCore.GetVersionList();
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (rel == null && list[i].Type == "release") rel = list[i].Id;
                        if (snap == null && list[i].Type == "snapshot") snap = list[i].Id;
                        if (rel != null && snap != null) break;
                    }
                }
                catch { }
                info.Dispatcher.BeginInvoke(new Action(delegate
                {
                    info.Text = rel == null
                        ? "版本列表没取到，稍后可以在下载页重试。"
                        : "最新正式版 " + rel + "，最新快照 " + snap + "。";
                }));
            });

            var btn = new FlatButton("去下载页");
            btn.SetFontSize(12);
            btn.SetPadding(new Thickness(14, 5, 14, 5));
            btn.SetCornerRadius(new CornerRadius(6));
            btn.Margin = new Thickness(0, 12, 0, 0);
            btn.HorizontalAlignment = HorizontalAlignment.Left;
            btn.Click += delegate { openDownload(); };
            body.Children.Add(btn);
        }

        // ===== 第 4 步：登录 =====
        static void BuildLogin(Config cfg, StackPanel body, Action<int> go)
        {
            Title(body, "建一个离线账号");
            Desc(body, "输入游戏内名字就能开始玩。想用正版或第三方账号，之后到「账号」页添加。");
            var box = new TextBox();
            box.Text = cfg.Username;
            box.FontSize = 13;
            box.Padding = new Thickness(8, 6, 8, 6);
            box.Margin = new Thickness(0, 12, 0, 0);
            box.Background = Palette.B(Palette.Input);
            box.Foreground = Palette.B(Palette.Text);
            box.CaretBrush = Palette.B(Palette.Text);
            box.BorderBrush = Palette.B(Palette.Line);
            box.BorderThickness = new Thickness(1);
            body.Children.Add(box);

            var btn = new FlatButton("用这个名字开始");
            btn.SetFontSize(12);
            btn.SetPadding(new Thickness(14, 5, 14, 5));
            btn.SetCornerRadius(new CornerRadius(6));
            btn.Margin = new Thickness(0, 10, 0, 0);
            btn.HorizontalAlignment = HorizontalAlignment.Left;
            btn.Click += delegate
            {
                string nm = (box.Text ?? "").Trim();
                if (nm.Length == 0) return;
                try
                {
                    cfg.Username = nm;
                    cfg.Save();
                    AccountStore.AddOffline(nm);
                }
                catch { }
                go(4);
            };
            body.Children.Add(btn);
        }

        // ===== 第 5 步：完成 =====
        static void BuildDone(StackPanel body)
        {
            Title(body, "可以开始了");
            Desc(body, "两个小提示：\n· 首页右上角的铅笔可以编辑小部件，拖动和缩放都行；\n· 「更多」里有找乐子、工具和关于。\n以后想再看这份引导，去 设置 → 关于 → 重新查看新手引导。");
        }
    }
}
