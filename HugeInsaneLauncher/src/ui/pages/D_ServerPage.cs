// HugeLauncher.Ui.Pages - 一键开服页：版本/实例联动 + 内存/核心/端口/人数 + 名称/介绍 + 地址复制 + 控制台
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HugeLauncher.Ai;

namespace HugeLauncher.Ui.Pages
{
    class PageServerOneClick : PageBase
    {
        DarkComboBox versionCombo, instanceCombo, coreCombo;
        TextBox memBox, consoleBox, maxPlayersBox;
        HintTextBox portBox, nameBox, motdBox, cmdBox;
        FlatButton actionBtn, copyBtn, copyBtn6, roomCopyBtn;
        TextBlock statusDot, statusText, addrText, addr6Text, roomLabel, roomCodeText;
        MiniCheckBox chkTunnel;
        List<ServerInstance> instList = new List<ServerInstance>(); // 与 instanceCombo 项一一对应
        string lanIp, ipv6;        // 构造时探测一次
        string addrFull, addr6Full; // 复制按钮用的完整地址
        bool syncing;          // 下拉联动防递归
        bool busy;             // 准备/关闭中
        string curVersion;     // 当前选中的版本（无实例时）
        string curInstance;    // 当前选中的实例名
        ServerInstance curCfg; // 当前实例配置（选中实例时）

