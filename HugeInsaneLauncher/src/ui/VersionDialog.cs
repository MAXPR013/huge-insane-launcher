// HugeLauncher.Ui - 版本选择对话框：模态无边框圆角 + 快照开关 + 加载圈 + 已安装/未安装行 + 下载确认页
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace HugeLauncher.Ui
{
    class VersionDialog : Window
    {
        public string SelectedId;
        public bool GoDownload;   // 用户点了「下载新版本」：主窗口导航到下载页

        Config cfg;
        HashSet<string> installed;
        DarkListBox list;
        UIElement listPage;
        StackPanel loadingPanel;
        TextBlock loadingText;
        RotateTransform loadingSpinTf;

        public VersionDialog(Config c)
        {
            cfg = c;
            Width = 560;
            Height = 480;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Colors.Transparent);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            BuildUi();
            Loaded += delegate
            {
                // 入场：淡入 + 上移回弹（PCL 弹窗配方）
                if (ThemeManager.AnimOn)
                {
                    var body = Content as UIElement;
                    if (body != null)
                    {
                        body.Opacity = 0;
                        Ani.Ts(body).Y = 12;
                        Ani.Start("dlgEnter",
                            Ani.AaOpacity(body, 1, 120, null, 60),
                            Ani.AaY(body, -12, 300, new AniEaseOutBack(Ani.Weak), 60));
                    }
                }
                StartLoad();
            };
        }

        void BuildUi()
        {
            var rootBorder = new Border();
            rootBorder.CornerRadius = new CornerRadius(12);
            rootBorder.Background = Palette.WindowBg();
            rootBorder.BorderBrush = ThemeManager.Accent;
            rootBorder.BorderThickness = new Thickness(1);
            rootBorder.Margin = new Thickness(10);   // 无投影：DropShadowEffect 已全局撤除（核显帧率杀手）

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            root.RowDefinitions.Add(UiHelpers.AutoRow());
            root.RowDefinitions.Add(UiHelpers.StarRow());

            // 标题行（可拖动）
            var titleBar = new Grid();
            titleBar.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            var titleText = new TextBlock();
            titleText.Text = "选择版本";
            titleText.Foreground = Palette.B(Palette.Text);
            titleText.FontSize = 14;
            titleText.FontWeight = FontWeights.SemiBold;
            titleText.Margin = new Thickness(16, 0, 0, 0);
            titleText.VerticalAlignment = VerticalAlignment.Center;
            titleBar.Children.Add(titleText);
            var closeBtn = new FlatButton("✕");
            closeBtn.SetColors(Color.FromArgb(0, 0, 0, 0), Palette.Red, Palette.Text, Palette.Text);
            closeBtn.SetCornerRadius(new CornerRadius(0, 12, 0, 0));
            closeBtn.SetPadding(new Thickness(0));
            closeBtn.Width = 40;
            closeBtn.Height = 40;
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Click += delegate { DialogResult = false; Close(); };
            titleBar.Children.Add(closeBtn);
            root.Children.Add(titleBar);

            // 工具行：右「下载新版本…」（跳转到下载页）
            var toolGrid = new Grid();
            toolGrid.Margin = new Thickness(14, 2, 14, 8);
            var btnGoDownload = new FlatButton("下载新版本…");
            btnGoDownload.SetFontSize(11);
            btnGoDownload.SetPadding(new Thickness(12, 5, 12, 5));
            Color acDl = ThemeManager.CurrentColor;
            btnGoDownload.SetColors(Color.FromArgb(0, 0, 0, 0), acDl, acDl, Palette.Dark);
            btnGoDownload.SetBorder(acDl);
            btnGoDownload.HorizontalAlignment = HorizontalAlignment.Right;
            btnGoDownload.Click += delegate { GoDownload = true; DialogResult = false; Close(); };
            toolGrid.Children.Add(btnGoDownload);
            Grid.SetRow(toolGrid, 1);
            root.Children.Add(toolGrid);

            // 内容区：列表 / 加载圈
            var contentGrid = new Grid();
            contentGrid.Margin = new Thickness(14, 0, 14, 12);
            Grid.SetRow(contentGrid, 2);

            list = new DarkListBox();
            listPage = list;

            loadingPanel = new StackPanel();
            loadingPanel.VerticalAlignment = VerticalAlignment.Center;
            var loadingSpin = new TextBlock();
            loadingSpin.Text = "◌";
            loadingSpin.Foreground = ThemeManager.Accent;
            loadingSpin.FontSize = 28;
            loadingSpin.HorizontalAlignment = HorizontalAlignment.Center;
            loadingSpin.RenderTransformOrigin = new Point(0.5, 0.5);
            loadingSpinTf = new RotateTransform();
            loadingSpin.RenderTransform = loadingSpinTf;
            if (ThemeManager.AnimOn)
            {
                var da = new DoubleAnimation(0, 360, ThemeManager.Dur(1000));
                da.RepeatBehavior = RepeatBehavior.Forever;
                loadingSpinTf.BeginAnimation(RotateTransform.AngleProperty, da);
            }
            loadingText = new TextBlock();
            loadingText.Text = "正在扫描已安装版本...";
            loadingText.Foreground = Palette.B(Palette.Dim);
            loadingText.FontSize = 11;
            loadingText.HorizontalAlignment = HorizontalAlignment.Center;
            loadingText.Margin = new Thickness(0, 8, 0, 0);
            loadingPanel.Children.Add(loadingSpin);
            loadingPanel.Children.Add(loadingText);

            contentGrid.Children.Add(listPage);
            contentGrid.Children.Add(loadingPanel);
            root.Children.Add(contentGrid);

            rootBorder.Child = root;
            Content = rootBorder;
        }

        // 后台线程扫描本地已安装版本（只列已安装，不联网）
        void StartLoad()
        {
            loadingPanel.Visibility = Visibility.Visible;
            listPage.Visibility = Visibility.Collapsed;
            var th = new Thread(new ThreadStart(delegate
            {
                try
                {
                    var inst = GameCore.GetInstalledVersions();
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        installed = new HashSet<string>();
                        foreach (var s in inst) installed.Add(s);
                        loadingPanel.Visibility = Visibility.Collapsed;
                        listPage.Visibility = Visibility.Visible;
                        RefillList();
                    }));
                }
                catch (Exception ex)
                {
                    string msg = ex.Message;
                    Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
                    {
                        loadingText.Text = "扫描已安装版本失败: " + msg;
                        if (loadingSpinTf != null) loadingSpinTf.BeginAnimation(RotateTransform.AngleProperty, null);
                    }));
                }
            }));
            th.IsBackground = true;
            th.Start();
        }

        // 只显示已安装版本（倒序，新版在上）；空则提示去下载
        void RefillList()
        {
            if (installed == null) return;
            list.Items.Clear();
            var ids = new List<string>(installed);
            ids.Sort(delegate(string a, string b) { return string.Compare(b, a, StringComparison.OrdinalIgnoreCase); });
            if (ids.Count == 0)
            {
                var empty = new TextBlock();
                empty.Text = "还没有已安装的版本，点右上角「下载新版本…」";
                empty.Foreground = Palette.B(Palette.Dim);
                empty.FontSize = 12;
                empty.Margin = new Thickness(4, 12, 0, 0);
                empty.IsHitTestVisible = false;
                list.Items.Add(empty);
                return;
            }
            foreach (var id in ids) list.Items.Add(MakeInstalledRow(id));
        }

        // 已安装版本行：点击直接选中启动
        UIElement MakeInstalledRow(string id)
        {
            var g = new Grid();
            g.Height = 34;
            g.ColumnDefinitions.Add(UiHelpers.StarCol());
            g.ColumnDefinitions.Add(UiHelpers.AutoCol());

            var idText = new TextBlock();
            idText.Text = id;
            idText.Foreground = Palette.B(Palette.Text);
            idText.FontSize = 12.5;
            idText.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(idText);

            var st = new TextBlock();
            st.Text = "已安装";
            st.Foreground = Palette.B(Palette.Dim);
            st.FontSize = 11;
            st.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(st, 1);
            g.Children.Add(st);

            g.Cursor = Cursors.Hand;
            g.MouseLeftButtonUp += delegate
            {
                SelectedId = id;
                DialogResult = true;
                Close();
            };
            return g;
        }
    }
}
