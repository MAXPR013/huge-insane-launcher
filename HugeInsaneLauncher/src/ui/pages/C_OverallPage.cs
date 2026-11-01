// HugeLauncher.Ui.Pages - 版本设置·概览页（子代理 C）：版本信息卡 + 操作卡 + 危险卡（PCL 风）
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    // 导航 key: instance:overall，NavParam = string 版本 id（可为 null）
    class PageInstanceOverall : PageBase
    {
        string versionId;
        Image iconImg;
        TextBlock idText;
        TextBlock typeText;
        TextBlock statusText;
        Border tabHost;   // 三级标签宿主（OnEnter 拿到 versionId 后才构建）

        public PageInstanceOverall()
        {
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel();
            stack.Margin = new Thickness(16);
            scroll.Content = stack;
            Children.Add(scroll);

            // 顶部返回栏（回实例列表）
            var back = new FlatButton("← 返回");
            back.SetPadding(new Thickness(10, 5, 10, 5));
            back.HorizontalAlignment = HorizontalAlignment.Left;
            back.Margin = new Thickness(0, 0, 0, 10);
            back.Click += delegate { Navigator.Go("instance:list"); };
            stack.Children.Add(back);

            // ---- 三级标签（概览/Mod/存档/设置）----
            tabHost = new Border();
            stack.Children.Add(tabHost);

            // ---- 版本信息卡 ----
            var infoCard = Card();
            var g = new Grid();
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            iconImg = new Image();
            iconImg.Width = 56;
            iconImg.Height = 56;
            iconImg.Margin = new Thickness(0, 0, 12, 0);
            RenderOptions.SetBitmapScalingMode(iconImg, BitmapScalingMode.NearestNeighbor);
            Grid.SetColumn(iconImg, 0);
            var right = new StackPanel();
            right.VerticalAlignment = VerticalAlignment.Center;
            idText = new TextBlock();
            idText.FontSize = 18;
            idText.FontWeight = FontWeights.SemiBold;
            idText.Foreground = Palette.B(Palette.Text);
            typeText = new TextBlock();
            typeText.FontSize = 12;
            typeText.Foreground = Palette.B(Palette.Dim);
            typeText.Margin = new Thickness(0, 3, 0, 0);
            statusText = new TextBlock();
            statusText.FontSize = 12;
            statusText.Margin = new Thickness(0, 3, 0, 0);
            right.Children.Add(idText);
            right.Children.Add(typeText);
            right.Children.Add(statusText);
            Grid.SetColumn(right, 1);
            g.Children.Add(iconImg);
            g.Children.Add(right);
            infoCard.Child = g;
            stack.Children.Add(infoCard);

            // ---- 操作卡 ----
            var opCard = Card();
            var opStack = new StackPanel();
            opStack.Children.Add(Title("操作"));
            var btnRow = new WrapPanel();
            btnRow.Children.Add(MkBtn("启动游戏", true, delegate { if (HasId()) RequestLaunch(versionId); }));
            btnRow.Children.Add(MkBtn("导出", false, delegate { if (HasId()) Navigator.Go("instance:export", versionId); }));
            btnRow.Children.Add(MkBtn("打开文件夹", false, OnOpenFolder));
            opStack.Children.Add(btnRow);
            opCard.Child = opStack;
            stack.Children.Add(opCard);

            // ---- 危险卡 ----
            var dangerCard = Card();
            var dStack = new StackPanel();
            var dt = Title("危险操作");
            dt.Foreground = Palette.B(Palette.Red);
            dStack.Children.Add(dt);
            dStack.Children.Add(DimText("删除版本将移除其全部文件，此操作不可恢复。"));
            var delBtn = new FlatButton("删除版本");
            delBtn.SetColors(Palette.Btn, Palette.BtnHover, Palette.Red, Palette.Red);
            delBtn.HorizontalAlignment = HorizontalAlignment.Left;
            delBtn.Margin = new Thickness(0, 10, 0, 0);
            delBtn.Click += OnDelete;
            dStack.Children.Add(delBtn);
            dangerCard.Child = dStack;
            stack.Children.Add(dangerCard);

            RefreshInfo();
        }

        public override void OnEnter()
        {
            versionId = NavParam as string;
            tabHost.Child = InstanceTabs.Build("overall", versionId);
            RefreshInfo();
        }

        void RefreshInfo()
        {
            if (string.IsNullOrEmpty(versionId))
            {
                idText.Text = "未选择版本";
                typeText.Text = "";
                statusText.Text = "";
                iconImg.Source = null;
                return;
            }
            idText.Text = versionId;
            typeText.Text = "类型：" + LoaderOf(versionId);
            LoadIcon();
            bool ready = GameCore.IsVersionReady(versionId);
            statusText.Text = ready ? "已就绪" : "文件缺失";
            statusText.Foreground = Palette.B(ready ? Palette.Green : Palette.Red);
        }

        void LoadIcon()
        {
            iconImg.Source = null;
            string p = Paths.LauncherDir + IconPathFor(versionId);
            try
            {
                if (File.Exists(p))
                    iconImg.Source = new BitmapImage(new Uri(p, UriKind.Absolute));
            }
            catch { }
        }

        static string LoaderOf(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("fabric")) return "Fabric";
            if (s.Contains("neoforge")) return "NeoForge";
            if (s.Contains("forge")) return "Forge";
            return "原版";
        }

        static string IconPathFor(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("fabric")) return "/Images/Loaders/Fabric.png";
            if (s.Contains("neoforge")) return "/Images/Loaders/NeoForge.png";
            if (s.Contains("forge")) return "/Images/Blocks/Anvil.png";
            return "/Images/Blocks/Grass.png";
        }

        bool HasId()
        {
            if (string.IsNullOrEmpty(versionId)) { Toast("未选择版本"); return false; }
            return true;
        }

        void OnOpenFolder(object sender, EventArgs e)
        {
            if (!HasId()) return;
            string dir = Paths.VersionDir(versionId);
            if (!Directory.Exists(dir)) { Toast("版本文件夹不存在"); return; }
            try { System.Diagnostics.Process.Start("explorer.exe", dir); }
            catch (Exception ex) { Toast("打开失败: " + ex.Message); }
        }

        void OnDelete(object sender, EventArgs e)
        {
            if (!HasId()) return;
            if (MessageBox.Show("确定删除版本 " + versionId + "？此操作不可恢复。", "Huge Insane Launcher",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            try
            {
                Directory.Delete(Paths.VersionDir(versionId), true);
                Toast("已删除版本 " + versionId);
                Navigator.Go("launch");
            }
            catch (Exception ex)
            {
                Toast("删除失败: " + ex.Message);
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

        static TextBlock DimText(string s)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = 11;
            t.Foreground = Palette.B(Palette.Dim);
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        static FlatButton MkBtn(string text, bool accent, EventHandler onClick)
        {
            var b = new FlatButton(text);
            if (accent)
                b.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.2), Palette.Dark, Palette.Dark);
            b.Margin = new Thickness(0, 0, 8, 8);
            b.Click += onClick;
            return b;
        }
    }
}
