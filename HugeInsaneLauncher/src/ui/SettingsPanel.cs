// HugeLauncher.Ui - 设置页（一拆二）：游戏设置（内存/优先级/Java 检测）+ 启动器设置（动画速度/主题配色/AI 史蒂夫）
// 全部即时生效并保存；共享基类提供滚动骨架与样式助手
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using HugeLauncher.Ai;

namespace HugeLauncher.Ui
{
    // 大瓷砖：圆角卡片（底色略浅于深背景/略深于亮背景），左上角主题色分类小字，内容加进 Body
    // helpId 非空时分类小字后跟一个「?」帮助点（瓷砖内不再铺与分类同名的白色标题）
    class SettingsTile : Border
    {
        public readonly StackPanel Body;
        public SettingsTile(string category, string helpId = null)
        {
            Background = Palette.B(Palette.Tile);
            CornerRadius = new CornerRadius(10);
            Padding = new Thickness(14, 10, 14, 12);
            Margin = new Thickness(0, 0, 0, 12);
            var sp = new StackPanel();
            if (!string.IsNullOrEmpty(category))
            {
                var cap = new TextBlock();
                cap.Text = category;
                cap.FontSize = 11;
                cap.FontWeight = FontWeights.SemiBold;
                var brush = new SolidColorBrush(ThemeManager.CurrentColor);
                ThemeManager.ColorChanged += delegate { brush.Color = ThemeManager.CurrentColor; };
                cap.Foreground = brush;
                FrameworkElement capEl = cap;
                if (!string.IsNullOrEmpty(helpId))
                {
                    var capRow = new StackPanel();
                    capRow.Orientation = Orientation.Horizontal;
                    capRow.Children.Add(cap);
                    var dot = HelpCatalog.Dot(helpId);
                    dot.Margin = new Thickness(5, 0, 0, 0);
                    capRow.Children.Add(dot);
                    capEl = capRow;
                }
                capEl.Margin = new Thickness(0, 0, 0, 4);
                sp.Children.Add(capEl);
            }
            Body = new StackPanel();
            sp.Children.Add(Body);
            Child = sp;
        }
    }

    abstract class SettingsBase : UserControl
    {
        protected Config cfg;
        protected bool loading = true;   // 初始化期间不触发保存
        protected StackPanel stack;

        protected SettingsBase(Config c)
        {
            cfg = c;
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            stack = new StackPanel();
            stack.Margin = new Thickness(16, 6, 16, 14);
            scroll.Content = stack;
            Content = scroll;
        }

        // 开一块大瓷砖，返回内容面板（后续控件往里面加）；helpId 非空则分类小字后跟「?」
        protected StackPanel AddTile(string category, string helpId = null)
        {
            var t = new SettingsTile(category, helpId);
            stack.Children.Add(t);
            return t.Body;
        }

        protected TextBlock MakeLabel(string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.Foreground = Palette.B(Palette.Text);
            t.FontSize = 13;
            t.FontWeight = FontWeights.SemiBold;
            t.Margin = new Thickness(0, 14, 0, 6);
            return t;
        }

        // 带「?」帮助钮的标签行（说明文字收进气泡，不再铺灰字）
        protected FrameworkElement MakeLabel(string text, string helpId)
        {
            var t = MakeLabel(text);
            t.Margin = new Thickness(0);
            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 14, 0, 6);
            row.Children.Add(t);
            row.Children.Add(HelpCatalog.Dot(helpId));
            return row;
        }

        protected TextBlock MakeValueText(string text)
        {
            var t = new TextBlock();
            t.Text = text;
            t.Foreground = Palette.B(Palette.Dim);
            t.FontSize = 11;
            t.VerticalAlignment = VerticalAlignment.Center;
            return t;
        }

