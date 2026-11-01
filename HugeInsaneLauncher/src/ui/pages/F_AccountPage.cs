// HugeLauncher.Ui.Pages - 账号页（子代理 F）：离线 / 外置(authlib-injector) / 微软 三列瓷砖
using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    // 导航 key: account:home，NavParam 无
    // 三列等宽瓷砖：离线账号（本地列表+就地添加）、外置登录（Yggdrasil 表单）、微软账号（占位）
    class PageAccount : PageBase
    {
        StackPanel offlineList;    // 离线列：账号卡 + 「+ 添加」卡，整体重建
        StackPanel externalList;   // 外置列：账号卡列表区（表单只建一次，重建不碰输入框）
        TextBox serverBox;
        TextBox emailBox;
        PasswordBox pwdBox;
        FlatButton loginBtn;
        TextBlock loginStatus;
        bool addingOffline;        // 「+ 添加」已展开成输入行（防回车+失焦双提交）
        bool loginBusy;

        public PageAccount()
        {
            Margin = new Thickness(10);
            ColumnDefinitions.Add(UiHelpers.StarCol());
            ColumnDefinitions.Add(UiHelpers.StarCol());
            ColumnDefinitions.Add(UiHelpers.StarCol());

            // ---- 第 1 列：离线账号 ----
            offlineList = new StackPanel();
            var tileOffline = MkTile("离线账号", offlineList);
            Grid.SetColumn(tileOffline, 0);
            Children.Add(tileOffline);

            // ---- 第 2 列：外置登录 ----
            var extCol = new StackPanel();
            externalList = new StackPanel();
            extCol.Children.Add(externalList);
            extCol.Children.Add(BuildExternalForm());
            var tileExternal = MkTile("外置登录", extCol);
            Grid.SetColumn(tileExternal, 1);
            Children.Add(tileExternal);

            // ---- 第 3 列：微软账号（规划中占位，垂直+水平居中） ----
            var tileMs = new Border();
            tileMs.Background = Palette.B(Palette.Tile);
            tileMs.CornerRadius = new CornerRadius(10);
            tileMs.Padding = new Thickness(14);
            tileMs.Margin = new Thickness(6);
            var msGrid = new Grid();
            msGrid.RowDefinitions.Add(UiHelpers.AutoRow());
            msGrid.RowDefinitions.Add(UiHelpers.StarRow());
            var msLabel = CatLabel("微软账号");
            Grid.SetRow(msLabel, 0);
            msGrid.Children.Add(msLabel);
            var msStack = new StackPanel();
            msStack.HorizontalAlignment = HorizontalAlignment.Center;
            msStack.VerticalAlignment = VerticalAlignment.Center;
            var msBig = new TextBlock();
            msBig.Text = "规划中";
            msBig.FontSize = 16;
            msBig.Foreground = Palette.B(Palette.Dim);
            msBig.HorizontalAlignment = HorizontalAlignment.Center;
            var msSmall = new TextBlock();
            msSmall.Text = "未来接入微软正版登录";
            msSmall.FontSize = 11;
            msSmall.Foreground = Palette.B(Palette.Dim);
            msSmall.Margin = new Thickness(0, 6, 0, 0);
            msSmall.HorizontalAlignment = HorizontalAlignment.Center;
            msStack.Children.Add(msBig);
            msStack.Children.Add(msSmall);
            Grid.SetRow(msStack, 1);
            msGrid.Children.Add(msStack);
            tileMs.Child = msGrid;
            Grid.SetColumn(tileMs, 2);
            Children.Add(tileMs);

            RebuildAll();
        }

        public override void OnEnter()
        {
            RebuildAll();
            AccountStore.Changed += OnStoreChanged;
        }

        public override void OnLeave()
        {
            AccountStore.Changed -= OnStoreChanged;
        }

        // Changed 可能在后台线程触发（外置登录回调），统一回 UI 线程重建
        void OnStoreChanged()
        {
            Dispatcher.BeginInvoke(new Action(delegate { RebuildAll(); }));
        }

        void RebuildAll()
        {
            RebuildOffline();
            RebuildExternal();
        }

        // ---------- 离线列 ----------

        void RebuildOffline()
        {
            addingOffline = false;
            offlineList.Children.Clear();
            var sel = AccountStore.Selected();
            foreach (var a in AccountStore.All())
            {
                if (a.Type != "offline") continue;
                offlineList.Children.Add(AccountCard(a, "离线 · " + a.UuidShort, SameAccount(a, sel)));
            }
            offlineList.Children.Add(MkAddCard());
        }

        Border MkAddCard()
        {
            var b = new Border();
            b.Background = Brushes.Transparent;   // 透明也要刷，否则点不中
            b.BorderBrush = Palette.B(Palette.Dim);
            b.BorderThickness = new Thickness(1);
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(10);
            b.Margin = new Thickness(0, 0, 0, 8);
            b.Cursor = Cursors.Hand;
            var t = new TextBlock();
            t.Text = "+ 添加";
            t.FontSize = 12;
            t.Foreground = Palette.B(Palette.Dim);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            b.Child = t;
            b.MouseLeftButtonUp += delegate { ShowAddInput(); };
            return b;
        }

        // 「+ 添加」就地变输入行：回车或失焦提交，空名不动作
        void ShowAddInput()
        {
            if (addingOffline) return;
            addingOffline = true;
            // 换掉列表末尾的「+ 添加」卡
            offlineList.Children.RemoveAt(offlineList.Children.Count - 1);
            TextBox box;
            var input = MkInput("输入名字，回车添加", out box);
            input.Margin = new Thickness(0, 0, 0, 8);
            offlineList.Children.Add(input);
            box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; CommitAddOffline(box); }
            };
            box.LostFocus += delegate { CommitAddOffline(box); };
            // 点击冒泡完再抢焦点，否则被卡片的 MouseUp 顶掉
            Dispatcher.BeginInvoke(new Action(delegate { box.Focus(); }));
        }

        void CommitAddOffline(TextBox box)
        {
            if (!addingOffline) return;
            addingOffline = false;   // 先落闸：重建触发的 LostFocus 不再进来
            string name = (box.Text ?? "").Trim();
            if (name.Length > 0)
            {
                AccountStore.AddOffline(name);
                Toast("已添加并选中账号 " + name);
            }
            RebuildOffline();
        }

        // ---------- 外置列 ----------

        void RebuildExternal()
        {
            externalList.Children.Clear();
            var sel = AccountStore.Selected();
            foreach (var a in AccountStore.All())
            {
                if (a.Type != "external") continue;
                externalList.Children.Add(AccountCard(a, "外置 · " + a.ServerName + " · " + a.UuidShort, SameAccount(a, sel)));
            }
        }

        // 登录表单只建一次：重建列表不清输入框
        UIElement BuildExternalForm()
        {
            var form = new StackPanel();
            form.Margin = new Thickness(0, 12, 0, 0);

            var titleRow = new StackPanel();
            titleRow.Orientation = Orientation.Horizontal;
            var title = new TextBlock();
            title.Text = "添加外置账号";
            title.FontSize = 11;
            title.Foreground = Palette.B(Palette.Dim);
            title.VerticalAlignment = VerticalAlignment.Center;
            titleRow.Children.Add(title);
            titleRow.Children.Add(HelpCatalog.Dot("acct:external"));   // 说明收进「?」气泡，不再铺灰色小字
            titleRow.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(titleRow);

            // 预设行：一键填 LittleSkin 地址（说明收到悬停提示里，不再占旁边一行）
            var preset = new StackPanel();
            preset.Orientation = Orientation.Horizontal;
            preset.Margin = new Thickness(0, 0, 0, 8);
            var lsBtn = new FlatButton("LittleSkin");
            lsBtn.SetFontSize(11);
            lsBtn.SetPadding(new Thickness(10, 4, 10, 4));
            lsBtn.VerticalAlignment = VerticalAlignment.Center;
            lsBtn.ToolTip = "点击填入 LittleSkin 服务器地址";
            lsBtn.Click += delegate { serverBox.Text = "https://littleskin.cn/api/yggdrasil"; };
            preset.Children.Add(lsBtn);
            form.Children.Add(preset);

            var serverInput = MkInput("认证服务器地址", out serverBox);
            serverInput.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(serverInput);
            var emailInput = MkInput("邮箱 / 用户名", out emailBox);
            emailInput.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(emailInput);
            var pwdInput = MkPasswordInput("密码", out pwdBox);
            pwdInput.Margin = new Thickness(0, 0, 0, 8);
            form.Children.Add(pwdInput);

            loginBtn = new FlatButton("登录并添加");
            loginBtn.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.2), Palette.Dark, Palette.Dark);
            loginBtn.HorizontalAlignment = HorizontalAlignment.Left;
            loginBtn.Click += OnLogin;
            form.Children.Add(loginBtn);

            loginStatus = new TextBlock();
            loginStatus.FontSize = 10.5;
            loginStatus.Foreground = Palette.B(Palette.Dim);
            loginStatus.TextWrapping = TextWrapping.Wrap;
            loginStatus.Margin = new Thickness(0, 6, 0, 0);
            loginStatus.Visibility = Visibility.Collapsed;
            form.Children.Add(loginStatus);

            var hugeNote = new TextBlock();
            hugeNote.Text = "Huge账号敬请期待";
            hugeNote.FontSize = 10.5;
            hugeNote.Foreground = Palette.B(Palette.Dim);
            hugeNote.Margin = new Thickness(0, 10, 0, 0);
            form.Children.Add(hugeNote);
            return form;
        }

        void OnLogin(object sender, EventArgs e)
        {
            if (loginBusy) return;
            string email = (emailBox.Text ?? "").Trim();
            string pwd = pwdBox.Password ?? "";
            string server;
            try
            {
                server = Yggdrasil.NormalizeServer(serverBox.Text);
            }
            catch (Exception ex)
            {
                SetStatus(ex.Message, true);
                return;
            }
            if (email.Length == 0 || pwd.Length == 0)
            {
                SetStatus("请填写完整：服务器地址 / 邮箱 / 密码", true);
                return;
            }
            loginBusy = true;
            loginBtn.SetEnabled(false);
            SetStatus("正在登录…", false);
            // Authenticate 是同步网络请求，必须后台线程
            var t = new Thread(delegate()
            {
                Account acc = null;
                Exception err = null;
                try { acc = Yggdrasil.Authenticate(server, email, pwd); }
                catch (Exception ex) { err = ex; }
                if (acc != null)
                {
                    try { AccountStore.AddExternal(acc); }
                    catch (Exception ex2) { err = ex2; acc = null; }
                }
                Account done = acc;
                Exception fail = err;
                Dispatcher.BeginInvoke(new Action(delegate { OnLoginDone(done, fail); }));
            });
            t.IsBackground = true;
            t.Start();
        }

        void OnLoginDone(Account acc, Exception err)
        {
            loginBusy = false;
            loginBtn.SetEnabled(true);
            if (err != null)
            {
                SetStatus(err.Message, true);
                return;
            }
            SetStatus("", false);
            Toast("登录成功：" + acc.Name + " @ " + acc.ServerName);
            emailBox.Text = "";
            pwdBox.Password = "";
            RebuildExternal();
        }

        void SetStatus(string msg, bool isError)
        {
            loginStatus.Text = msg;
            loginStatus.Foreground = Palette.B(isError ? Palette.Red : Palette.Dim);
            loginStatus.Visibility = msg.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        // ---------- 账号卡（离线/外置共用） ----------

        static bool SameAccount(Account a, Account b)
        {
            if (a == null || b == null) return false;
            if (a.Type != b.Type) return false;
            if (a.Type == "external") return a.Uuid == b.Uuid;
            return a.Name == b.Name;
        }

        Border AccountCard(Account a, string subLine, bool selected)
        {
            var card = new Border();
            card.Background = Palette.B(Palette.Panel);
            card.CornerRadius = new CornerRadius(8);
            card.Padding = new Thickness(10);
            card.Margin = new Thickness(0, 0, 0, 8);
            card.Cursor = Cursors.Hand;

            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            var left = new StackPanel();
            left.VerticalAlignment = VerticalAlignment.Center;
            var name = new TextBlock();
            name.Text = a.Name;
            name.FontSize = 13;
            name.FontWeight = FontWeights.SemiBold;
            name.Foreground = Palette.B(Palette.Text);
            var sub = new TextBlock();
            sub.Text = subLine;
            sub.FontSize = 10.5;
            sub.Foreground = Palette.B(Palette.Dim);
            sub.Margin = new Thickness(0, 2, 0, 0);
            left.Children.Add(name);
            left.Children.Add(sub);
            Grid.SetColumn(left, 0);
            g.Children.Add(left);

            var right = new StackPanel();
            right.Orientation = Orientation.Horizontal;
            right.VerticalAlignment = VerticalAlignment.Center;
            if (selected)
            {
                var chk = new TextBlock();
                chk.Text = "✓";
                chk.FontSize = 13;
                chk.FontWeight = FontWeights.SemiBold;
                chk.Foreground = Palette.B(ThemeManager.CurrentColor);
                chk.VerticalAlignment = VerticalAlignment.Center;
                chk.Margin = new Thickness(0, 0, 8, 0);
                right.Children.Add(chk);
            }
            // 删除：独立小 Border 吃掉点击事件，不冒泡成卡片选中
            var delBrush = Palette.B(Palette.Dim);
            var del = new Border();
            del.Padding = new Thickness(4, 2, 4, 2);
            del.Cursor = Cursors.Hand;
            var delT = new TextBlock();
            delT.Text = "删除";
            delT.FontSize = 10.5;
            delT.Foreground = delBrush;
            del.Child = delT;
            del.MouseEnter += delegate { delBrush.Color = Palette.Red; };
            del.MouseLeave += delegate { delBrush.Color = Palette.Dim; };
            Account acc = a;   // C# 4.0 foreach 闭包陷阱：先拷本地再捕获
            del.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs e)
            {
                e.Handled = true;
                if (MessageBox.Show("删除账号 " + acc.Name + "？", "Huge Insane Launcher",
                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
                AccountStore.Remove(acc);
                RebuildAll();
            };
            right.Children.Add(del);
            Grid.SetColumn(right, 1);
            g.Children.Add(right);

            card.Child = g;
            card.MouseLeftButtonUp += delegate { AccountStore.Select(acc); RebuildAll(); };
            return card;
        }

        // ---------- 小控件工厂 ----------

        // 大瓷砖：左上角分类标签 + 下方滚动区
        static Border MkTile(string label, UIElement content)
        {
            var b = new Border();
            b.Background = Palette.B(Palette.Tile);
            b.CornerRadius = new CornerRadius(10);
            b.Padding = new Thickness(14);
            b.Margin = new Thickness(6);
            var g = new Grid();
            g.RowDefinitions.Add(UiHelpers.AutoRow());
            g.RowDefinitions.Add(UiHelpers.StarRow());
            var t = CatLabel(label);
            Grid.SetRow(t, 0);
            g.Children.Add(t);
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            scroll.Content = content;
            Grid.SetRow(scroll, 1);
            g.Children.Add(scroll);
            b.Child = g;
            return b;
        }

        static TextBlock CatLabel(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 11;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Palette.B(ThemeManager.CurrentColor);
            t.Margin = new Thickness(0, 0, 0, 8);
            return t;
        }

        // 黑底白字输入框 + Dim 提示叠层（仿聊天框写法）
        static Border MkInput(string hint, out TextBox box)
        {
            var wrap = InputShell();
            var g = new Grid();
            var tb = new TextBox();
            tb.Background = new SolidColorBrush(Colors.Transparent);
            tb.Foreground = Palette.B(Palette.Text);
            tb.BorderThickness = new Thickness(0);
            tb.Padding = new Thickness(9, 6, 9, 6);
            tb.FontSize = 12;
            tb.CaretBrush = Palette.B(Palette.Text);
            tb.VerticalContentAlignment = VerticalAlignment.Center;
            var h = HintLabel(hint);
            g.Children.Add(tb);
            g.Children.Add(h);
            tb.TextChanged += delegate
            {
                h.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            wrap.Child = g;
            box = tb;
            return wrap;
        }

        static Border MkPasswordInput(string hint, out PasswordBox box)
        {
            var wrap = InputShell();
            var g = new Grid();
            var pb = new PasswordBox();
            pb.Background = new SolidColorBrush(Colors.Transparent);
            pb.Foreground = Palette.B(Palette.Text);
            pb.BorderThickness = new Thickness(0);
            pb.Padding = new Thickness(9, 6, 9, 6);
            pb.FontSize = 12;
            pb.CaretBrush = Palette.B(Palette.Text);
            pb.VerticalContentAlignment = VerticalAlignment.Center;
            var h = HintLabel(hint);
            g.Children.Add(pb);
            g.Children.Add(h);
            pb.PasswordChanged += delegate
            {
                h.Visibility = pb.Password.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            };
            wrap.Child = g;
            box = pb;
            return wrap;
        }

        static Border InputShell()
        {
            var wrap = new Border();
            wrap.Background = Palette.B(Palette.Input);
            wrap.BorderBrush = Palette.B(Palette.Line);
            wrap.BorderThickness = new Thickness(1);
            wrap.CornerRadius = new CornerRadius(4);
            wrap.MinHeight = 30;
            return wrap;
        }

        static TextBlock HintLabel(string hint)
        {
            var h = new TextBlock();
            h.Text = hint;
            h.Foreground = Palette.B(Palette.Dim);
            h.FontSize = 12;
            h.IsHitTestVisible = false;
            h.VerticalAlignment = VerticalAlignment.Center;
            h.Margin = new Thickness(10, 0, 0, 0);
            return h;
        }
    }
}