        public PageServerOneClick()
        {
            RowDefinitions.Add(UiHelpers.AutoRow());
            RowDefinitions.Add(UiHelpers.AutoRow());
            RowDefinitions.Add(UiHelpers.AutoRow());
            RowDefinitions.Add(UiHelpers.StarRow());

            // ===== 标题（说明收进「?」气泡，不再铺灰字） =====
            var title = new TextBlock();
            title.Text = "一键开服";
            title.Foreground = Palette.B(Palette.Text);
            title.FontSize = 17;
            title.FontWeight = FontWeights.SemiBold;
            title.Margin = new Thickness(2, 4, 0, 2);
            var titleRow = new StackPanel();
            titleRow.Orientation = Orientation.Horizontal;
            titleRow.Children.Add(title);
            titleRow.Children.Add(HelpCatalog.Dot("server:guide"));
            Children.Add(titleRow);

            // ===== 顶部操作卡 =====
            var card = new Border();
            card.Background = Palette.B(Palette.Panel);
            card.CornerRadius = new CornerRadius(8);
            card.Padding = new Thickness(14);
            var cardGrid = new Grid();
            cardGrid.RowDefinitions.Add(UiHelpers.AutoRow());
            cardGrid.RowDefinitions.Add(UiHelpers.AutoRow());
            cardGrid.RowDefinitions.Add(UiHelpers.AutoRow());
            cardGrid.RowDefinitions.Add(UiHelpers.AutoRow()); // 第四行：Huge 隧道

            // 第一行：版本选择 45% + 10px + 实例选择 45%
            var row1 = new Grid();
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45, GridUnitType.Star) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            row1.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45, GridUnitType.Star) });
            versionCombo = new DarkComboBox();
            versionCombo.SelectionChanged += delegate { OnVersionSelected(); };
            row1.Children.Add(versionCombo);
            instanceCombo = new DarkComboBox();
            Grid.SetColumn(instanceCombo, 2);
            instanceCombo.SelectionChanged += delegate { OnInstanceSelected(); };
            row1.Children.Add(instanceCombo);
            cardGrid.Children.Add(row1);

            // 第二行：内存 + 核心类型 + 端口 + 动态按钮
            var row2 = new Grid();
            row2.Margin = new Thickness(0, 10, 0, 0);
            row2.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 内存框
            row2.ColumnDefinitions.Add(UiHelpers.AutoCol()); // MB
            row2.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 核心
            row2.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 端口标签
            row2.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 端口框
            row2.ColumnDefinitions.Add(UiHelpers.StarCol());
            row2.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 按钮
            memBox = new TextBox();
            memBox.Width = 80;
            memBox.Text = "2048";
            memBox.Background = Palette.B(Palette.Input);
            memBox.Foreground = Palette.B(Palette.Text);
            memBox.BorderBrush = Palette.B(Palette.Line);
            memBox.CaretBrush = Palette.B(Palette.Text);
            memBox.FontSize = 12;
            memBox.Padding = new Thickness(6, 4, 6, 4);
            memBox.VerticalContentAlignment = VerticalAlignment.Center;
            row2.Children.Add(memBox);
            var mb = new TextBlock();
            mb.Text = "MB";
            mb.Foreground = Palette.B(Palette.Dim);
            mb.FontSize = 11;
            mb.Margin = new Thickness(6, 0, 0, 0);
            mb.VerticalAlignment = VerticalAlignment.Center;
            var memWrap = new StackPanel();
            memWrap.Orientation = Orientation.Horizontal;
            memWrap.Margin = new Thickness(0, 0, 12, 0);
            memWrap.Children.Add(mb);
            memWrap.Children.Add(HelpCatalog.Dot("server:memory"));
            Grid.SetColumn(memWrap, 1);
            row2.Children.Add(memWrap);
            coreCombo = new DarkComboBox();
            coreCombo.Width = 120;
            coreCombo.Items.Add("Vanilla");
            coreCombo.Items.Add("Paper");
            coreCombo.Items.Add(new ComboBoxItem { Content = "Forge（即将支持）", IsEnabled = false });
            coreCombo.Items.Add(new ComboBoxItem { Content = "Fabric（即将支持）", IsEnabled = false });
            coreCombo.Items.Add(new ComboBoxItem { Content = "NeoForge（即将支持）", IsEnabled = false });
            coreCombo.SelectedIndex = 0;
            var coreWrap = new StackPanel();
            coreWrap.Orientation = Orientation.Horizontal;
            coreWrap.Children.Add(coreCombo);
            coreWrap.Children.Add(HelpCatalog.Dot("server:core"));
            Grid.SetColumn(coreWrap, 2);
            row2.Children.Add(coreWrap);
            var portLabel = new TextBlock();
            portLabel.Text = "端口";
            portLabel.Foreground = Palette.B(Palette.Dim);
            portLabel.FontSize = 11;
            portLabel.Margin = new Thickness(12, 0, 6, 0);
            portLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(portLabel, 3);
            row2.Children.Add(portLabel);
            portBox = new HintTextBox("自动");
            portBox.Width = 70;
            portBox.Box.BorderBrush = Palette.B(Palette.Line);
            portBox.Box.BorderThickness = new Thickness(1);
            var portWrap = new StackPanel();
            portWrap.Orientation = Orientation.Horizontal;
            portWrap.Children.Add(portBox);
            portWrap.Children.Add(HelpCatalog.Dot("server:port"));
            Grid.SetColumn(portWrap, 4);
            row2.Children.Add(portWrap);
            actionBtn = new FlatButton("请选择版本或实例");
            actionBtn.SetPadding(new Thickness(18, 8, 18, 8));
            actionBtn.SetCornerRadius(new CornerRadius(8));
            actionBtn.SetFontSize(13);
            actionBtn.SetEnabled(false);
            actionBtn.Click += delegate { OnAction(); };
            Grid.SetColumn(actionBtn, 6);
            row2.Children.Add(actionBtn);
            // H史 UiMap：实例下拉框与操作按钮（start_server/stop_server 剧本 LookAt 目标）
            UiMap.Register("server:instances", delegate { return instanceCombo; });
            UiMap.Register("server:action", delegate { return actionBtn; });
            Grid.SetRow(row2, 1);
            cardGrid.Children.Add(row2);

            // 第三行：名称 + 介绍 + 人数上限（只读，随内存计算）
            var row3 = new Grid();
            row3.Margin = new Thickness(0, 10, 0, 0);
            row3.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 名称标签
            row3.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 名称框
            row3.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 介绍标签
            row3.ColumnDefinitions.Add(UiHelpers.StarCol()); // 介绍框
            row3.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 可容标签
            row3.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 人数框
            row3.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 人
            var nameLabel = new TextBlock();
            nameLabel.Text = "名称";
            nameLabel.Foreground = Palette.B(Palette.Dim);
            nameLabel.FontSize = 11;
            nameLabel.Margin = new Thickness(0, 0, 6, 0);
            nameLabel.VerticalAlignment = VerticalAlignment.Center;
            row3.Children.Add(nameLabel);
            nameBox = new HintTextBox("默认 = 实例名");
            nameBox.Width = 140;
            nameBox.Box.BorderBrush = Palette.B(Palette.Line);
            nameBox.Box.BorderThickness = new Thickness(1);
            Grid.SetColumn(nameBox, 1);
            row3.Children.Add(nameBox);
            var motdLabel = new TextBlock();
            motdLabel.Text = "介绍";
            motdLabel.Foreground = Palette.B(Palette.Dim);
            motdLabel.FontSize = 11;
            motdLabel.Margin = new Thickness(12, 0, 6, 0);
            motdLabel.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(motdLabel, 2);
            row3.Children.Add(motdLabel);
            motdBox = new HintTextBox("朋友在服务器列表看到的介绍（MOTD）");
            motdBox.Box.BorderBrush = Palette.B(Palette.Line);
            motdBox.Box.BorderThickness = new Thickness(1);
            Grid.SetColumn(motdBox, 3);
            row3.Children.Add(motdBox);
            var mpLabel = new TextBlock();
            mpLabel.Text = "可容";
            mpLabel.Foreground = Palette.B(Palette.Dim);
            mpLabel.FontSize = 11;
            mpLabel.Margin = new Thickness(12, 0, 6, 0);
            mpLabel.VerticalAlignment = VerticalAlignment.Center;
            var mpWrap = new StackPanel();
            mpWrap.Orientation = Orientation.Horizontal;
            mpWrap.Children.Add(mpLabel);
            mpWrap.Children.Add(HelpCatalog.Dot("server:maxplayers"));
            Grid.SetColumn(mpWrap, 4);
            row3.Children.Add(mpWrap);
            maxPlayersBox = new TextBox();
            maxPlayersBox.Width = 44;
            maxPlayersBox.IsReadOnly = true;
            maxPlayersBox.Text = "20";
            maxPlayersBox.Background = Palette.B(Palette.Input);
            maxPlayersBox.Foreground = Palette.B(Palette.Dim);
            maxPlayersBox.BorderBrush = Palette.B(Palette.Line);
            maxPlayersBox.FontSize = 12;
            maxPlayersBox.Padding = new Thickness(4, 4, 4, 4);
            maxPlayersBox.TextAlignment = TextAlignment.Center;
            maxPlayersBox.VerticalContentAlignment = VerticalAlignment.Center;
            Grid.SetColumn(maxPlayersBox, 5);
            row3.Children.Add(maxPlayersBox);
            var mpUnit = new TextBlock();
            mpUnit.Text = "人";
            mpUnit.Foreground = Palette.B(Palette.Dim);
            mpUnit.FontSize = 11;
            mpUnit.Margin = new Thickness(6, 0, 0, 0);
            mpUnit.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(mpUnit, 6);
            row3.Children.Add(mpUnit);
            Grid.SetRow(row3, 2);
            cardGrid.Children.Add(row3);

            // 第四行：Huge 隧道开关（工业风=复选框/玻璃=iOS开关，MiniCheckBox 自动换装）
            // + 「?」帮助钮 + 房间码显示（隧道开启后亮码）+ 复制
            var row4 = new Grid();
            row4.Margin = new Thickness(0, 10, 0, 0);
            row4.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 开关
            row4.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 标签 + ?
            row4.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 房间码标签
            row4.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 房间码
            row4.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 复制按钮
            row4.ColumnDefinitions.Add(UiHelpers.StarCol()); // 剩余空间
            chkTunnel = new MiniCheckBox();
            chkTunnel.VerticalAlignment = VerticalAlignment.Center;
            chkTunnel.Changed += delegate { OnTunnelToggled(); };
            row4.Children.Add(chkTunnel);
            var tunnelLabel = new TextBlock();
            tunnelLabel.Text = "开启 Huge 隧道";
            tunnelLabel.Foreground = Palette.B(Palette.Text);
            tunnelLabel.FontSize = 12;
            tunnelLabel.Margin = new Thickness(8, 0, 12, 0);
            tunnelLabel.VerticalAlignment = VerticalAlignment.Center;
            var tunnelWrap = new StackPanel();
            tunnelWrap.Orientation = Orientation.Horizontal;
            tunnelWrap.Children.Add(tunnelLabel);
            tunnelWrap.Children.Add(HelpCatalog.Dot("server:tunnel"));
            Grid.SetColumn(tunnelWrap, 1);
            row4.Children.Add(tunnelWrap);
            roomLabel = new TextBlock();
            roomLabel.Text = "房间码";
            roomLabel.Foreground = Palette.B(Palette.Dim);
            roomLabel.FontSize = 11;
            roomLabel.VerticalAlignment = VerticalAlignment.Center;
            roomLabel.Visibility = Visibility.Collapsed;
            Grid.SetColumn(roomLabel, 2);
            row4.Children.Add(roomLabel);
            roomCodeText = new TextBlock();
            roomCodeText.Foreground = Palette.B(Palette.Text);
            roomCodeText.FontSize = 15;
            roomCodeText.FontWeight = FontWeights.Bold;
            roomCodeText.FontFamily = new FontFamily("Consolas");
            roomCodeText.Margin = new Thickness(8, 0, 0, 0);
            roomCodeText.VerticalAlignment = VerticalAlignment.Center;
            roomCodeText.Visibility = Visibility.Collapsed;
            Grid.SetColumn(roomCodeText, 3);
            row4.Children.Add(roomCodeText);
            roomCopyBtn = new FlatButton("复制");
            roomCopyBtn.SetFontSize(10);
            roomCopyBtn.SetPadding(new Thickness(7, 2, 7, 2));
            roomCopyBtn.Margin = new Thickness(8, 0, 12, 0);
            roomCopyBtn.Visibility = Visibility.Collapsed;
            roomCopyBtn.Click += delegate { CopyAddr(TunnelManager.HostRoom); };
            Grid.SetColumn(roomCopyBtn, 4);
            row4.Children.Add(roomCopyBtn);
            Grid.SetRow(row4, 3);
            cardGrid.Children.Add(row4);

            card.Child = cardGrid;
            card.Margin = new Thickness(0, 8, 0, 0);   // 提示行撤掉后补回标题与卡片的间距
            Grid.SetRow(card, 2);
            Children.Add(card);

            // ===== 控制台区 =====
            var consoleGrid = new Grid();
            consoleGrid.RowDefinitions.Add(UiHelpers.AutoRow());
            consoleGrid.RowDefinitions.Add(UiHelpers.StarRow());
            consoleGrid.RowDefinitions.Add(UiHelpers.AutoRow()); // 命令输入行
            consoleGrid.Margin = new Thickness(0, 10, 0, 0);
            // 头部：状态灯 + 状态文字 + 地址复制（IPv4 / 有 IPv6 则多一条）+ 清空
            var head = new Grid();
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            head.ColumnDefinitions.Add(UiHelpers.StarCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol()); // 「?」帮助钮
            statusDot = new TextBlock();
            statusDot.Text = "●";
            statusDot.Foreground = Palette.B(Palette.Dim);
            statusDot.FontSize = 11;
            statusDot.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(statusDot);
            statusText = new TextBlock();
            statusText.Text = " 已停止";
            statusText.Foreground = Palette.B(Palette.Dim);
            statusText.FontSize = 11;
            statusText.VerticalAlignment = VerticalAlignment.Center;
            statusText.Margin = new Thickness(4, 0, 8, 0);
            Grid.SetColumn(statusText, 1);
            head.Children.Add(statusText);
            addrText = new TextBlock();
            addrText.Foreground = Palette.B(Palette.Text);
            addrText.FontSize = 11;
            addrText.FontFamily = new FontFamily("Consolas");
            addrText.VerticalAlignment = VerticalAlignment.Center;
            addrText.Margin = new Thickness(8, 0, 4, 0);
            addrText.Visibility = Visibility.Collapsed;
            Grid.SetColumn(addrText, 2);
            head.Children.Add(addrText);
            copyBtn = new FlatButton("复制");
            copyBtn.SetFontSize(10);
            copyBtn.SetPadding(new Thickness(7, 2, 7, 2));
            copyBtn.Visibility = Visibility.Collapsed;
            copyBtn.Click += delegate { CopyAddr(addrFull); };
            Grid.SetColumn(copyBtn, 3);
            head.Children.Add(copyBtn);
            addr6Text = new TextBlock();
            addr6Text.Foreground = Palette.B(Palette.Dim);
            addr6Text.FontSize = 11;
            addr6Text.FontFamily = new FontFamily("Consolas");
            addr6Text.VerticalAlignment = VerticalAlignment.Center;
            addr6Text.Margin = new Thickness(10, 0, 4, 0);
            addr6Text.MaxWidth = 230;
            addr6Text.TextTrimming = TextTrimming.CharacterEllipsis;
            addr6Text.Visibility = Visibility.Collapsed;
            Grid.SetColumn(addr6Text, 4);
            head.Children.Add(addr6Text);
            copyBtn6 = new FlatButton("复制");
            copyBtn6.SetFontSize(10);
            copyBtn6.SetPadding(new Thickness(7, 2, 7, 2));
            copyBtn6.Visibility = Visibility.Collapsed;
            copyBtn6.Click += delegate { CopyAddr(addr6Full); };
            Grid.SetColumn(copyBtn6, 5);
            head.Children.Add(copyBtn6);
            var clearBtn = new FlatButton("清空");
            clearBtn.SetFontSize(10);
            clearBtn.SetPadding(new Thickness(8, 2, 8, 2));
            clearBtn.Click += delegate
            {
                if (curInstance != null) ServerManager.ClearLog(curInstance);
                consoleBox.Text = "";
            };
            Grid.SetColumn(clearBtn, 7);
            head.Children.Add(clearBtn);
            var consoleDot = HelpCatalog.Dot("server:console");
            Grid.SetColumn(consoleDot, 8);
            head.Children.Add(consoleDot);
            consoleGrid.Children.Add(head);
            // 控制台本体（只读，自动滚底；配色跟色板：暗=黑终端，亮=浅灰底深字）
            consoleBox = new TextBox();
            consoleBox.IsReadOnly = true;
            consoleBox.FontFamily = new FontFamily("Consolas");
            consoleBox.FontSize = 9;
            consoleBox.Background = Palette.B(Palette.Light ? Color.FromRgb(0xF2, 0xF4, 0xF8) : Color.FromRgb(0x10, 0x12, 0x18));
            consoleBox.Foreground = Palette.B(Palette.Light ? Color.FromRgb(0x3A, 0x3F, 0x4E) : Color.FromRgb(0x9A, 0xA0, 0xB0));
            consoleBox.BorderThickness = new Thickness(0);
            consoleBox.TextWrapping = TextWrapping.Wrap;
            consoleBox.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            consoleBox.Margin = new Thickness(0, 6, 0, 0);
            consoleBox.Padding = new Thickness(8);
            Grid.SetRow(consoleBox, 1);
            consoleGrid.Children.Add(consoleBox);
            // 命令输入行：回车发送（服务端控制台命令不用打 /）
            cmdBox = new HintTextBox("输入命令，回车发送（不用打 /），如 say 你好 / op MAX");
            cmdBox.Box.BorderBrush = Palette.B(Palette.Line);
            cmdBox.Box.BorderThickness = new Thickness(1);
            cmdBox.Margin = new Thickness(0, 6, 0, 0);
            cmdBox.Box.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) SendConsoleCommand();
            };
            Grid.SetRow(cmdBox, 2);
            consoleGrid.Children.Add(cmdBox);
            Grid.SetRow(consoleGrid, 3);
            Children.Add(consoleGrid);
            // H史 UiMap：控制台区与命令输入框（read_server_console/send_server_command 剧本 LookAt 目标）
            UiMap.Register("server:console", delegate { return consoleBox; });
            UiMap.Register("server:cmd", delegate { return cmdBox; });

            // 本机地址探测一次；内存变动联动人数上限；名称/介绍失焦保存
            lanIp = ServerCore.GetLocalIPv4();
            ipv6 = ServerCore.GetGlobalIPv6();
            memBox.TextChanged += delegate { maxPlayersBox.Text = ComputeMaxPlayers().ToString(); };
            maxPlayersBox.Text = ComputeMaxPlayers().ToString();
            nameBox.Box.LostFocus += delegate { OnIdentityChanged(); };
            motdBox.Box.LostFocus += delegate { OnIdentityChanged(); };

            RefreshVersionList();
            RefreshInstanceList(null);
        }

        // ===== 数据刷新 =====

        void RefreshVersionList()
        {
            string keep = versionCombo.SelectedItem as string;
            versionCombo.Items.Clear();
            var vers = GameCore.GetInstalledVersions();
            vers.Sort(delegate(string a, string b) { return string.Compare(b, a, StringComparison.OrdinalIgnoreCase); });
            foreach (var v in vers) versionCombo.Items.Add(v);
            if (keep != null && vers.Contains(keep)) versionCombo.SelectedItem = keep;
        }

        void RefreshInstanceList(string selectName)
        {
            string keep = selectName;
            if (keep == null && instanceCombo.SelectedIndex >= 0 && instanceCombo.SelectedIndex < instList.Count)
                keep = instList[instanceCombo.SelectedIndex].Name;
            instanceCombo.Items.Clear();
            instList.Clear();
            int sel = -1;
            foreach (var n in ServerCore.GetInstances())
            {
                var inst = ServerCore.Load(n);
                int idx = instList.Count;
                instList.Add(inst);
                instanceCombo.Items.Add(inst.DisplayName.Length > 0
                    ? inst.DisplayName + "（" + inst.Name + "）"
                    : inst.Name);
                if (inst.Name == keep) sel = idx;
            }
            if (sel >= 0) instanceCombo.SelectedIndex = sel;
        }

        // ===== 下拉联动 =====

        void OnVersionSelected()
        {
            if (syncing) return;
            curVersion = versionCombo.SelectedItem as string;
            if (curVersion == null) { curInstance = null; curCfg = null; UpdateButton(); return; }
            if (ServerCore.InstanceExists(curVersion))
            {
                // 已有实例：自动切换到实例下拉项（触发实例加载）
                syncing = true;
                RefreshInstanceList(curVersion);
                syncing = false;
                LoadInstance(curVersion);
            }
            else
            {
                // 未创建实例：开服模式，内存填全局默认值，核心 Vanilla，名称/介绍清空
                curInstance = null;
                curCfg = null;
                syncing = true;
                instanceCombo.SelectedIndex = -1;
                memBox.Text = Cfg != null ? Cfg.MemoryMb.ToString() : "2048";
                coreCombo.SelectedIndex = 0;
                portBox.Text = ""; // 空 = 自动分配
                nameBox.Text = "";
                motdBox.Text = "";
                syncing = false;
            }
            UpdateButton();
        }

        void OnInstanceSelected()
        {
            if (syncing) return;
            int idx = instanceCombo.SelectedIndex;
            if (idx < 0 || idx >= instList.Count) { curInstance = null; curCfg = null; UpdateButton(); return; }
            LoadInstance(instList[idx].Name);
            UpdateButton();
        }

        void LoadInstance(string name)
        {
            curInstance = name;
            curCfg = ServerCore.Load(name);
            syncing = true;
            memBox.Text = curCfg.MemoryMb.ToString();
            portBox.Text = curCfg.Port.ToString();
            nameBox.Text = curCfg.DisplayName;
            motdBox.Text = curCfg.Motd;
            chkTunnel.SetChecked(curCfg.Tunnel, false);
            coreCombo.SelectedIndex = curCfg.CoreType == "paper" ? 1 : 0;
            // 版本框只读显示该实例版本（在列表中则选中）
            if (curCfg.GameVersion != null)
            {
                foreach (var it in versionCombo.Items)
                    if ((string)it == curCfg.GameVersion) { versionCombo.SelectedItem = it; break; }
            }
            syncing = false;
            consoleBox.Text = ServerManager.GetLogText(name);
            consoleBox.ScrollToEnd();
        }

        // ===== Huge 隧道 =====

        // 开关拨动：存进实例配置；服务器正在跑则立即开/关隧道
        void OnTunnelToggled()
        {
            if (syncing) return;
            if (curCfg != null)
            {
                curCfg.Tunnel = chkTunnel.IsChecked;
                ServerCore.Save(curCfg);
            }
            SyncTunnelState();
        }

        // 让隧道实际状态对齐「当前实例运行中 && 开关打开」
        void SyncTunnelState()
        {
            bool want = chkTunnel.IsChecked && curInstance != null && ServerManager.IsRunning(curInstance);
            if (want && !TunnelManager.IsHostActive)
            {
                TunnelManager.StartHost(curInstance, curCfg != null ? curCfg.Port : 25565);
                ShowRoomState("正在连接隧道服务器…", null);
            }
            else if (!want && TunnelManager.IsHostActive)
            {
                TunnelManager.StopHost();
                ShowRoomState(null, null);
            }
        }

        // 隧道状态文本：ROOM:XXXXXX 亮码；ERR:xxx 红字；null 全隐藏
        void ShowRoomState(string status, string room)
        {
            if (room != null)
            {
                roomCodeText.Text = room;
                roomCodeText.Foreground = Palette.B(Palette.Text);
                roomLabel.Visibility = Visibility.Visible;
                roomCodeText.Visibility = Visibility.Visible;
                roomCopyBtn.Visibility = Visibility.Visible;
                roomCodeText.ToolTip = null;
            }
            else if (status != null)
            {
                roomCodeText.Text = status;
                roomCodeText.Foreground = Palette.B(status.IndexOf("错") >= 0 || status.IndexOf("失败") >= 0 || status.IndexOf("断") >= 0
                    ? Palette.Red : Palette.Dim);
                roomCodeText.FontSize = 11;
                roomCodeText.FontWeight = FontWeights.Normal;
                roomLabel.Visibility = Visibility.Visible;
                roomCodeText.Visibility = Visibility.Visible;
                roomCopyBtn.Visibility = Visibility.Collapsed;
            }
            else
            {
                roomCodeText.FontSize = 15;
                roomCodeText.FontWeight = FontWeights.Bold;
                roomLabel.Visibility = Visibility.Collapsed;
                roomCodeText.Visibility = Visibility.Collapsed;
                roomCopyBtn.Visibility = Visibility.Collapsed;
            }
        }

        void OnTunnelStatus(string s)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(OnTunnelStatus), s); return; }
            if (s.StartsWith("ROOM:"))
            {
                roomCodeText.FontSize = 15;
                roomCodeText.FontWeight = FontWeights.Bold;
                ShowRoomState(null, s.Substring(5));
            }
            else if (s.StartsWith("ERR:"))
            {
                ShowRoomState("隧道错误：" + s.Substring(4), null);
            }
            else ShowRoomState(s, null);
        }

        void OnTunnelPeer(string action, string id)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string, string>(OnTunnelPeer), action, id); return; }
            if (action == "join") Toast("有玩家通过隧道加入了房间");
            else if (action == "direct") Toast("打洞成功，与该玩家建立直连");
            else Toast("一位隧道玩家离开了");
        }


        void UpdateButton()
        {
            if (busy) return; // 准备/关闭期间保持禁用文案
            if (curInstance != null)
            {
                if (ServerManager.IsRunning(curInstance))
                {
                    actionBtn.Text = "关闭服务器";
                    actionBtn.SetColors(Palette.Red, Palette.Lighten(Palette.Red, 0.12), Colors.White, Colors.White);
                    actionBtn.SetEnabled(true);
                }
                else
                {
                    actionBtn.Text = "启动服务器";
                    actionBtn.SetColors(Palette.Green, Palette.Lighten(Palette.Green, 0.12), Colors.White, Colors.White);
                    actionBtn.SetEnabled(true);
                }
            }
            else if (curVersion != null)
            {
                actionBtn.Text = "开服！！";
                var c = ThemeManager.CurrentColor;
                actionBtn.SetColors(c, Palette.Lighten(c, 0.15), Colors.White, Colors.White);
                actionBtn.SetEnabled(true);
            }
            else
            {
                actionBtn.Text = "请选择版本或实例";
                actionBtn.SetColors(Palette.Btn, Palette.BtnHover, Palette.Dim, Palette.Dim);
                actionBtn.SetEnabled(false);
            }
            UpdateStatus();
        }

        void SetBusy(string text)
        {
            busy = true;
            actionBtn.Text = text;
            actionBtn.SetColors(Palette.Btn, Palette.BtnHover, Palette.Dim, Palette.Dim);
            actionBtn.SetEnabled(false);
        }

        void UpdateStatus()
        {
            bool run = curInstance != null && ServerManager.IsRunning(curInstance);
            statusDot.Foreground = Palette.B(run ? Palette.Green : Palette.Dim);
            statusText.Text = run ? " 运行中" : " 已停止";
            if (curCfg != null)
            {
                addrFull = lanIp + ":" + curCfg.Port;
                addrText.Text = addrFull;
                addrText.Visibility = Visibility.Visible;
                copyBtn.Visibility = Visibility.Visible;
                if (ipv6 != null)
                {
                    addr6Full = "[" + ipv6 + "]:" + curCfg.Port;
                    addr6Text.Text = addr6Full;
                    addr6Text.ToolTip = addr6Full + "（朋友需支持 IPv6）";
                    addr6Text.Visibility = Visibility.Visible;
                    copyBtn6.Visibility = Visibility.Visible;
                }
                else
                {
                    addr6Text.Visibility = Visibility.Collapsed;
                    copyBtn6.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                addrText.Visibility = Visibility.Collapsed;
                copyBtn.Visibility = Visibility.Collapsed;
                addr6Text.Visibility = Visibility.Collapsed;
                copyBtn6.Visibility = Visibility.Collapsed;
            }
        }

        void CopyAddr(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            try { Clipboard.SetText(s); Toast("地址已复制：" + s); }
            catch { Toast("复制失败"); }
        }

        // 命令输入框回车：仅运行中的实例可发，发送成功清空输入
        bool SendConsoleCommand()
        {
            string cmd = cmdBox.Text.Trim();
            if (cmd.Length == 0) return false;
            if (curInstance == null || !ServerManager.IsRunning(curInstance))
            {
                Toast("服务器未运行，命令未发送");
                return false;
            }
            if (ServerManager.SendCommand(curInstance, cmd)) { cmdBox.Text = ""; return true; }
            Toast("命令发送失败");
            return false;
        }

        // ===== H史剧本开口（UI 线程调用） =====

        // 选中指定实例（触发真实下拉联动，控制台区同步显示该实例日志）；不存在返回 false
        public bool AiSelectInstance(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            for (int i = 0; i < instList.Count; i++)
            {
                if (instList[i].Name != name) continue;
                instanceCombo.SelectedIndex = i;   // 走真实 OnInstanceSelected → LoadInstance
                return curInstance == name;
            }
            return false;
        }

        // 走页面真实控制台输入链路：选中实例→填进命令框→SendConsoleCommand（写 stdin）
        public bool AiSendCommand(string name, string cmd)
        {
            if (!AiSelectInstance(name)) return false;
            if (string.IsNullOrEmpty(cmd)) return false;
            cmdBox.Text = cmd;
            return SendConsoleCommand();
        }

        void OnAction()
        {
            if (curInstance != null)
            {
                if (ServerManager.IsRunning(curInstance))
                {
                    // 关闭服务器
                    SetBusy("正在关闭…");
                    ServerManager.Stop(curInstance);
                }
                else
                {
                    // 启动已有实例（同步最新内存/核心/端口/人数/名称/介绍到配置）
                    int port = ResolvePort();
                    if (port < 0) return;
                    SyncIdentityToCfg();
                    curCfg.MemoryMb = ParseMem();
                    curCfg.CoreType = coreCombo.SelectedIndex == 1 ? "paper" : "vanilla";
                    curCfg.Port = port;
                    ServerCore.Save(curCfg);
                    SetBusy("正在准备…");
                    ServerManager.StartExisting(curCfg);
                }
            }
            else if (curVersion != null)
            {
                // 开服！！：创建实例并启动（端口框留空则自动分配）
                int port = ResolvePort();
                if (port < 0) return;
                var inst = new ServerInstance();
                inst.Name = curVersion;
                inst.GameVersion = curVersion;
                inst.CoreType = coreCombo.SelectedIndex == 1 ? "paper" : "vanilla";
                inst.MemoryMb = ParseMem();
                inst.Port = port;
                inst.MaxPlayers = ComputeMaxPlayers();
                inst.DisplayName = nameBox.Text.Trim();
                inst.Motd = motdBox.Text.Trim();
                inst.Tunnel = chkTunnel.IsChecked;
                inst.CreatedAt = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
                ServerCore.Save(inst);
                curInstance = inst.Name;
                curCfg = inst;
                portBox.Text = port.ToString(); // 回显实际使用的端口
                SetBusy("正在准备…");
                ServerManager.CreateAndStart(inst);
            }
        }

        // 把名称/介绍/人数写回当前实例配置（不触发保存）
        void SyncIdentityToCfg()
        {
            if (curCfg == null) return;
            curCfg.DisplayName = nameBox.Text.Trim();
            curCfg.Motd = motdBox.Text.Trim();
            curCfg.MaxPlayers = ComputeMaxPlayers();
        }

        // 名称/介绍失焦：有改动则存盘并刷新实例下拉显示名
        void OnIdentityChanged()
        {
            if (syncing || curCfg == null) return;
            string dn = nameBox.Text.Trim();
            string md = motdBox.Text.Trim();
            if (dn == curCfg.DisplayName && md == curCfg.Motd) return;
            curCfg.DisplayName = dn;
            curCfg.Motd = md;
            ServerCore.Save(curCfg);
            syncing = true;
            RefreshInstanceList(curInstance);
            syncing = false;
        }

        // 读取端口框：空=自动分配；非法输入/端口被占用则 Toast 并返回 -1
        int ResolvePort()
        {
            string t = portBox.Text.Trim();
            if (t.Length == 0) return ServerCore.FindFreePort(25565);
            int v;
            if (!int.TryParse(t, out v) || v < 1 || v > 65535)
            {
                Toast("端口无效，请输入 1-65535 的数字");
                return -1;
            }
            if (!ServerCore.IsPortFree(v))
            {
                Toast("端口 " + v + " 已被占用");
                return -1;
            }
            return v;
        }

        int ParseMem()
        {
            int v;
            if (!int.TryParse(memBox.Text.Trim(), out v)) v = Cfg != null ? Cfg.MemoryMb : 2048;
            if (v < 512) v = 512;
            if (v > 65536) v = 65536;
            return v;
        }

        // 人数上限 = 内存/100（2048MB → 20 人，与 vanilla 默认一致）
        int ComputeMaxPlayers()
        {
            int v = ParseMem() / 100;
            if (v < 1) v = 1;
            if (v > 1000) v = 1000;
            return v;
        }

        // ===== 页面生命周期：订阅/退订进程事件 =====

        public override void OnEnter()
        {
            // 同页重进会再进 OnEnter 而没有先 OnLeave（Navigate 同页捷径只刷新数据），
            // 先退订再订阅，保证幂等，否则每重进一次控制台输出就重复一遍
            ServerManager.Output -= OnServerOutput;
            ServerManager.StateChanged -= OnServerState;
            ServerManager.Output += OnServerOutput;
            ServerManager.StateChanged += OnServerState;
            TunnelManager.HostStatus -= OnTunnelStatus;
            TunnelManager.HostPeer -= OnTunnelPeer;
            TunnelManager.HostStatus += OnTunnelStatus;
            TunnelManager.HostPeer += OnTunnelPeer;
            RefreshVersionList();
            RefreshInstanceList(null);
            UpdateButton();
            // 页面回来时隧道可能已在后台运行：恢复房间码显示
            if (TunnelManager.IsHostActive && TunnelManager.HostRoom != null)
                ShowRoomState(null, TunnelManager.HostRoom);
        }

        public override void OnLeave()
        {
            ServerManager.Output -= OnServerOutput;
            ServerManager.StateChanged -= OnServerState;
            TunnelManager.HostStatus -= OnTunnelStatus;
            TunnelManager.HostPeer -= OnTunnelPeer;
        }

        void OnServerOutput(string name, string line)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string, string>(OnServerOutput), name, line); return; }
            if (name != curInstance) return;
            consoleBox.AppendText(line + "\n");
            consoleBox.ScrollToEnd();
        }

        void OnServerState(string name)
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action<string>(OnServerState), name); return; }
            busy = false;
            // 开服完成后实例列表出现新目录
            if (name == curInstance || name == curVersion) RefreshInstanceList(name);
            UpdateButton();
            if (name == curInstance)
            {
                consoleBox.Text = ServerManager.GetLogText(name);
                consoleBox.ScrollToEnd();
                SyncTunnelState(); // 服务器起/停 → 隧道跟随开关状态
            }
        }
    }
}
