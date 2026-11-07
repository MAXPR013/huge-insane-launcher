// HugeLauncher.Ui.Pages - 账号页（单页）：列表 + 详情 + 两段式添加弹窗
// 导航 key: account:home
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    class PageAccount : PageBase
    {
        static readonly Color ColOffline = Color.FromRgb(0x9A, 0xA1, 0xB0);
        static readonly Color ColMs = Color.FromRgb(0x4A, 0x9E, 0xFF);
        static readonly Color ColExt = Color.FromRgb(0xE0, 0x8A, 0x3C);

        Grid host;          // 内容 + 弹窗
        Grid content;       // 列表 / 详情
        Border overlay;     // 弹窗层（含遮罩）
        Border dialog;      // 弹窗卡片
        bool dialogOpen;

        // 弹窗状态
        TextBox nameBox;
        DarkComboBox typeCombo;
        FlatButton primaryBtn;
        int stage = 1;      // 1=名称+类型 2=凭证
        TextBox serverBox, emailBox;
        PasswordBox pwdBox;
        TextBlock statusLine;
        string pendingType = "offline";

        public PageAccount()
        {
            Margin = new Thickness(16, 12, 16, 12);
            host = new Grid();
            Children.Add(host);

            content = new Grid();
            host.Children.Add(content);

            BuildList();
        }

        public override void OnEnter()
        {
            BuildList();
            if (Environment.GetEnvironmentVariable("HIL_ACCT_DEMO") == "1") OpenDialog();
            // 调试：HIL_ACCT_DETAIL=1 直接打开首个账号详情，便于离屏截图验收
            if (Environment.GetEnvironmentVariable("HIL_ACCT_DETAIL") == "1")
            {
                var t = new System.Windows.Threading.DispatcherTimer();
                t.Interval = TimeSpan.FromMilliseconds(900);
                t.Tick += delegate
                {
                    t.Stop();
                    var list = AccountStore.All();
                    if (list != null && list.Count > 0) ShowDetail(list[0]);
                };
                t.Start();
            }
        }

        // ================= 列表 =================
        void BuildList()
        {
            if (dialogOpen) return;
            content.Children.Clear();
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());
            content.Children.Add(g);
            AnimateIn(g);

            // 标题
            var head = new TextBlock();
            head.Text = "账号";
            head.FontSize = 17;
            head.FontWeight = FontWeights.SemiBold;
            head.Foreground = Palette.B(Palette.Text);
            g.Children.Add(head);

            // 虚线添加框
            var addTile = new Grid();
            addTile.Height = 64;
            addTile.Margin = new Thickness(0, 10, 0, 10);
            addTile.Cursor = Cursors.Hand;
            var dash = new System.Windows.Shapes.Rectangle();
            dash.RadiusX = 10; dash.RadiusY = 10;
            dash.Stroke = Palette.B(Palette.Line);
            dash.StrokeThickness = 1.5;
            dash.StrokeDashArray = new DoubleCollection(new double[] { 5, 4 });
            dash.Fill = new SolidColorBrush(Color.FromArgb(12, 255, 255, 255));
            addTile.Children.Add(dash);
            var addStack = new StackPanel();
            addStack.HorizontalAlignment = HorizontalAlignment.Center;
            addStack.VerticalAlignment = VerticalAlignment.Center;
            var plus = new TextBlock();
            plus.Text = "＋";
            plus.FontSize = 18;
            plus.HorizontalAlignment = HorizontalAlignment.Center;
            plus.Foreground = Palette.B(Palette.Dim);
            addStack.Children.Add(plus);
            var addCap = new TextBlock();
            addCap.Text = "添加账号";
            addCap.FontSize = 11;
            addCap.HorizontalAlignment = HorizontalAlignment.Center;
            addCap.Foreground = Palette.B(Palette.Dim);
            addStack.Children.Add(addCap);
            addTile.Children.Add(addStack);
            addTile.MouseLeftButtonUp += delegate { OpenDialog(); };
            addTile.MouseEnter += delegate { dash.Stroke = Palette.B(ThemeManager.CurrentColor); plus.Foreground = Palette.B(ThemeManager.CurrentColor); };
            addTile.MouseLeave += delegate { dash.Stroke = Palette.B(Palette.Line); plus.Foreground = Palette.B(Palette.Dim); };
            Grid.SetRow(addTile, 1);
            g.Children.Add(addTile);

            // 账号行（放进滚动区，账号多了也能翻）
            var rows = new StackPanel();
            var scroller = new SmoothScrollViewer();
            scroller.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroller.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroller.Content = rows;
            Grid.SetRow(scroller, 2);
            g.Children.Add(scroller);
            List<Account> all = AccountStore.All();
            Account sel = AccountStore.Selected();
            if (all.Count == 0)
            {
                var empty = new TextBlock();
                empty.Text = "还没有账号，先添加一个吧";
                empty.FontSize = 12;
                empty.Margin = new Thickness(2, 8, 0, 0);
                empty.Foreground = Palette.B(Palette.Dim);
                rows.Children.Add(empty);
            }
            for (int i = 0; i < all.Count; i++)
            {
                Account a = all[i];
                bool on = sel != null && sel.Name == a.Name && sel.Type == a.Type;
                rows.Children.Add(BuildRow(a, on));
            }
        }

        UIElement BuildRow(Account a, bool selected)
        {
            var host = new Border();
            host.CornerRadius = new CornerRadius(10);
            host.Background = Palette.B(Palette.Tile);
            host.BorderThickness = new Thickness(1);
            host.BorderBrush = selected ? Palette.B(ThemeManager.CurrentColor) : Palette.B(Palette.Line);
            host.Padding = new Thickness(12, 10, 12, 10);
            host.Margin = new Thickness(0, 0, 0, 8);
            host.Cursor = Cursors.Hand;

            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            // 头像（先用首字母色块，皮肤头后续接入）
            var av = new Border();
            av.Width = 34; av.Height = 34;
            av.CornerRadius = new CornerRadius(8);
            av.Background = Palette.B(TypeColor(a.Type));
            av.Margin = new Thickness(0, 0, 10, 0);
            var avt = new TextBlock();
            avt.Text = string.IsNullOrEmpty(a.Name) ? "?" : a.Name.Substring(0, 1).ToUpper();
            avt.FontSize = 15;
            avt.FontWeight = FontWeights.SemiBold;
            avt.Foreground = Palette.B(Palette.Dark);
            avt.HorizontalAlignment = HorizontalAlignment.Center;
            avt.VerticalAlignment = VerticalAlignment.Center;
            av.Child = avt;
            g.Children.Add(av);

            var name = new TextBlock();
            name.Text = a.Name;
            name.FontSize = 13;
            name.VerticalAlignment = VerticalAlignment.Center;
            name.Foreground = Palette.B(Palette.Text);
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(name, 1);
            g.Children.Add(name);

            // 选择按钮（类型标注前面）
            var radio = new Border();
            radio.Width = 18; radio.Height = 18;
            radio.CornerRadius = new CornerRadius(9);
            radio.BorderThickness = new Thickness(1.5);
            radio.BorderBrush = selected ? Palette.B(ThemeManager.CurrentColor) : Palette.B(Palette.Line);
            radio.Background = selected ? Palette.B(ThemeManager.CurrentColor) : Brushes.Transparent;
            radio.Margin = new Thickness(0, 0, 8, 0);
            radio.VerticalAlignment = VerticalAlignment.Center;
            radio.Cursor = Cursors.Hand;
            var dot = new Border();
            dot.Width = 6; dot.Height = 6;
            dot.CornerRadius = new CornerRadius(3);
            dot.Background = Palette.B(Palette.Dark);
            dot.HorizontalAlignment = HorizontalAlignment.Center;
            dot.VerticalAlignment = VerticalAlignment.Center;
            dot.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
            radio.Child = dot;
            radio.MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                AccountStore.Select(a);
                BuildList();
            };
            Grid.SetColumn(radio, 2);
            g.Children.Add(radio);

            // 类型标签
            var badge = new Border();
            badge.CornerRadius = new CornerRadius(5);
            badge.Padding = new Thickness(7, 2, 7, 3);
            Color tc = TypeColor(a.Type);
            badge.Background = new SolidColorBrush(Color.FromArgb(28, tc.R, tc.G, tc.B));
            badge.VerticalAlignment = VerticalAlignment.Center;
            var bt = new TextBlock();
            bt.Text = TypeText(a.Type);
            bt.FontSize = 11;
            bt.Foreground = Palette.B(tc);
            badge.Child = bt;
            Grid.SetColumn(badge, 3);
            g.Children.Add(badge);

            host.Child = g;
            host.MouseLeftButtonUp += delegate { ShowDetail(a); };
            return host;
        }

        static string TypeText(string t)
        {
            if (t == "microsoft") return "正版";
            if (t == "external") return "第三方";
            return "离线";
        }

        static Color TypeColor(string t)
        {
            if (t == "microsoft") return ColMs;
            if (t == "external") return ColExt;
            return ColOffline;
        }

        // 列表 ↔ 详情切换：淡入 + 从右侧滑入，和主窗口换页同款手感
        void AnimateIn(UIElement el)
        {
            try
            {
                el.Opacity = 0;
                var tf = new TranslateTransform(14, 0);
                el.RenderTransform = tf;
                Ani.Start("acctSwitch",
                    Ani.AaOpacity(el, 1, 150),
                    Ani.Aa(delegate(double d) { tf.X += d; }, -14, 220, new AniEaseOutFluent(Ani.Weak)));
            }
            catch { }
        }
        // ================= 详情 =================
        void ShowDetail(Account a)
        {
            content.Children.Clear();
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());
            content.Children.Add(g);
            AnimateIn(g);

            var back = new FlatButton("← 返回账号列表");
            back.SetFontSize(12);
            back.SetPadding(new Thickness(10, 4, 10, 4));
            back.SetCornerRadius(new CornerRadius(6));
            back.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Click += delegate { BuildList(); };
            g.Children.Add(back);

            var body = new Grid();
            body.Margin = new Thickness(0, 12, 0, 0);
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4, GridUnitType.Star) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            g.Children.Add(body);

            // 左：皮肤正面图（用这个账号自己的皮肤，取不到才退回默认）
            var skinCard = new Border();
            skinCard.CornerRadius = new CornerRadius(10);
            skinCard.Background = Palette.B(Palette.Tile);
            skinCard.BorderThickness = new Thickness(1);
            skinCard.BorderBrush = Palette.B(Palette.Line);
            skinCard.Margin = new Thickness(0, 0, 10, 0);
            skinCard.ClipToBounds = true;
            BitmapSource own = null;
            try { own = Skins.For(a); } catch { }
            var skin = new SkinPreview(own ?? DefaultSkin());
            skinCard.Child = skin;
            body.Children.Add(skinCard);

            // 右：信息
            var right = new StackPanel();
            right.Margin = new Thickness(6, 4, 0, 0);
            Grid.SetColumn(right, 1);
            body.Children.Add(right);

            var nameRow = new StackPanel();
            nameRow.Orientation = Orientation.Horizontal;
            var nm = new TextBlock();
            nm.Text = a.Name;
            nm.FontSize = 20;
            nm.FontWeight = FontWeights.SemiBold;
            nm.Foreground = Palette.B(Palette.Text);
            nameRow.Children.Add(nm);
            var badge = new Border();
            badge.CornerRadius = new CornerRadius(5);
            badge.Padding = new Thickness(7, 2, 7, 3);
            badge.Margin = new Thickness(10, 0, 0, 0);
            badge.VerticalAlignment = VerticalAlignment.Center;
            Color tc = TypeColor(a.Type);
            badge.Background = new SolidColorBrush(Color.FromArgb(30, tc.R, tc.G, tc.B));
            var bt = new TextBlock();
            bt.Text = TypeText(a.Type);
            bt.FontSize = 11;
            bt.Foreground = Palette.B(tc);
            badge.Child = bt;
            nameRow.Children.Add(badge);
            right.Children.Add(nameRow);

            right.Children.Add(DetailRow("UUID", string.IsNullOrEmpty(a.Uuid) ? "（离线账号使用固定 UUID）" : a.Uuid));
            if (a.Type == "external") right.Children.Add(DetailRow("服务器", string.IsNullOrEmpty(a.ServerName) ? a.Server : a.ServerName));
            right.Children.Add(DetailRow("状态", TypeText(a.Type) + (a.Type == "offline" ? " · 仅本地" : " · 已登录")));

            var del = new FlatButton("删除账号");
            del.SetFontSize(12);
            del.SetPadding(new Thickness(12, 5, 12, 5));
            del.SetCornerRadius(new CornerRadius(6));
            del.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(30, 217, 83, 79), Palette.Red, Palette.Red);
            del.HorizontalAlignment = HorizontalAlignment.Left;
            del.Margin = new Thickness(0, 26, 0, 0);
            del.Click += delegate
            {
                AccountStore.Remove(a);
                BuildList();
            };
            right.Children.Add(del);
        }

        UIElement DetailRow(string k, string v)
        {
            var row = new Grid();
            row.Margin = new Thickness(0, 12, 0, 0);
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(UiHelpers.StarCol());
            var kt = new TextBlock();
            kt.Text = k;
            kt.FontSize = 11;
            kt.Foreground = Palette.B(Palette.Dim);
            row.Children.Add(kt);
            var vt = new TextBlock();
            vt.Text = v;
            vt.FontSize = 12;
            vt.Foreground = Palette.B(Palette.Text);
            vt.TextWrapping = TextWrapping.Wrap;
            Grid.SetColumn(vt, 1);
            row.Children.Add(vt);
            return row;
        }

        static System.Windows.Media.Imaging.BitmapSource DefaultSkin()
        {
            try { return SteveView.LoadSkin(); } catch { return null; }
        }

        // ================= 添加账号弹窗 =================
        void OpenDialog()
        {
            dialogOpen = true;
            stage = 1;

            dialog = new Border();
            dialog.Width = 380;
            dialog.CornerRadius = new CornerRadius(12);
            dialog.Background = Palette.B(Palette.Light
                ? Color.FromArgb(252, 0xFF, 0xFF, 0xFF)
                : Color.FromArgb(252, 0x1C, 0x1F, 0x27));
            dialog.BorderThickness = new Thickness(1);
            dialog.BorderBrush = Palette.B(Palette.Line);
            dialog.Padding = new Thickness(18, 16, 18, 14);
            dialog.HorizontalAlignment = HorizontalAlignment.Center;
            dialog.VerticalAlignment = VerticalAlignment.Center;
            dialog.Effect = new DropShadowEffect { BlurRadius = 26, ShadowDepth = 4, Opacity = 0.4 };
            dialog.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e) { e.Handled = true; };
            PageBase.ShowModal(dialog, false, delegate { dialogOpen = false; });

            BuildStage();
        }

        void CloseDialog()
        {
            dialogOpen = false;
            PageBase.HideModal();
        }

        void BuildStage()
        {
            var stack = new StackPanel();
            var title = new TextBlock();
            title.Text = "添加账号";
            title.FontSize = 15;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Palette.B(Palette.Text);
            stack.Children.Add(title);

            if (stage == 1)
            {
                stack.Children.Add(FieldLabel("名称"));
                nameBox = MakeInput("");
                nameBox.Margin = new Thickness(0, 4, 0, 10);
                stack.Children.Add(nameBox);

                stack.Children.Add(FieldLabel("类型"));
                typeCombo = new DarkComboBox();
                typeCombo.FontSize = 12;
                typeCombo.Height = 30;
                typeCombo.Margin = new Thickness(0, 4, 0, 8);
                typeCombo.Items.Add("离线");
                typeCombo.Items.Add("第三方");
                typeCombo.Items.Add("正版");
                typeCombo.SelectedIndex = 0;
                typeCombo.SelectionChanged += delegate { UpdatePrimary(); };
                stack.Children.Add(typeCombo);

                var hint = new TextBlock();
                hint.Text = "请总是支持正版 Minecraft";
                hint.FontSize = 10.5;
                hint.Margin = new Thickness(1, 2, 0, 0);
                hint.Foreground = Palette.B(Palette.Dim);
                stack.Children.Add(hint);
            }
            else
            {
                var who = new TextBlock();
                who.Text = pendingType == "microsoft" ? "微软正版登录" : "第三方外置登录";
                who.FontSize = 12;
                who.Margin = new Thickness(0, 6, 0, 10);
                who.Foreground = Palette.B(Palette.Dim);
                stack.Children.Add(who);

                if (pendingType == "external")
                {
                    stack.Children.Add(FieldLabel("服务器"));
                    serverBox = MakeInput("https://littleskin.cn/api/yggdrasil");
                    stack.Children.Add(serverBox);
                    stack.Children.Add(FieldLabel("邮箱"));
                    emailBox = MakeInput("");
                    stack.Children.Add(emailBox);
                    stack.Children.Add(FieldLabel("密码"));
                    pwdBox = new PasswordBox();
                    pwdBox.Background = Palette.B(Palette.Input);
                    pwdBox.Foreground = Palette.B(Palette.Text);
                    pwdBox.BorderBrush = Palette.B(Palette.Line);
                    pwdBox.BorderThickness = new Thickness(1);
                    pwdBox.Padding = new Thickness(8, 5, 8, 5);
                    pwdBox.FontSize = 12;
                    pwdBox.Margin = new Thickness(0, 4, 0, 0);
                    stack.Children.Add(pwdBox);
                }
                else
                {
                    var soon = new TextBlock();
                    soon.Text = "微软登录已在开发中：接入完成后，这里会显示一次性代码，在浏览器输入即可登录。";
                    soon.FontSize = 11.5;
                    soon.TextWrapping = TextWrapping.Wrap;
                    soon.Margin = new Thickness(0, 2, 0, 8);
                    soon.Foreground = Palette.B(Palette.Dim);
                    stack.Children.Add(soon);
                }

                statusLine = new TextBlock();
                statusLine.FontSize = 11;
                statusLine.Margin = new Thickness(0, 8, 0, 0);
                statusLine.TextWrapping = TextWrapping.Wrap;
                statusLine.Foreground = Palette.B(Palette.Red);
                stack.Children.Add(statusLine);
            }

            // 按钮行：左下灰色取消
            var btns = new Grid();
            btns.Margin = new Thickness(0, 18, 0, 0);
            var leftBtn = new StackPanel();
            leftBtn.Orientation = Orientation.Horizontal;
            var cancel = new FlatButton("取消");
            cancel.SetFontSize(12);
            cancel.SetPadding(new Thickness(14, 5, 14, 5));
            cancel.SetCornerRadius(new CornerRadius(6));
            cancel.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            cancel.Click += delegate { CloseDialog(); };
            leftBtn.Children.Add(cancel);
            if (stage == 2)
            {
                var back = new FlatButton("上一步");
                back.SetFontSize(12);
                back.SetPadding(new Thickness(14, 5, 14, 5));
                back.SetCornerRadius(new CornerRadius(6));
                back.Margin = new Thickness(8, 0, 0, 0);
                back.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
                back.Click += delegate { stage = 1; AnimateStage(); };
                leftBtn.Children.Add(back);
            }
            btns.Children.Add(leftBtn);

            primaryBtn = new FlatButton(stage == 1 ? "创建" : "登录");
            primaryBtn.SetFontSize(12);
            primaryBtn.SetPadding(new Thickness(18, 5, 18, 5));
            primaryBtn.SetCornerRadius(new CornerRadius(6));
            primaryBtn.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.12), Palette.Dark, Palette.Dark);
            primaryBtn.HorizontalAlignment = HorizontalAlignment.Right;
            primaryBtn.Click += delegate { OnPrimary(); };
            btns.Children.Add(primaryBtn);
            stack.Children.Add(btns);

            dialog.Child = stack;
            UpdatePrimary();
        }

        void UpdatePrimary()
        {
            if (primaryBtn == null || stage != 1) return;
            int idx = typeCombo != null ? typeCombo.SelectedIndex : 0;
            primaryBtn.Text = idx == 0 ? "创建" : "下一步";
            if (nameBox != null) nameBox.IsEnabled = idx == 0;
        }

        TextBlock FieldLabel(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 11;
            t.Margin = new Thickness(1, 6, 0, 0);
            t.Foreground = Palette.B(Palette.Dim);
            return t;
        }

        static TextBox MakeInput(string text)
        {
            var box = new TextBox();
            box.Text = text ?? "";
            box.Background = Palette.B(Palette.Input);
            box.Foreground = Palette.B(Palette.Text);
            box.CaretBrush = Palette.B(Palette.Text);
            box.BorderBrush = Palette.B(Palette.Line);
            box.BorderThickness = new Thickness(1);
            box.Padding = new Thickness(8, 5, 8, 5);
            box.FontSize = 12;
            box.Margin = new Thickness(0, 4, 0, 6);
            return box;
        }

        void OnPrimary()
        {
            if (stage == 1)
            {
                int idx = typeCombo != null ? typeCombo.SelectedIndex : 0;
                pendingType = idx == 0 ? "offline" : (idx == 1 ? "external" : "microsoft");
                if (pendingType == "offline")
                {
                    string nm = nameBox != null ? nameBox.Text.Trim() : "";
                    if (nm.Length == 0) { ShakeDialog(); return; }
                    AccountStore.AddOffline(nm);
                    CloseDialog();
                    BuildList();
                }
                else
                {
                    stage = 2;
                    AnimateStage();
                }
                return;
            }

            if (pendingType == "external")
            {
                DoExternalLogin();
            }
        }

        void DoExternalLogin()
        {
            string server = serverBox != null ? serverBox.Text.Trim() : "";
            string email = emailBox != null ? emailBox.Text.Trim() : "";
            string pwd = pwdBox != null ? pwdBox.Password : "";
            if (server.Length == 0 || email.Length == 0 || pwd.Length == 0)
            {
                if (statusLine != null) statusLine.Text = "服务器、邮箱、密码都要填";
                ShakeDialog();
                return;
            }
            if (statusLine != null) statusLine.Foreground = Palette.B(Palette.Dim);
            if (statusLine != null) statusLine.Text = "登录中…";
            if (primaryBtn != null) primaryBtn.IsEnabled = false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Account acc = null;
                string err = null;
                try { acc = Yggdrasil.Authenticate(server, email, pwd); }
                catch (Exception ex) { err = ex.Message; }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (primaryBtn != null) primaryBtn.IsEnabled = true;
                    if (acc == null)
                    {
                        if (statusLine != null)
                        {
                            statusLine.Foreground = Palette.B(Palette.Red);
                            statusLine.Text = string.IsNullOrEmpty(err) ? "登录失败" : err;
                        }
                        return;
                    }
                    AccountStore.AddExternal(acc);
                    CloseDialog();
                    BuildList();
                }));
            });
        }

        // 两步之间平滑改高：先钉住当前高度，动画到目标高度，结束后放开自适应
        void AnimateStage()
        {
            double from = dialog.ActualHeight > 0 ? dialog.ActualHeight : 200;
            dialog.Height = from;
            BuildStage();
            dialog.Measure(new Size(dialog.Width, double.PositiveInfinity));
            double to = Math.Max(160, dialog.DesiredSize.Height);
            double delta = to - from;
            if (Math.Abs(delta) < 1) { dialog.Height = double.NaN; return; }
            Ani.Start("acctStage",
                Ani.Aa(delegate(double d) { dialog.Height = from + d; }, delta, 260, new AniEaseOutFluent(Ani.Weak)),
                Ani.AaCode(delegate { dialog.Height = double.NaN; }, 10, false));
        }

        void ShakeDialog()
        {
            if (dialog == null) return;
            var tf = dialog.RenderTransform as TranslateTransform;
            if (tf == null) { tf = new TranslateTransform(); dialog.RenderTransform = tf; }
            Ani.Start("acctShake",
                Ani.Aa(delegate(double d) { tf.X += d; }, 6, 50, new AniEaseOutFluent(Ani.Weak)),
                Ani.Aa(delegate(double d) { tf.X += d; }, -12, 80, new AniEaseOutFluent(Ani.Weak)),
                Ani.Aa(delegate(double d) { tf.X += d; }, 6, 80, new AniEaseOutFluent(Ani.Weak)));
        }
    }
}
