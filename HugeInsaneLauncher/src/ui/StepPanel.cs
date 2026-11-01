// HugeLauncher.Ui - 步骤面板：状态图标（○/旋转/✓/✗）+ 步骤名 + 速度/进度 + 平滑进度条
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HugeLauncher.Ui
{
    class StepPanel : UserControl
    {
        class Row
        {
            public Border Root;
            public TextBlock Icon;
            public TextBlock Name;
            public TextBlock Stats;
            public ProgressBar Bar;
            public RotateTransform Spin;
            public bool Spinning;
        }

        List<Row> rows = new List<Row>();
        StackPanel stack;

        public StepPanel()
        {
            var sv = new SmoothScrollViewer();
            sv.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            sv.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            stack = new StackPanel();
            sv.Content = stack;
            Content = sv;
        }

        public void Reset()
        {
            foreach (var r in rows) StopSpin(r);
            stack.Children.Clear();
            rows.Clear();
        }

        // StepStart 时若 index 超出现有行数则新增行
        Row EnsureRow(int idx)
        {
            while (rows.Count <= idx) AddRow();
            return rows[idx];
        }

        void AddRow()
        {
            var r = new Row();
            r.Icon = new TextBlock();
            r.Icon.Text = "○";
            r.Icon.Foreground = Palette.B(Palette.Dim);
            r.Icon.FontSize = 13;
            r.Icon.Width = 22;
            r.Icon.TextAlignment = TextAlignment.Center;
            r.Icon.VerticalAlignment = VerticalAlignment.Center;
            r.Icon.RenderTransformOrigin = new Point(0.5, 0.5);

            r.Name = new TextBlock();
            r.Name.Text = "步骤 " + rows.Count;
            r.Name.Foreground = Palette.B(Palette.Text);
            r.Name.FontSize = 12.5;
            r.Name.VerticalAlignment = VerticalAlignment.Center;

            r.Stats = new TextBlock();
            r.Stats.Foreground = Palette.B(Palette.Dim);
            r.Stats.FontSize = 10;
            r.Stats.VerticalAlignment = VerticalAlignment.Center;
            r.Stats.HorizontalAlignment = HorizontalAlignment.Right;

            var top = new Grid();
            top.ColumnDefinitions.Add(UiHelpers.AutoCol());
            top.ColumnDefinitions.Add(UiHelpers.StarCol());
            top.ColumnDefinitions.Add(UiHelpers.AutoCol());
            Grid.SetColumn(r.Name, 1);
            Grid.SetColumn(r.Stats, 2);
            top.Children.Add(r.Icon);
            top.Children.Add(r.Name);
            top.Children.Add(r.Stats);

            r.Bar = new ProgressBar();
            r.Bar.Minimum = 0;
            r.Bar.Maximum = 100;
            r.Bar.Height = 5;
            r.Bar.Margin = new Thickness(0, 6, 0, 0);
            r.Bar.Background = Palette.B(Palette.Input);
            r.Bar.Foreground = ThemeManager.Accent;
            r.Bar.BorderThickness = new Thickness(0);

            var body = new StackPanel();
            body.Children.Add(top);
            body.Children.Add(r.Bar);

            r.Root = new Border();
            r.Root.Background = Palette.B(Palette.Panel);
            r.Root.CornerRadius = new CornerRadius(6);
            r.Root.Padding = new Thickness(10, 8, 10, 10);
            r.Root.Margin = new Thickness(0, 0, 0, 6);
            r.Root.Child = body;

            rows.Add(r);
            stack.Children.Add(r.Root);
        }

        // 必须在 UI 线程调用；Log 事件由主窗口日志区处理，这里忽略
        public void Handle(StepEvent e)
        {
            switch (e.EventKind)
            {
                case StepEvent.Kind.StepStart:
                    {
                        var r = EnsureRow(e.StepIndex);
                        if (e.StepName.Length > 0) r.Name.Text = e.StepName;
                        r.Stats.Text = "";
                        StartSpin(r);
                        break;
                    }
                case StepEvent.Kind.StepProgress:
                    {
                        var r = EnsureRow(e.StepIndex);
                        StartSpin(r);
                        if (e.Total > 0)
                        {
                            r.Bar.IsIndeterminate = false;
                            AnimateBar(r, 100.0 * e.Current / e.Total);
                            r.Stats.Text = e.Current + "/" + e.Total + "  " + e.MbPerSec.ToString("F1") + " MB/s";
                        }
                        else
                        {
                            r.Bar.IsIndeterminate = true;
                            r.Stats.Text = "处理中...";
                        }
                        break;
                    }
                case StepEvent.Kind.StepDone:
                    {
                        var r = EnsureRow(e.StepIndex);
                        StopSpin(r);
                        if (e.Success)
                        {
                            r.Icon.Text = "✓";
                            r.Icon.Foreground = Palette.B(Palette.Green);
                            r.Bar.IsIndeterminate = false;
                            AnimateBar(r, 100);
                            r.Stats.Text = "完成";
                        }
                        else
                        {
                            r.Icon.Text = "✗";
                            r.Icon.Foreground = Palette.B(Palette.Red);
                            r.Bar.IsIndeterminate = false;
                            r.Bar.Foreground = Palette.B(Palette.Red);
                            r.Stats.Text = "失败";
                            r.Root.ToolTip = e.Message;
                        }
                        break;
                    }
            }
        }

        // 进度条平滑过渡到新值
        void AnimateBar(Row r, double v)
        {
            if (v < 0) v = 0;
            if (v > 100) v = 100;
            if (ThemeManager.AnimOn)
            {
                r.Bar.BeginAnimation(ProgressBar.ValueProperty, new DoubleAnimation(v, ThemeManager.Dur(250)));
            }
            else
            {
                r.Bar.BeginAnimation(ProgressBar.ValueProperty, null);
                r.Bar.Value = v;
            }
        }

        void StartSpin(Row r)
        {
            if (r.Spinning) return;
            r.Spinning = true;
            r.Icon.Text = "◌";
            r.Icon.Foreground = ThemeManager.Accent;
            if (!ThemeManager.AnimOn) return;
            if (r.Spin == null)
            {
                r.Spin = new RotateTransform();
                r.Icon.RenderTransform = r.Spin;
            }
            var da = new DoubleAnimation(0, 360, ThemeManager.Dur(900));
            da.RepeatBehavior = RepeatBehavior.Forever;
            r.Spin.BeginAnimation(RotateTransform.AngleProperty, da);
        }

        void StopSpin(Row r)
        {
            r.Spinning = false;
            if (r.Spin != null)
            {
                r.Spin.BeginAnimation(RotateTransform.AngleProperty, null);
                r.Spin.Angle = 0;
            }
        }
    }
}
