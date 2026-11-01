// HugeLauncher.Ui - 错误报告对话框：游戏崩溃分析结果 / 启动器异常详情（深色圆角模态）
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace HugeLauncher.Ui
{
    class CrashDialog : Window
    {
        string versionId, instanceName;
        CrashReport.CrashResult gameResult;
        string launcherDetail, launcherEnv;

        // 游戏崩溃
        public CrashDialog(string versionId, string instanceName, CrashReport.CrashResult result)
        {
            this.versionId = versionId;
            this.instanceName = instanceName;
            gameResult = result;
            Init("Minecraft 似乎崩溃了");
            BuildGame();
        }

        // 启动器异常
        public CrashDialog(string detail, string envInfo)
        {
            launcherDetail = detail;
            launcherEnv = envInfo;
            Init("HIL 出现错误");
            BuildLauncher();
        }

        void Init(string title)
        {
            Width = 640;
            Height = 540;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = new SolidColorBrush(Colors.Transparent);
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var rootBorder = new Border();
            rootBorder.CornerRadius = new CornerRadius(12);
            rootBorder.Background = Palette.WindowBg();
            rootBorder.BorderBrush = ThemeManager.Accent;
            rootBorder.BorderThickness = new Thickness(1);
            rootBorder.Margin = new Thickness(10);   // 无投影：DropShadowEffect 已全局撤除（核显帧率杀手）

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
            root.RowDefinitions.Add(UiHelpers.StarRow());
            root.RowDefinitions.Add(UiHelpers.AutoRow());

            // 标题栏
            var titleBar = new Grid();
            titleBar.MouseLeftButtonDown += delegate { try { DragMove(); } catch { } };
            var tt = new TextBlock();
            tt.Text = title;
            tt.Foreground = Palette.B(Palette.Text);
            tt.FontSize = 14;
            tt.FontWeight = FontWeights.SemiBold;
            tt.Margin = new Thickness(16, 0, 0, 0);
            tt.VerticalAlignment = VerticalAlignment.Center;
            titleBar.Children.Add(tt);
            var closeBtn = new FlatButton("✕");
            closeBtn.SetColors(Color.FromArgb(0, 0, 0, 0), Palette.Red, Palette.Text, Palette.Text);
            closeBtn.SetCornerRadius(new CornerRadius(0, 12, 0, 0));
            closeBtn.SetPadding(new Thickness(0));
            closeBtn.Width = 40;
            closeBtn.Height = 40;
            closeBtn.HorizontalAlignment = HorizontalAlignment.Right;
            closeBtn.Click += delegate { Close(); };
            titleBar.Children.Add(closeBtn);
            root.Children.Add(titleBar);

            contentHost = new SmoothScrollViewer();
            contentHost.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            contentHost.Margin = new Thickness(14, 0, 14, 0);
            Grid.SetRow(contentHost, 1);
            root.Children.Add(contentHost);

            btnRow = new StackPanel();
            btnRow.Orientation = Orientation.Horizontal;
            btnRow.HorizontalAlignment = HorizontalAlignment.Right;
            btnRow.Margin = new Thickness(14, 10, 14, 12);
            Grid.SetRow(btnRow, 2);
            root.Children.Add(btnRow);

            rootBorder.Child = root;
            Content = rootBorder;
        }

        SmoothScrollViewer contentHost;
        StackPanel btnRow;

        static TextBlock MakeText(string text, double size, Color color, bool bold)
        {
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = size;
            t.Foreground = Palette.B(color);
            if (bold) t.FontWeight = FontWeights.SemiBold;
            t.TextWrapping = TextWrapping.Wrap;
            return t;
        }

        FlatButton MakeBtn(string text, bool primary)
        {
            var b = new FlatButton(text);
            b.SetFontSize(12);
            b.SetPadding(new Thickness(14, 7, 14, 7));
            b.SetCornerRadius(new CornerRadius(7));
            b.Margin = new Thickness(8, 0, 0, 0);
            if (primary)
                b.SetColors(ThemeManager.CurrentColor, Palette.Lighten(ThemeManager.CurrentColor, 0.15), Colors.White, Colors.White);
            return b;
        }

        Border MakeCard(UIElement child, Thickness pad)
        {
            var c = new Border();
            c.Background = Palette.B(Palette.Panel);
            c.CornerRadius = new CornerRadius(8);
            c.Padding = pad;
            c.Margin = new Thickness(0, 0, 0, 10);
            c.Child = child;
            return c;
        }

        // ---------- 游戏崩溃视图 ----------

        void BuildGame()
        {
            var stack = new StackPanel();
            contentHost.Content = stack;

            stack.Children.Add(MakeCard(
                MakeText("游戏进程异常退出。HIL 已分析日志，可能的原因如下（按可能性排序，建议逐条尝试）：",
                    12, Palette.Dim, false), new Thickness(12, 10, 12, 10)));

            foreach (var reason in gameResult.Reasons)
            {
                var sp = new StackPanel();
                var lines = reason.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var t = MakeText(lines[i].TrimEnd('\r'), 12.5,
                        i == 0 ? Palette.Text : Palette.Dim, i == 0);
                    if (i > 0) t.Margin = new Thickness(0, 4, 0, 0);
                    sp.Children.Add(t);
                }
                stack.Children.Add(MakeCard(sp, new Thickness(12, 10, 12, 10)));
            }

            if (!string.IsNullOrEmpty(gameResult.Excerpt))
            {
                var sp = new StackPanel();
                sp.Children.Add(MakeText("日志片段", 12, Palette.Text, true));
                var box = new TextBox();
                box.IsReadOnly = true;
                box.Text = gameResult.Excerpt;
                box.FontFamily = new FontFamily("Consolas");
                box.FontSize = 9;
                box.Foreground = Palette.B(Palette.Dim);
                box.Background = Palette.B(Palette.Input);
                box.BorderThickness = new Thickness(0);
                box.TextWrapping = TextWrapping.Wrap;
                box.MaxHeight = 150;
                box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
                box.Margin = new Thickness(0, 6, 0, 0);
                sp.Children.Add(box);
                stack.Children.Add(MakeCard(sp, new Thickness(12, 10, 12, 10)));
            }

            var btnOk = MakeBtn("知道了", true);
            btnOk.Click += delegate { Close(); };
            btnRow.Children.Add(btnOk);
            if (gameResult.DirectFile != null)
            {
                var btnLog = MakeBtn("查看日志", false);
                btnLog.Click += delegate
                {
                    try { Process.Start("notepad.exe", "\"" + gameResult.DirectFile + "\""); } catch { }
                };
                btnRow.Children.Add(btnLog);
            }
            var btnExport = MakeBtn("导出错误报告", false);
            btnExport.Click += delegate { DoExport(); };
            btnRow.Children.Add(btnExport);
            var btnCopy = MakeBtn("复制分析结果", false);
            btnCopy.Click += delegate
            {
                try
                {
                    Clipboard.SetText(string.Join("\r\n\r\n", gameResult.Reasons.ToArray())
                        + "\r\n\r\n" + (gameResult.Excerpt ?? ""));
                }
                catch { }
            };
            btnRow.Children.Add(btnCopy);
        }

        void DoExport()
        {
            try
            {
                var dlg = new Microsoft.Win32.SaveFileDialog();
                dlg.Filter = "ZIP 压缩包|*.zip";
                dlg.FileName = "错误报告-" + DateTime.Now.ToString("yyyy-MM-dd HH.mm") + ".zip";
                if (dlg.ShowDialog() != true) return;
                string instDir = System.IO.Path.Combine(Paths.InstancesDir, instanceName);
                CrashReport.ExportReport(instDir, versionId, dlg.FileName);
                Process.Start("explorer.exe", "/select,\"" + dlg.FileName + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "导出失败：" + ex.Message, "HIL", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ---------- 启动器异常视图 ----------

        void BuildLauncher()
        {
            var stack = new StackPanel();
            contentHost.Content = stack;

            stack.Children.Add(MakeCard(
                MakeText("启动器遇到未处理的错误。你可以复制详情或导出日志，反馈给开发方。", 12, Palette.Dim, false),
                new Thickness(12, 10, 12, 10)));

            var detail = MakeText(launcherDetail ?? "未知错误", 11, Palette.Text, false);
            detail.FontFamily = new FontFamily("Consolas");
            stack.Children.Add(MakeCard(detail, new Thickness(12, 10, 12, 10)));

            if (!string.IsNullOrEmpty(launcherEnv))
                stack.Children.Add(MakeCard(MakeText(launcherEnv, 11, Palette.Dim, false),
                    new Thickness(12, 10, 12, 10)));

            var btnClose = MakeBtn("关闭", true);
            btnClose.Click += delegate { Close(); };
            btnRow.Children.Add(btnClose);
            var btnCopy = MakeBtn("复制详情", false);
            btnCopy.Click += delegate
            {
                try { Clipboard.SetText(launcherDetail + "\r\n\r\n" + launcherEnv); } catch { }
            };
            btnRow.Children.Add(btnCopy);
            var btnExport = MakeBtn("导出启动器日志", false);
            btnExport.Click += delegate
            {
                try
                {
                    var dlg = new Microsoft.Win32.SaveFileDialog();
                    dlg.Filter = "ZIP 压缩包|*.zip";
                    dlg.FileName = "HIL日志-" + DateTime.Now.ToString("yyyy-MM-dd HH.mm") + ".zip";
                    if (dlg.ShowDialog() != true) return;
                    CrashReport.ExportReport(null, null, dlg.FileName);
                    Process.Start("explorer.exe", "/select,\"" + dlg.FileName + "\"");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "导出失败：" + ex.Message, "HIL", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
            btnRow.Children.Add(btnExport);
        }
    }
}