        // 复选框行：MiniCheckBox + 说明文字
        protected MiniCheckBox AddCheckRow(StackPanel parent, string text, bool isChecked)
        {
            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            var chk = new MiniCheckBox();
            chk.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(chk);
            if (!string.IsNullOrEmpty(text))   // 空文本 = 裸开关，不留解释文字占位
            {
                var t = new TextBlock();
                t.Text = text;
                t.Foreground = Palette.B(Palette.Text);
                t.FontSize = 12;
                t.VerticalAlignment = VerticalAlignment.Center;
                t.Margin = new Thickness(8, 0, 0, 0);
                row.Children.Add(t);
            }
            parent.Children.Add(row);
            chk.SetChecked(isChecked, false);
            return chk;
        }

        // 深色输入框（黑底白字细边）
        protected static TextBox MakeInput(string text)
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
            box.Margin = new Thickness(0, 4, 0, 0);
            return box;
        }
    }

    // 游戏设置：内存分配 / 进程优先级 / 系统 Java 检测
    class SettingsGamePanel : SettingsBase
    {
        TextBlock memText;
        TextBlock gpuStatus;
        GlassSlider memSlider;

        public SettingsGamePanel(Config c)
            : base(c)
        {
            // ===== 瓷砖一：性能 =====
            var tilePerf = AddTile("性能");
            var lblMem = MakeLabel("内存分配", "setup:memory");
            lblMem.Margin = new Thickness(0, 2, 0, 6);
            tilePerf.Children.Add(lblMem);
            var memGrid = new Grid();
            memGrid.ColumnDefinitions.Add(UiHelpers.StarCol());
            memGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            memSlider = new GlassSlider();
            memSlider.Minimum = 1024;
            memSlider.Maximum = 16384;
            memSlider.TickFrequency = 512;
            memSlider.IsSnapToTickEnabled = true;
            memSlider.Value = cfg.MemoryMb;
            memSlider.VerticalAlignment = VerticalAlignment.Center;
            memText = MakeValueText(cfg.MemoryMb + " MB");
            memText.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(memText, 1);
            memGrid.Children.Add(memSlider);
            memGrid.Children.Add(memText);
            memSlider.ValueChanged += delegate
            {
                int v = (int)memSlider.Value;
                memText.Text = v + " MB";
                if (loading) return;
                cfg.MemoryMb = v;
                cfg.Save();
            };
            tilePerf.Children.Add(memGrid);

            // 进程优先级
            tilePerf.Children.Add(MakeLabel("进程优先级", "setup:priority"));
            var prioCombo = new DarkComboBox();
            prioCombo.Width = 160;
            prioCombo.HorizontalAlignment = HorizontalAlignment.Left;
            prioCombo.Items.Add("高");
            prioCombo.Items.Add("高于正常");
            prioCombo.Items.Add("正常");
            prioCombo.SelectedItem = cfg.Priority;
            if (prioCombo.SelectedIndex < 0) prioCombo.SelectedIndex = 2;
            prioCombo.SelectionChanged += delegate
            {
                if (loading) return;
                cfg.Priority = prioCombo.SelectedItem as string;
                cfg.Save();
            };
            tilePerf.Children.Add(prioCombo);

            // JVM GC 优化（全局默认；版本设置页可按版本覆盖）
            tilePerf.Children.Add(MakeLabel("垃圾回收（GC）优化", "setup:gc"));
            var chkGc = AddCheckRow(tilePerf, "启用 GC 优化参数（减少卡顿尖峰，推荐）", cfg.GcBoost);
            chkGc.Changed += delegate
            {
                if (loading) return;
                cfg.GcBoost = chkGc.IsChecked;
                cfg.Save();
            };

            // ===== 瓷砖二：显卡 =====
            var tileGpu = AddTile("显卡", "setup:gpu");
            var gpuRow = new StackPanel();
            gpuRow.Orientation = Orientation.Horizontal;
            var btnDgpu = new FlatButton("指定独显运行");
            btnDgpu.SetPadding(new Thickness(12, 6, 12, 6));
            btnDgpu.Click += delegate { SetGpuPreference(true); };
            var btnGpuReset = new FlatButton("恢复默认");
            btnGpuReset.SetPadding(new Thickness(12, 6, 12, 6));
            btnGpuReset.Margin = new Thickness(8, 0, 0, 0);
            btnGpuReset.Click += delegate { SetGpuPreference(false); };
            gpuRow.Children.Add(btnDgpu);
            gpuRow.Children.Add(btnGpuReset);
            tileGpu.Children.Add(gpuRow);
            // 状态只在「已指定独显」或设置失败时亮出；默认状态不铺灰字
            gpuStatus = MakeValueText("");
            gpuStatus.Margin = new Thickness(0, 4, 0, 0);
            gpuStatus.Visibility = Visibility.Collapsed;
            tileGpu.Children.Add(gpuStatus);
            RefreshGpuStatus();

            loading = false;
            // H史 UiMap：内存滑条（set_memory 剧本 LookAt 目标）
            UiMap.Register("setup:memory", delegate { return memSlider; });
        }

        // H史剧本用：真实改滑条（ValueChanged 事件会即时生效并存配置）
        public void AiSetMemory(int mb)
        {
            if (mb < memSlider.Minimum || mb > memSlider.Maximum) return;
            memSlider.Value = mb;
        }

        // Windows 图形首选项：GpuPreference=2 = 高性能（独显）；删除值 = 恢复系统默认
        // 多运行时并存（runtime/<组件>/bin/javaw.exe），全部都要写，否则老组件的运行时吃不到独显
        void SetGpuPreference(bool dedicated)
        {
            try
            {
                const string key = @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences";
                foreach (var javaw in AllJavawPaths())
                {
                    if (dedicated)
                        Microsoft.Win32.Registry.SetValue(key, javaw, "GpuPreference=2;");
                    else
                        using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                            @"Software\Microsoft\DirectX\UserGpuPreferences", true))
                        {
                            if (k != null) k.DeleteValue(javaw, false);
                        }
                }
                RefreshGpuStatus();
            }
            catch (Exception ex)
            {
                gpuStatus.Text = "设置失败: " + ex.Message;
                gpuStatus.Foreground = Palette.B(Palette.Red);
                gpuStatus.Visibility = Visibility.Visible;
            }
        }

        // 所有已安装运行时的 javaw.exe（旧 jre 目录 + 各组件目录）
        static System.Collections.Generic.List<string> AllJavawPaths()
        {
            var list = new System.Collections.Generic.List<string>();
            try
            {
                string rtRoot = System.IO.Path.Combine(Paths.McDir, "runtime");
                if (System.IO.Directory.Exists(rtRoot))
                    foreach (var dir in System.IO.Directory.GetDirectories(rtRoot))
                    {
                        string p = System.IO.Path.Combine(dir, "bin", "javaw.exe");
                        if (System.IO.File.Exists(p)) list.Add(p);
                    }
            }
            catch { }
            if (list.Count == 0) list.Add(Paths.JavawExe);   // 还没装运行时也给默认路径写上，装完即生效
            return list;
        }

        void RefreshGpuStatus()
        {
            bool on = false;
            try
            {
                // 任一运行时被标记即视为已开（SetGpuPreference 会写全部）
                foreach (var javaw in AllJavawPaths())
                {
                    object v = Microsoft.Win32.Registry.GetValue(
                        @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences", javaw, null);
                    if (v != null && v.ToString().IndexOf("GpuPreference=2") >= 0) { on = true; break; }
                }
            }
            catch { }
            gpuStatus.Text = on ? "当前：已指定独显（下次启动游戏生效）" : "";
            gpuStatus.Foreground = Palette.B(on ? Palette.Green : Palette.Dim);
            gpuStatus.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    // 启动器设置：动画速度 / 主题配色 / AI 史蒂夫
    class SettingsLauncherPanel : SettingsBase
    {
        TextBlock animText;
        readonly List<Ellipse> swatches = new List<Ellipse>();
        readonly List<string> swatchNames = new List<string>();
        MiniCheckBox chkAiEnable, chkAiAnim;
        MiniCheckBox chkLight, chkDayNight, chkUpdate, chkSlime;
        TextBlock updStatus;
        TextBox apiKeyBox, apiEndpointBox, mirrorBox;
        bool apiKeyDirty;   // 掩码显示后用户是否改过 key 输入框

        public SettingsLauncherPanel(Config c)
            : base(c)
        {
            // ===== 瓷砖一：外观 =====
            var tileLook = AddTile("外观");
            var lblAnim = MakeLabel("动画速度", "setup:anim");
            lblAnim.Margin = new Thickness(0, 2, 0, 6);
            tileLook.Children.Add(lblAnim);
            var animGrid = new Grid();
            animGrid.ColumnDefinitions.Add(UiHelpers.StarCol());
            animGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            var animSlider = new GlassSlider();
            animSlider.Minimum = 0;
            animSlider.Maximum = 2;
            animSlider.TickFrequency = 0.25;
            animSlider.IsSnapToTickEnabled = true;
            animSlider.Value = cfg.AnimSpeed;
            animSlider.VerticalAlignment = VerticalAlignment.Center;
            animText = MakeValueText(AnimText(cfg.AnimSpeed));
            animText.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(animText, 1);
            animGrid.Children.Add(animSlider);
            animGrid.Children.Add(animText);
            animSlider.ValueChanged += delegate
            {
                double v = animSlider.Value;
                animText.Text = AnimText(v);
                if (loading) return;
                cfg.AnimSpeed = v;
                ThemeManager.Speed = v;
                cfg.Save();
            };
            tileLook.Children.Add(animGrid);

            // 主题配色：6 个圆形色块
            tileLook.Children.Add(MakeLabel("主题配色"));
            var themeStack = new StackPanel();
            themeStack.Orientation = Orientation.Horizontal;
            themeStack.Margin = new Thickness(0, 4, 0, 0);
            AddSwatch(themeStack, "绿", Color.FromRgb(0x55, 0xC4, 0x5A));
            AddSwatch(themeStack, "蓝", Color.FromRgb(0x4F, 0x8F, 0xDF));
            AddSwatch(themeStack, "紫", Color.FromRgb(0x9B, 0x6B, 0xD3));
            AddSwatch(themeStack, "橙", Color.FromRgb(0xE0, 0x8A, 0x3C));
            AddSwatch(themeStack, "红", Color.FromRgb(0xD9, 0x53, 0x4F));
            AddSwatch(themeStack, "白", Color.FromRgb(0xE8, 0xE8, 0xE8));
            tileLook.Children.Add(themeStack);
            UpdateSwatchSelection();
            // H史 UiMap：主题色块行（set_theme 剧本 LookAt 目标）
            UiMap.Register("setup:theme", delegate { return themeStack; });

            // 液态玻璃（总开关本身就是玻璃开关：任何模式都是 iOS 脸，所见即所得）
            tileLook.Children.Add(MakeLabel("液态玻璃", "setup:glass"));
            var chkGlass = AddCheckRow(tileLook, "", cfg.LiquidGlass);
            chkGlass.ForceGlass = true;
            chkGlass.Changed += delegate
            {
                if (loading) return;
                cfg.LiquidGlass = chkGlass.IsChecked;
                cfg.Save();
                ThemeManager.ApplyGlass(chkGlass.IsChecked);
            };

            // 亮色模式（渐变罩切换，整套 UI 重建；输入框/下拉/滑条一并换肤）
            tileLook.Children.Add(MakeLabel("亮色模式", "setup:lightmode"));
            chkLight = AddCheckRow(tileLook, "", cfg.LightMode);
            chkLight.Changed += delegate
            {
                if (loading) return;
                var sw = ThemeManager.LightModeSwitch;
                if (sw != null) sw(chkLight.IsChecked);   // cfg.LightMode 由 SwitchSkin 落盘
            };

            // 自动调色：7:00-19:00 亮色；开着时上面的手动开关被托管置灰
            tileLook.Children.Add(MakeLabel("自动调色", "setup:daynight"));
            chkDayNight = AddCheckRow(tileLook, "", cfg.AutoDayNight);
            chkDayNight.Changed += delegate
            {
                if (loading) return;
                cfg.AutoDayNight = chkDayNight.IsChecked;
                cfg.Save();
                chkLight.SetEnabled(!cfg.AutoDayNight);
                if (cfg.AutoDayNight)
                {
                    var ev = ThemeManager.EvalAuto;
                    if (ev != null) ev();
                }
            };
            chkLight.SetEnabled(!cfg.AutoDayNight);

            // 流动史莱姆：壁纸取色的 metaball 色块背景；只留背景模糊可调
            // （中层固定 60% 默认值、前景固定 0——两项已按用户要求从设置里砍掉，画面不变）
            tileLook.Children.Add(MakeLabel("流动史莱姆", "setup:slime"));
            chkSlime = AddCheckRow(tileLook, "", cfg.SlimeOn);
            chkSlime.Changed += delegate
            {
                if (loading) return;
                cfg.SlimeOn = chkSlime.IsChecked;
                Slime.On = cfg.SlimeOn;
                cfg.Save();
                Slime.FireChanged();   // 色板半透明化 + 整套 UI 重建
            };
            AddSliderRow(tileLook, "背景模糊", 0, 20, 1, cfg.SlimeBlurBg,
                delegate(double v) { return ((int)v) + " px"; },
                delegate(double v)
                {
                    Slime.BlurBg = v;
                    cfg.SlimeBlurBg = v;
                    cfg.Save();
                    if (Slime.Current != null && Slime.Current.Effect is System.Windows.Media.Effects.BlurEffect)
                        ((System.Windows.Media.Effects.BlurEffect)Slime.Current.Effect).Radius = v;   // 即时生效，不重建
                });

            // ===== 瓷砖二：悬浮球（主窗最小化或游戏运行时的桌面简化面板） =====
            var tileBall = AddTile("悬浮球", "setup:floatball");
            var chkBall = AddCheckRow(tileBall, "", cfg.FloatBallOn);
            chkBall.Changed += delegate
            {
                if (loading) return;
                cfg.FloatBallOn = chkBall.IsChecked;
                cfg.Save();
                FloatBall.Sync(cfg);
            };

            // ===== 瓷砖三：更新 =====
            var tileUpd = AddTile("更新");
            var lblUpd = MakeLabel("自动更新", "setup:autoupdate");
            lblUpd.Margin = new Thickness(0, 2, 0, 6);
            tileUpd.Children.Add(lblUpd);
            chkUpdate = AddCheckRow(tileUpd, "", cfg.AutoUpdate);
            chkUpdate.Changed += delegate
            {
                if (loading) return;
                cfg.AutoUpdate = chkUpdate.IsChecked;
                cfg.Save();
            };
            var updRow = new StackPanel();
            updRow.Orientation = Orientation.Horizontal;
            updRow.Margin = new Thickness(0, 8, 0, 0);
            var btnCheckUpd = new FlatButton("检查更新");
            btnCheckUpd.SetPadding(new Thickness(12, 6, 12, 6));
            btnCheckUpd.Click += delegate { CheckUpdateManual(btnCheckUpd); };
            updStatus = MakeValueText("当前版本 " + MainWindow.Version);
            updStatus.Margin = new Thickness(12, 0, 0, 0);
            updRow.Children.Add(btnCheckUpd);
            updRow.Children.Add(updStatus);
            tileUpd.Children.Add(updRow);

            // Modrinth 镜像：留空 = 官方直连；填反代地址（如自建的 Cloudflare Worker）
            // ===== 瓷砖四：下载源 =====
            var tileDl = AddTile("下载源");
            var lblMirror = MakeLabel("Modrinth 镜像", "setup:mirror");
            lblMirror.Margin = new Thickness(0, 2, 0, 6);
            tileDl.Children.Add(lblMirror);
            mirrorBox = MakeInput(cfg.ModrinthMirror);
            mirrorBox.LostFocus += delegate { SaveMirror(); };
            mirrorBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; SaveMirror(); }
            };
            tileDl.Children.Add(mirrorBox);

            // ===== 瓷砖五：AI 史蒂夫 =====
            var tileAi = AddTile("AI 史蒂夫", "setup:ai");
            chkAiEnable = AddCheckRow(tileAi, "", cfg.AiEnabled);
            chkAiEnable.Changed += delegate
            {
                if (loading) return;
                cfg.AiEnabled = chkAiEnable.IsChecked;
                cfg.Save();
                UpdateAiControlState();
                HugeLauncher.Ai.AiSteve.NotifyConfigChanged();
            };

            tileAi.Children.Add(MakeValueText("API Key"));
            apiKeyBox = MakeInput(cfg.AiApiKey.Length > 0 ? MaskKey(cfg.AiApiKey) : "");
            apiKeyBox.TextChanged += delegate { if (!loading) apiKeyDirty = true; };
            apiKeyBox.LostFocus += delegate { SaveApiKey(); };
            apiKeyBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; SaveApiKey(); }
            };
            tileAi.Children.Add(apiKeyBox);

            var epLabel = MakeValueText("API 端点");
            epLabel.Margin = new Thickness(0, 10, 0, 0);
            tileAi.Children.Add(epLabel);
            apiEndpointBox = MakeInput(cfg.AiEndpoint);
            apiEndpointBox.LostFocus += delegate { SaveEndpoint(); };
            apiEndpointBox.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; SaveEndpoint(); }
            };
            tileAi.Children.Add(apiEndpointBox);

            var lblAnim2 = MakeLabel("拟人动画");
            lblAnim2.Margin = new Thickness(0, 10, 0, 0);
            tileAi.Children.Add(lblAnim2);
            chkAiAnim = AddCheckRow(tileAi, "", cfg.AiAnim);
            chkAiAnim.Margin = new Thickness(0, 2, 0, 0);
            chkAiAnim.Changed += delegate
            {
                if (loading) return;
                cfg.AiAnim = chkAiAnim.IsChecked;
                cfg.Save();
            };
            UpdateAiControlState();

            loading = false;
        }

        // 已保存的 key 只显示掩码（前 3 位 + • + 末 4 位），拍屏/旁观不泄露；
        // 用户没动过输入框（apiKeyDirty=false）时保存不覆盖原 key。
        static string MaskKey(string k)
        {
            if (string.IsNullOrEmpty(k)) return "";
            if (k.Length <= 7) return new string('•', Math.Max(4, k.Length));
            return k.Substring(0, 3) + new string('•', k.Length - 7) + k.Substring(k.Length - 4);
        }

        void SaveApiKey()
        {
            if (loading) return;
            if (!apiKeyDirty && cfg.AiApiKey.Length > 0) return;   // 框里是掩码，未修改
            cfg.AiApiKey = (apiKeyBox.Text ?? "").Trim();
            cfg.Save();
            apiKeyDirty = false;
            if (cfg.AiApiKey.Length > 0)
            {
                loading = true;
                apiKeyBox.Text = MaskKey(cfg.AiApiKey);
                loading = false;
            }
        }

        void SaveEndpoint()
        {
            if (loading) return;
            string v = (apiEndpointBox.Text ?? "").Trim();
            cfg.AiEndpoint = v.Length == 0 ? "https://api.deepseek.com/v1" : v;
            cfg.Save();
        }

        void SaveMirror()
        {
            if (loading) return;
            cfg.ModrinthMirror = (mirrorBox.Text ?? "").Trim();
            Modrinth.MirrorOverride = cfg.ModrinthMirror.Length == 0 ? null : cfg.ModrinthMirror;
            cfg.Save();
        }

        // 手动检查更新：结果写在按钮旁的状态行；发现新版走 MainWindow 的气泡确认通道
        void CheckUpdateManual(FlatButton btn)
        {
            btn.SetEnabled(false);
            updStatus.Text = "正在检查...";
            updStatus.Foreground = Palette.B(Palette.Dim);
            Updater.CheckAsync(MainWindow.Version,
                delegate(string newVer)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        btn.SetEnabled(true);
                        updStatus.Text = "发现新版本 " + newVer;
                        updStatus.Foreground = Palette.B(Palette.Green);
                        Updater.NotifyNewVersion(newVer);
                    }));
                },
                delegate
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        btn.SetEnabled(true);
                        updStatus.Text = "已是最新（" + MainWindow.Version + "）";
                        updStatus.Foreground = Palette.B(Palette.Dim);
                    }));
                },
                delegate(string err)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        btn.SetEnabled(true);
                        updStatus.Text = "检查失败：" + err;
                        updStatus.Foreground = Palette.B(Palette.Red);
                    }));
                });
        }

        // 勾选「开启AI史蒂夫」后，Key 输入框与拟人动画开关才可用（灰底禁用）
        void UpdateAiControlState()
        {
            bool on = chkAiEnable.IsChecked;
            apiKeyBox.IsEnabled = on;
            chkAiAnim.SetEnabled(on);
        }

        // 滑条行：标签小字 + GlassSlider + 右侧数值（样式照抄内存滑条）
        void AddSliderRow(StackPanel parent, string label, double min, double max, double tick,
            double val, Func<double, string> fmt, Action<double> onChanged)
        {
            var lb = MakeValueText(label);
            lb.Margin = new Thickness(0, 8, 0, 0);
            parent.Children.Add(lb);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(UiHelpers.StarCol());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            var slider = new GlassSlider();
            slider.Minimum = min;
            slider.Maximum = max;
            slider.TickFrequency = tick;
            slider.IsSnapToTickEnabled = tick >= 1;
            slider.Value = val;
            slider.VerticalAlignment = VerticalAlignment.Center;
            var txt = MakeValueText(fmt(val));
            txt.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(txt, 1);
            grid.Children.Add(slider);
            grid.Children.Add(txt);
            slider.ValueChanged += delegate
            {
                txt.Text = fmt(slider.Value);
                if (loading) return;
                onChanged(slider.Value);
            };
            parent.Children.Add(grid);
        }

        // 复选框行/深色输入框助手已上移到 SettingsBase

        static string AnimText(double v)
        {
            if (v <= 0) return "关闭";
            return v.ToString("0.00") + " x";
        }

        void AddSwatch(Panel parent, string name, Color color)
        {
            var e = new Ellipse();
            e.Width = 26;
            e.Height = 26;
            e.Fill = Palette.B(color);
            e.Margin = new Thickness(0, 0, 10, 0);
            e.Cursor = Cursors.Hand;
            e.ToolTip = name;
            e.StrokeThickness = 2;
            e.Stroke = new SolidColorBrush(Colors.Transparent);
            swatches.Add(e);
            swatchNames.Add(name);
            e.MouseLeftButtonUp += delegate
            {
                ThemeManager.ApplyTheme(name);
                cfg.Theme = name;
                cfg.Save();
                UpdateSwatchSelection();
            };
            parent.Children.Add(e);
        }

        void UpdateSwatchSelection()
        {
            for (int i = 0; i < swatches.Count; i++)
            {
                bool on = swatchNames[i] == ThemeManager.CurrentName;
                swatches[i].Stroke = on ? Palette.B(Palette.Text) : new SolidColorBrush(Colors.Transparent);
            }
        }
    }
}
