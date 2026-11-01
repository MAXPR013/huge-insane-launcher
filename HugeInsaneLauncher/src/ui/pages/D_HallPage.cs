// HugeLauncher.Ui.Pages - 联机大厅页：Huge 隧道玩家端（输入房间码加入腐竹的世界）
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace HugeLauncher.Ui.Pages
{
    // 导航 key: server:hall。布局：瓷砖一「加入房间」（房间码输入+状态+已加入面板）
    // + 瓷砖二「创建房间（局域网）」（端口 + 隧道开关 + 创建按钮 → 房间码）。说明文字全部收进「?」气泡。
    // 加入成功：显示手动兜底地址（127.0.0.1[:port]）+ 复制；房间也会经假 LAN 广播
    // 自动出现在 MC 多人游戏的局域网列表（被防火墙拦了就照兜底地址手填）。
    class PageServerHall : PageBase
    {
        HintTextBox codeBox;
        FlatButton joinBtn, hallCopyBtn, leaveBtn;
        TextBlock hallStatus, addrBigText;
        Border joinedCard;
        bool joining;   // 连接中（防抖，避免连点）

        // ===== 局域网联机创建房间 =====
        HintTextBox lanPortBox;
        MiniCheckBox lanTunnelChk;
        FlatButton lanCreateBtn, lanRoomCopyBtn;
        TextBlock lanStatus, lanRoomLabel, lanRoomText;
        bool lanCreating;   // 连接隧道服务器中（防抖）

        public PageServerHall()
        {
            RowDefinitions.Add(UiHelpers.AutoRow()); // 瓷砖一：加入房间
            RowDefinitions.Add(UiHelpers.AutoRow()); // 瓷砖二：局域网创建房间

            // ===== 输入行：房间码 + 加入按钮 =====
            var row = new Grid();
            row.Margin = new Thickness(0, 8, 0, 0);   // 提示行撤掉后补回间距
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            codeBox = new HintTextBox("输入腐竹给你的房间码");
            codeBox.Width = 220;
            codeBox.Box.BorderBrush = Palette.B(Palette.Line);
            codeBox.Box.BorderThickness = new Thickness(1);
            codeBox.Box.FontFamily = new FontFamily("Consolas");
            codeBox.Box.CharacterCasing = CharacterCasing.Upper;
            codeBox.Box.MaxLength = 6;
            codeBox.Box.KeyDown += delegate(object s, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Enter) OnJoin();
            };
            row.Children.Add(codeBox);
            joinBtn = new FlatButton("加入");
            joinBtn.SetPadding(new Thickness(22, 8, 22, 8));
            joinBtn.SetCornerRadius(new CornerRadius(8));
            joinBtn.SetFontSize(13);
            joinBtn.Margin = new Thickness(10, 0, 0, 0);
            var ac = ThemeManager.CurrentColor;
            joinBtn.SetColors(ac, Palette.Lighten(ac, 0.15), Colors.White, Colors.White);
            joinBtn.Click += delegate { OnJoin(); };
            Grid.SetColumn(joinBtn, 1);
            row.Children.Add(joinBtn);

            // ===== 状态行 =====
            hallStatus = new TextBlock();
            hallStatus.Foreground = Palette.B(Palette.Dim);
            hallStatus.FontSize = 11.5;
            hallStatus.Margin = new Thickness(2, 10, 0, 0);

            // ===== 已加入面板：大字兜底地址 + 复制 + 断开 =====
            joinedCard = new Border();
            joinedCard.Background = Palette.B(Palette.Panel);
            joinedCard.CornerRadius = new CornerRadius(8);
            joinedCard.Padding = new Thickness(14);
            joinedCard.Margin = new Thickness(0, 12, 0, 0);
            joinedCard.Visibility = Visibility.Collapsed;
            var jg = new Grid();
            jg.RowDefinitions.Add(UiHelpers.AutoRow());
            jg.RowDefinitions.Add(UiHelpers.AutoRow());
            jg.RowDefinitions.Add(UiHelpers.AutoRow());
            var okLine = new TextBlock();
            okLine.Text = "已加入房间！打开 MC → 多人游戏，房间一般会自动出现在局域网列表";
            okLine.Foreground = Palette.B(Palette.Green);
            okLine.FontSize = 12;
            var okWrap = new StackPanel();
            okWrap.Orientation = Orientation.Horizontal;
            okWrap.Children.Add(okLine);
            okWrap.Children.Add(HelpCatalog.Dot("hall:joined"));
            jg.Children.Add(okWrap);
            var addrRow = new Grid();
            addrRow.Margin = new Thickness(0, 8, 0, 0);
            addrRow.ColumnDefinitions.Add(UiHelpers.AutoCol());
            addrRow.ColumnDefinitions.Add(UiHelpers.AutoCol());
            addrRow.ColumnDefinitions.Add(UiHelpers.AutoCol());
            addrBigText = new TextBlock();
            addrBigText.Foreground = Palette.B(Palette.Text);
            addrBigText.FontSize = 20;
            addrBigText.FontWeight = FontWeights.Bold;
            addrBigText.FontFamily = new FontFamily("Consolas");
            addrBigText.VerticalAlignment = VerticalAlignment.Center;
            addrRow.Children.Add(addrBigText);
            hallCopyBtn = new FlatButton("复制");
            hallCopyBtn.SetFontSize(10);
            hallCopyBtn.SetPadding(new Thickness(8, 3, 8, 3));
            hallCopyBtn.Margin = new Thickness(10, 0, 0, 0);
            hallCopyBtn.VerticalAlignment = VerticalAlignment.Center;
            hallCopyBtn.Click += delegate
            {
                try { Clipboard.SetText(addrBigText.Text); Toast("地址已复制：" + addrBigText.Text); }
                catch { Toast("复制失败"); }
            };
            Grid.SetColumn(hallCopyBtn, 1);
            addrRow.Children.Add(hallCopyBtn);
            leaveBtn = new FlatButton("断开");
            leaveBtn.SetFontSize(10);
            leaveBtn.SetPadding(new Thickness(8, 3, 8, 3));
            leaveBtn.Margin = new Thickness(8, 0, 0, 0);
            leaveBtn.VerticalAlignment = VerticalAlignment.Center;
            leaveBtn.Click += delegate
            {
                TunnelManager.StopJoin();
                ApplyIdle();
                Toast("已断开隧道");
            };
            Grid.SetColumn(leaveBtn, 2);
            addrRow.Children.Add(leaveBtn);
            Grid.SetRow(addrRow, 1);
            jg.Children.Add(addrRow);
            joinedCard.Child = jg;

            // ===== 瓷砖一：加入房间 =====
            var tileJoin = new SettingsTile("加入房间", "hall:join");
            row.Margin = new Thickness(0, 4, 0, 0);
            tileJoin.Body.Children.Add(row);
            tileJoin.Body.Children.Add(hallStatus);
            tileJoin.Body.Children.Add(joinedCard);
            Grid.SetRow(tileJoin, 0);
            Children.Add(tileJoin);

            // ================= 瓷砖二：局域网创建房间 =================
            var tileLan = new SettingsTile("创建房间（局域网）", "hall:lan");

            // 端口输入框：游戏内「对局域网开放」提示的端口
            lanPortBox = new HintTextBox("输入游戏内提供的端口号，如“25565”");
            lanPortBox.Width = 300;
            lanPortBox.HorizontalAlignment = HorizontalAlignment.Left;
            lanPortBox.Box.BorderBrush = Palette.B(Palette.Line);
            lanPortBox.Box.BorderThickness = new Thickness(1);
            lanPortBox.Box.FontFamily = new FontFamily("Consolas");
            lanPortBox.Box.MaxLength = 5;
            lanPortBox.Margin = new Thickness(0, 4, 0, 0);
            tileLan.Body.Children.Add(lanPortBox);

            // 开关（工业风=复选框/玻璃=iOS 开关，MiniCheckBox 自动换装）+ 创建按钮
            var lanRow = new Grid();
            lanRow.Margin = new Thickness(0, 10, 0, 0);
            lanRow.ColumnDefinitions.Add(UiHelpers.AutoCol());
            lanRow.ColumnDefinitions.Add(UiHelpers.AutoCol());
            lanRow.ColumnDefinitions.Add(UiHelpers.AutoCol());
            lanTunnelChk = new MiniCheckBox();
            lanTunnelChk.SetChecked(true, false);   // 默认开：不开隧道房间码发不出去，朋友进不来
            lanTunnelChk.VerticalAlignment = VerticalAlignment.Center;
            lanRow.Children.Add(lanTunnelChk);
            var lanTunnelLabel = new TextBlock();
            lanTunnelLabel.Text = "开启 Huge 隧道";
            lanTunnelLabel.Foreground = Palette.B(Palette.Text);
            lanTunnelLabel.FontSize = 12;
            lanTunnelLabel.Margin = new Thickness(8, 0, 0, 0);
            lanTunnelLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(lanTunnelLabel, 1);
            lanRow.Children.Add(lanTunnelLabel);
            lanCreateBtn = new FlatButton("创建房间");
            lanCreateBtn.SetPadding(new Thickness(22, 8, 22, 8));
            lanCreateBtn.SetCornerRadius(new CornerRadius(8));
            lanCreateBtn.SetFontSize(13);
            lanCreateBtn.Margin = new Thickness(18, 0, 0, 0);
            lanCreateBtn.SetColors(ac, Palette.Lighten(ac, 0.15), Colors.White, Colors.White);
            lanCreateBtn.Click += delegate { OnLanCreate(); };
            Grid.SetColumn(lanCreateBtn, 2);
            lanRow.Children.Add(lanCreateBtn);
            tileLan.Body.Children.Add(lanRow);

            // 状态 + 房间码行（创建成功才亮码）
            var lanStateRow = new Grid();
            lanStateRow.Margin = new Thickness(2, 10, 0, 0);
            lanStateRow.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 房间码标签
            lanStateRow.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 房间码/状态
            lanStateRow.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 复制
            lanRoomLabel = new TextBlock();
            lanRoomLabel.Text = "房间码";
            lanRoomLabel.Foreground = Palette.B(Palette.Dim);
            lanRoomLabel.FontSize = 11;
            lanRoomLabel.VerticalAlignment = VerticalAlignment.Center;
            lanRoomLabel.Visibility = Visibility.Collapsed;
            lanStateRow.Children.Add(lanRoomLabel);
            lanRoomText = new TextBlock();
            lanRoomText.Foreground = Palette.B(Palette.Text);
            lanRoomText.FontSize = 15;
            lanRoomText.FontWeight = FontWeights.Bold;
            lanRoomText.FontFamily = new FontFamily("Consolas");
            lanRoomText.Margin = new Thickness(8, 0, 0, 0);
            lanRoomText.VerticalAlignment = VerticalAlignment.Center;
            lanRoomText.Visibility = Visibility.Collapsed;
            Grid.SetColumn(lanRoomText, 1);
            lanStateRow.Children.Add(lanRoomText);
            lanRoomCopyBtn = new FlatButton("复制");
            lanRoomCopyBtn.SetFontSize(10);
            lanRoomCopyBtn.SetPadding(new Thickness(8, 3, 8, 3));
            lanRoomCopyBtn.Margin = new Thickness(10, 0, 0, 0);
            lanRoomCopyBtn.VerticalAlignment = VerticalAlignment.Center;
            lanRoomCopyBtn.Visibility = Visibility.Collapsed;
            lanRoomCopyBtn.Click += delegate
            {
                try { Clipboard.SetText(lanRoomText.Text); Toast("房间码已复制：" + lanRoomText.Text); }
                catch { Toast("复制失败"); }
            };
            Grid.SetColumn(lanRoomCopyBtn, 2);
            lanStateRow.Children.Add(lanRoomCopyBtn);
            lanStatus = new TextBlock();
            lanStatus.Foreground = Palette.B(Palette.Dim);
            lanStatus.FontSize = 11.5;
            lanStatus.VerticalAlignment = VerticalAlignment.Center;
            lanStatus.Visibility = Visibility.Collapsed;
            // 状态文本与房间码共用一格（互斥显示）
            Grid.SetColumn(lanStatus, 1);
            lanStateRow.Children.Add(lanStatus);
            tileLan.Body.Children.Add(lanStateRow);
            Grid.SetRow(tileLan, 1);
            Children.Add(tileLan);
        }

        // ===== LAN 创建房间 =====

        void OnLanCreate()
        {
            if (lanCreating) return;
            // 已创建 → 点击即关闭
            if (TunnelManager.IsHostActive)
            {
                TunnelManager.StopHost();
                ApplyLanIdle();
                Toast("房间已关闭");
                return;
            }
            if (!lanTunnelChk.IsChecked)
            {
                Toast("未开隧道：你的世界已在局域网内广播，同一网络的朋友直接在多人游戏列表进");
                return;
            }
            int port;
            string ps = lanPortBox.Text.Trim();
            if (!int.TryParse(ps, out port) || port < 1 || port > 65535)
            {
                SetLanStatus("端口号不对——填游戏聊天栏提示的那串数字（对局域网开放后会出现）", true);
                return;
            }
            lanCreating = true;
            lanCreateBtn.SetEnabled(false);
            SetLanStatus("正在连接隧道服务器…", false);
            TunnelManager.StartHost("lan", port);
        }

        void ApplyLanIdle()
        {
            lanCreating = false;
            lanCreateBtn.SetEnabled(true);
            lanCreateBtn.Text = "创建房间";
            ShowLanRoom(null);
            lanStatus.Visibility = Visibility.Collapsed;
        }

        void ApplyLanCreated(string room)
        {
            lanCreating = false;
            lanCreateBtn.SetEnabled(true);
            lanCreateBtn.Text = "关闭房间";
            lanStatus.Visibility = Visibility.Collapsed;
            ShowLanRoom(room);
        }

        void ShowLanRoom(string room)
        {
            bool on = room != null;
            if (on) lanRoomText.Text = room;
            lanRoomLabel.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            lanRoomText.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            lanRoomCopyBtn.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }

        void SetLanStatus(string s, bool isErr)
        {
            lanStatus.Text = s ?? "";
            lanStatus.Foreground = Palette.B(isErr ? Palette.Red : Palette.Dim);
            lanStatus.Visibility = s != null ? Visibility.Visible : Visibility.Collapsed;
            if (s != null) ShowLanRoom(null);
        }

        void OnLanHostStatus(string s)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(OnLanHostStatus), s); return; }
            // 只处理 LAN 房间的（开服页也开着隧道时不抢它的状态）
            if (TunnelManager.HostInstance != "lan") return;
            if (s.StartsWith("ROOM:")) ApplyLanCreated(s.Substring(5));
            else if (s.StartsWith("ERR:"))
            {
                ApplyLanIdle();
                SetLanStatus(s.Substring(4), true);
            }
            else if (!TunnelManager.IsHostActive) ApplyLanIdle();
            else SetLanStatus(s, false);
        }

        void OnJoin()
        {
            if (joining) return;
            string code = codeBox.Text.Trim().ToUpperInvariant();
            if (code.Length != 6)
            {
                SetStatus("房间码是 6 位字母数字（腐竹开服页面上亮着的那串）", true);
                return;
            }
            foreach (char c in code)
                if (char.IsLetterOrDigit(c) == false) { SetStatus("房间码只能包含字母和数字", true); return; }
            joining = true;
            joinBtn.SetEnabled(false);
            joinedCard.Visibility = Visibility.Collapsed;
            SetStatus("正在连接房间 " + code + " …", false);
            TunnelManager.StartJoin(code);
        }

        void SetStatus(string s, bool isErr)
        {
            hallStatus.Text = s ?? "";
            hallStatus.Foreground = Palette.B(isErr ? Palette.Red : Palette.Dim);
        }

        void ApplyIdle()
        {
            joining = false;
            joinBtn.SetEnabled(true);
            joinBtn.Text = "加入";
            joinedCard.Visibility = Visibility.Collapsed;
        }

        void ApplyJoined(int port)
        {
            joining = false;
            joinBtn.SetEnabled(true);
            // 与双击局域网列表完全同款地址（本机 LAN IP + 显式端口），杜绝连错目标
            addrBigText.Text = TunnelManager.JoinDisplayAddress ?? ("127.0.0.1:" + port);
            joinedCard.Visibility = Visibility.Visible;
        }

        // ===== 隧道事件（后台线程 → 归队 UI 线程）=====

        void OnJoinStatus(string s)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(OnJoinStatus), s); return; }
            if (s.StartsWith("ERR:"))
            {
                SetStatus(s.Substring(4), true);
                ApplyIdle();
            }
            else SetStatus(s, false);
        }

        void OnJoinReady(int port)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<int>(OnJoinReady), port); return; }
            ApplyJoined(port);
        }

        public override void OnEnter()
        {
            TunnelManager.JoinStatus -= OnJoinStatus;
            TunnelManager.JoinReady -= OnJoinReady;
            TunnelManager.JoinStatus += OnJoinStatus;
            TunnelManager.JoinReady += OnJoinReady;
            TunnelManager.HostStatus -= OnLanHostStatus;
            TunnelManager.HostStatus += OnLanHostStatus;
            // 页面离开时隧道仍在后台跑：回来时恢复面板
            if (TunnelManager.IsJoinActive && TunnelManager.JoinLocalPort > 0)
            {
                ApplyJoined(TunnelManager.JoinLocalPort);
                SetStatus("已加入房间 " + TunnelManager.JoinCode, false);
            }
            // LAN 房间还开着就恢复房间码显示（开服页的隧道不归这里管）
            if (TunnelManager.IsHostActive && TunnelManager.HostInstance == "lan" && TunnelManager.HostRoom != null)
                ApplyLanCreated(TunnelManager.HostRoom);
        }

        public override void OnLeave()
        {
            TunnelManager.JoinStatus -= OnJoinStatus;
            TunnelManager.JoinReady -= OnJoinReady;
            TunnelManager.HostStatus -= OnLanHostStatus;
        }
    }
}
