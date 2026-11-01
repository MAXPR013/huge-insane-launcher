// HugeLauncher.Ui.Pages - 版本设置·设置页（子代理 C）：每版本内存/进程优先级 + VersionSettings 持久化
using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    // 每版本启动覆盖设置。主窗口启动游戏时调 TryLoad 做每版本覆盖；未存过返回 false。
    // 存储：Paths.VersionDir(versionId) + "/hil.version.json"（无 BOM UTF8）
    static class VersionSettings
    {
        static string FileFor(string versionId)
        {
            return Path.Combine(Paths.VersionDir(versionId), "hil.version.json");
        }

        public static bool TryLoad(string versionId, out int memoryMb, out string priority, out bool? gcBoost)
        {
            memoryMb = 0;
            priority = null;
            gcBoost = null;
            try
            {
                if (string.IsNullOrEmpty(versionId)) return false;
                string f = FileFor(versionId);
                if (!File.Exists(f)) return false;
                var j = Json.Parse(File.ReadAllText(f, Encoding.UTF8));
                long mb = Json.Long(j, "memoryMb");
                if (mb <= 0 || mb > int.MaxValue) return false;
                memoryMb = (int)mb;
                priority = Json.Str(j, "priority");
                if (string.IsNullOrEmpty(priority)) priority = "正常";
                object g;
                if (j.TryGetValue("gcBoost", out g) && g is bool) gcBoost = (bool)g;
                return true;
            }
            catch { return false; }
        }

        public static void Save(string versionId, int memoryMb, string priority, bool gcBoost)
        {
            if (string.IsNullOrEmpty(versionId)) throw new ArgumentException("版本 id 为空");
            Directory.CreateDirectory(Paths.VersionDir(versionId));
            string json = "{\r\n  \"memoryMb\": " + memoryMb + ",\r\n  \"priority\": \"" + Esc(priority ?? "正常")
                + "\",\r\n  \"gcBoost\": " + (gcBoost ? "true" : "false") + "\r\n}\r\n";
            File.WriteAllText(FileFor(versionId), json, new UTF8Encoding(false));
        }

        static string Esc(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"");
        }
    }

    // 导航 key: instance:setup，NavParam = string 版本 id（可为 null）
    class PageInstanceSetup : PageBase
    {
        string versionId;
        GlassSlider memSlider;
        TextBox memBox;
        DarkComboBox priCombo;
        MiniCheckBox chkGc;
        bool memSync;
        Border tabHost;   // 三级标签宿主（OnEnter 拿到 versionId 后才构建）

        public PageInstanceSetup()
        {
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel();
            stack.Margin = new Thickness(16);
            scroll.Content = stack;
            Children.Add(scroll);

            // 顶部返回栏
            var back = new FlatButton("← 返回");
            back.SetPadding(new Thickness(10, 5, 10, 5));
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 0, 0, 10);
            back.Click += delegate { Navigator.Go("instance:list"); };
            stack.Children.Add(back);

            // 三级标签（概览/Mod/存档/设置）
            tabHost = new Border();
            stack.Children.Add(tabHost);

            // ---- 内存卡 ----
            var memCard = Card();
            var memStack = new StackPanel();
            memStack.Children.Add(Title("游戏内存", "ver:memory"));
            var row = new Grid();
            row.ColumnDefinitions.Add(UiHelpers.StarCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());
            memSlider = new GlassSlider();
            memSlider.Minimum = 512;
            memSlider.Maximum = 16384;
            memSlider.TickFrequency = 256;
            memSlider.IsSnapToTickEnabled = true;
            memSlider.VerticalAlignment = VerticalAlignment.Center;
            memSlider.Margin = new Thickness(0, 0, 10, 0);
            Grid.SetColumn(memSlider, 0);
            memBox = new TextBox();
            memBox.Width = 72;
            memBox.Background = Palette.B(Palette.Input);
            memBox.Foreground = Palette.B(Palette.Text);
            memBox.CaretBrush = Palette.B(Palette.Text);
            memBox.BorderBrush = Palette.B(Palette.Line);
            memBox.BorderThickness = new Thickness(1);
            memBox.Padding = new Thickness(6, 4, 6, 4);
            memBox.FontSize = 12;
            memBox.HorizontalContentAlignment = HorizontalAlignment.Center;
            memBox.VerticalContentAlignment = VerticalAlignment.Center;
            Grid.SetColumn(memBox, 1);
            var btnSmart = new FlatButton("智能");
            btnSmart.SetPadding(new Thickness(10, 4, 10, 4));
            btnSmart.Margin = new Thickness(8, 0, 0, 0);
            btnSmart.VerticalAlignment = VerticalAlignment.Center;
            btnSmart.Click += SmartRecommend;
            Grid.SetColumn(btnSmart, 2);
            row.Children.Add(memSlider);
            row.Children.Add(memBox);
            row.Children.Add(btnSmart);
            memStack.Children.Add(row);
            memCard.Child = memStack;
            stack.Children.Add(memCard);

            // ---- 进程优先级卡 ----
            var priCard = Card();
            var priStack = new StackPanel();
            priStack.Children.Add(Title("进程优先级", "setup:priority"));
            priCombo = new DarkComboBox();
            priCombo.Items.Add("高");
            priCombo.Items.Add("高于正常");
            priCombo.Items.Add("正常");
            priCombo.SelectedIndex = 2;
            priCombo.Width = 200;
            priCombo.HorizontalAlignment = HorizontalAlignment.Left;
            priStack.Children.Add(priCombo);
            priCard.Child = priStack;
            stack.Children.Add(priCard);

            // ---- 性能优化卡 ----
            var perfCard = Card();
            var perfStack = new StackPanel();
            perfStack.Children.Add(Title("性能优化", "ver:gc"));
            var gcRow = new StackPanel();
            gcRow.Orientation = Orientation.Horizontal;
            chkGc = new MiniCheckBox();
            chkGc.VerticalAlignment = VerticalAlignment.Center;
            var gcText = new TextBlock();
            gcText.Text = "GC 优化参数（减少卡顿尖峰，推荐开启）";
            gcText.Foreground = Palette.B(Palette.Text);
            gcText.FontSize = 12;
            gcText.VerticalAlignment = VerticalAlignment.Center;
            gcText.Margin = new Thickness(8, 0, 0, 0);
            gcRow.Children.Add(chkGc);
            gcRow.Children.Add(gcText);
            perfStack.Children.Add(gcRow);
            var btnPerf = new FlatButton("写入性能优先画面设置");
            btnPerf.SetPadding(new Thickness(12, 6, 12, 6));
            btnPerf.Margin = new Thickness(0, 12, 0, 0);
            btnPerf.Click += ApplyPerfOptions;
            var perfBtnRow = new StackPanel();
            perfBtnRow.Orientation = Orientation.Horizontal;
            perfBtnRow.HorizontalAlignment = HorizontalAlignment.Left;
            perfBtnRow.Children.Add(btnPerf);
            var perfDot = HelpCatalog.Dot("ver:perf");
            perfDot.Margin = new Thickness(6, 12, 0, 0);
            perfBtnRow.Children.Add(perfDot);
            perfStack.Children.Add(perfBtnRow);
            perfCard.Child = perfStack;
            stack.Children.Add(perfCard);

            // ---- 保存按钮 ----
            var save = new FlatButton("保存");
            save.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.2), Palette.Dark, Palette.Dark);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            save.Click += OnSave;
            stack.Children.Add(save);

            // 内存 Slider <-> TextBox 双向同步
            memSlider.ValueChanged += delegate
            {
                if (memSync) return;
                memSync = true;
                memBox.Text = ((int)memSlider.Value).ToString();
                memSync = false;
            };
            memBox.TextChanged += delegate
            {
                if (memSync) return;
                int v;
                if (int.TryParse(memBox.Text, out v) && v >= 512 && v <= 16384)
                {
                    memSync = true;
                    memSlider.Value = v;
                    memSync = false;
                }
            };
            memBox.LostFocus += delegate
            {
                memSync = true;
                memBox.Text = ((int)memSlider.Value).ToString();
                memSync = false;
            };
        }

        public override void OnEnter()
        {
            versionId = NavParam as string;
            tabHost.Child = InstanceTabs.Build("setup", versionId);
            int mb;
            string pri;
            bool? gcb;
            if (string.IsNullOrEmpty(versionId) || !VersionSettings.TryLoad(versionId, out mb, out pri, out gcb))
            {
                // 无存过的版本覆盖 → 跟随全局设置
                mb = Cfg != null ? Cfg.MemoryMb : 2048;
                pri = Cfg != null ? Cfg.Priority : "正常";
                gcb = null;
            }
            SetMem(mb);
            SelectPriority(pri);
            chkGc.SetChecked(gcb.HasValue ? gcb.Value : (Cfg != null ? Cfg.GcBoost : true), false);
        }

        void SetMem(int mb)
        {
            if (mb < 512) mb = 512;
            if (mb > 16384) mb = 16384;
            memSync = true;
            memSlider.Value = mb;
            memBox.Text = ((int)memSlider.Value).ToString();
            memSync = false;
        }

        void SelectPriority(string pri)
        {
            priCombo.SelectedIndex = pri == "高" ? 0 : (pri == "高于正常" ? 1 : 2);
        }

        void OnSave(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(versionId)) { Toast("未选择版本"); return; }
            int mb = (int)memSlider.Value;
            string pri = (priCombo.SelectedItem as string) ?? "正常";
            try
            {
                VersionSettings.Save(versionId, mb, pri, chkGc.IsChecked);
                if (Cfg != null)
                {
                    Cfg.MemoryMb = mb;
                    Cfg.Priority = pri;
                    Cfg.Save();
                }
                Toast("已保存");
            }
            catch (Exception ex)
            {
                Toast("保存失败: " + ex.Message);
            }
        }

        // 智能内存推荐：按物理内存与实例 mods 数量估算（给系统预留 3G）
        void SmartRecommend(object sender, EventArgs e)
        {
            long total = Env.TotalPhysMb();
            int mods = 0;
            try
            {
                if (!string.IsNullOrEmpty(versionId))
                {
                    string md = Path.Combine(Paths.InstanceDir(versionId), "mods");
                    if (Directory.Exists(md)) mods = Directory.GetFiles(md, "*.jar").Length;
                }
            }
            catch { }
            int rec = mods > 0 ? 4096 : 2048;
            if (mods > 30) rec = 6144;
            long cap = Math.Max(2048, total - 3072);
            if (rec > cap) rec = (int)(cap / 256) * 256;
            SetMem(rec);
            Toast("按" + (mods > 0 ? "模组数 " + mods + " 个" : "原版") + " + 物理内存 " + total + " MB，推荐 " + rec + " MB");
        }

        // 性能优先画面预设：合并写入该版本实例目录的 options.txt
        void ApplyPerfOptions(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(versionId)) { Toast("未选择版本"); return; }
            try
            {
                var rv = GameCore.Resolve(versionId);
                GameCore.ApplyPerformanceOptions(Paths.InstanceDir(versionId), rv.RootId);
                Toast("已写入性能优先画面设置（下次启动生效）");
            }
            catch (Exception ex)
            {
                Toast("写入失败: " + ex.Message);
            }
        }

        // ---------- 小控件工厂 ----------

        static Border Card()
        {
            var b = new Border();
            b.Background = Palette.B(Palette.Panel);
            b.CornerRadius = new CornerRadius(8);
            b.Padding = new Thickness(14);
            b.Margin = new Thickness(0, 0, 0, 10);
            return b;
        }

        static TextBlock Title(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 13;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Palette.B(Palette.Text);
            t.Margin = new Thickness(0, 0, 0, 8);
            return t;
        }

        // 带「?」帮助钮的卡标题行
        static FrameworkElement Title(string s, string helpId)
        {
            var t = Title(s);
            t.Margin = new Thickness(0);
            var row = new StackPanel();
            row.Orientation = Orientation.Horizontal;
            row.Margin = new Thickness(0, 0, 0, 8);
            row.Children.Add(t);
            row.Children.Add(HelpCatalog.Dot(helpId));
            return row;
        }

        static TextBlock DimText(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 11;
            t.Foreground = Palette.B(Palette.Dim);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }
    }
}
