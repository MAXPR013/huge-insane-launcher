// HugeLauncher.Ui.Pages - 下载管理页（子代理 A）：总览左栏 + 任务卡片列表（PCL 下载管理仿制，深色主题）
// 导航 key: download:speed
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using HugeLauncher;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    class PageDownloadSpeed : PageBase
    {
        TextBlock _tbProgress, _tbSpeed, _tbRemaining;
        StackPanel _taskPanel;
        DispatcherTimer _timer;
        bool _active;
        bool _refreshPending;   // Changed 风暴合并：已有挂起的刷新就不再排队
        Dictionary<DownloadTaskItem, bool> _expandOverride = new Dictionary<DownloadTaskItem, bool>();

        public PageDownloadSpeed()
        {
            ColumnDefinitions.Add(UiHelpers.AutoCol());
            ColumnDefinitions.Add(UiHelpers.StarCol());

            // ---------- 左栏总览卡 ----------
            var leftCard = new Border();
            leftCard.Width = 220;
            leftCard.Margin = new Thickness(16, 16, 8, 16);
            leftCard.Background = Palette.B(Palette.Panel);
            leftCard.CornerRadius = new CornerRadius(8);
            leftCard.Padding = new Thickness(14);
            leftCard.VerticalAlignment = VerticalAlignment.Top;

            var left = new StackPanel();
            left.Children.Add(MakeLabel("总进度"));
            _tbProgress = MakeValue("100 %", 28);
            left.Children.Add(_tbProgress);
            left.Children.Add(MakeSeparator());
            left.Children.Add(MakeLabel("下载速度"));
            _tbSpeed = MakeValue("0.0 MB/s", 20);
            left.Children.Add(_tbSpeed);
            left.Children.Add(MakeSeparator());
            left.Children.Add(MakeLabel("剩余文件"));
            _tbRemaining = MakeValue("0", 20);
            left.Children.Add(_tbRemaining);
            left.Children.Add(MakeSeparator());

            var btnClear = new FlatButton("清空已完成");
            btnClear.SetPadding(new Thickness(10, 7, 10, 7));
            btnClear.Margin = new Thickness(0, 6, 0, 0);
            btnClear.Click += delegate
            {
                lock (TaskCenter.Tasks)
                    TaskCenter.Tasks.RemoveAll(delegate(DownloadTaskItem t) { return t.Finished; });
                Refresh();
            };
            left.Children.Add(btnClear);
            leftCard.Child = left;
            Children.Add(leftCard);

            // ---------- 右侧任务列表 ----------
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _taskPanel = new StackPanel();
            _taskPanel.Margin = new Thickness(8, 16, 16, 16);
            scroll.Content = _taskPanel;
            Grid.SetColumn(scroll, 1);
            Children.Add(scroll);

            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += delegate { Refresh(); };
        }

        static TextBlock MakeLabel(string text)
        {
            var tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = 12;
            tb.Foreground = Palette.B(Palette.Dim);
            tb.Margin = new Thickness(0, 4, 0, 2);
            return tb;
        }

        static TextBlock MakeValue(string text, double size)
        {
            var tb = new TextBlock();
            tb.Text = text;
            tb.FontSize = size;
            tb.FontWeight = FontWeights.SemiBold;
            tb.Foreground = Palette.B(Palette.Text);
            return tb;
        }

        static Border MakeSeparator()
        {
            var b = new Border();
            b.Height = 1;
            b.Background = Palette.B(Palette.Line);
            b.Margin = new Thickness(0, 12, 0, 8);
            return b;
        }

        // ---------- 生命周期 ----------

        public override void OnEnter()
        {
            _active = true;
            // 同页重进会再进 OnEnter 而没有先 OnLeave，先退订再订阅保证幂等
            TaskCenter.Changed -= OnTasksChanged;
            TaskCenter.Changed += OnTasksChanged;
            _timer.Start();
            Refresh();
        }

        public override void OnLeave()
        {
            _active = false;
            TaskCenter.Changed -= OnTasksChanged;
            _timer.Stop();
        }

        void OnTasksChanged()
        {
            if (!_active || _refreshPending) return;
            _refreshPending = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate
            {
                _refreshPending = false;
                if (_active) Refresh();
            }));
        }

        // ---------- 刷新 ----------

        void Refresh()
        {
            double p, mbps;
            int remaining;
            TaskCenter.Overall(out p, out mbps, out remaining);
            _tbProgress.Text = ((int)Math.Floor(p * 100)) + " %";
            _tbSpeed.Text = mbps.ToString("F1") + " MB/s";
            _tbRemaining.Text = remaining.ToString();

            var snapshot = new List<DownloadTaskItem>();
            lock (TaskCenter.Tasks) snapshot.AddRange(TaskCenter.Tasks);

            _taskPanel.Children.Clear();
            if (snapshot.Count == 0)
            {
                var empty = new TextBlock();
                empty.Text = "暂无下载任务";
                empty.FontSize = 13;
                empty.Foreground = Palette.B(Palette.Dim);
                empty.HorizontalAlignment = HorizontalAlignment.Center;
                empty.Margin = new Thickness(0, 60, 0, 0);
                _taskPanel.Children.Add(empty);
                return;
            }
            foreach (var item in snapshot) _taskPanel.Children.Add(BuildTaskCard(item));
        }

        // 在 lock(item) 下拷贝的字段快照
        class StepSnap
        {
            public string Name;
            public long Cur, Total;
            public double Mbps;
            public int State;
            public string Error;
        }

        UIElement BuildTaskCard(DownloadTaskItem item)
        {
            string name, failMsg;
            bool finished, failed;
            double progress;
            var steps = new List<StepSnap>();
            lock (item)
            {
                name = item.Name;
                finished = item.Finished;
                failed = item.Failed;
                failMsg = item.FailMessage;
                progress = item.Progress;
                foreach (var s in item.Steps)
                    steps.Add(new StepSnap { Name = s.Name, Cur = s.Cur, Total = s.Total, Mbps = s.Mbps, State = s.State, Error = s.Error });
            }

            bool expanded = !finished;
            bool ov;
            if (_expandOverride.TryGetValue(item, out ov)) expanded = ov;

            var card = new Border();
            card.Background = Palette.B(Palette.Panel);
            card.CornerRadius = new CornerRadius(8);
            card.Padding = new Thickness(14);
            card.Margin = new Thickness(0, 0, 0, 10);

            var sp = new StackPanel();

            // 标题行（点击展开/收起步骤）
            var head = new Grid();
            head.Cursor = Cursors.Hand;
            head.ColumnDefinitions.Add(UiHelpers.StarCol());
            head.ColumnDefinitions.Add(UiHelpers.AutoCol());
            var tbName = new TextBlock();
            tbName.Text = name;
            tbName.FontSize = 13;
            tbName.FontWeight = FontWeights.SemiBold;
            tbName.Foreground = Palette.B(Palette.Text);
            tbName.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(tbName);

            var tbState = new TextBlock();
            tbState.FontSize = 12;
            tbState.VerticalAlignment = VerticalAlignment.Center;
            if (failed) { tbState.Text = "失败"; tbState.Foreground = Palette.B(Palette.Red); }
            else if (finished) { tbState.Text = "已完成"; tbState.Foreground = Palette.B(Palette.Green); }
            else { tbState.Text = ((int)Math.Floor(progress * 100)) + " %"; tbState.Foreground = ThemeManager.Accent; }
            Grid.SetColumn(tbState, 1);
            head.Children.Add(tbState);
            sp.Children.Add(head);

            // 总进度条
            var bar = new ProgressBar();
            bar.Minimum = 0;
            bar.Maximum = 100;
            bar.Value = Math.Max(0, Math.Min(100, progress * 100));
            bar.Height = 6;
            bar.Margin = new Thickness(0, 8, 0, 4);
            bar.Foreground = ThemeManager.Accent;
            bar.Background = Palette.B(Palette.Dark);
            sp.Children.Add(bar);

            // 失败原因
            if (failed && !string.IsNullOrEmpty(failMsg))
            {
                var tbFail = new TextBlock();
                tbFail.Text = failMsg;
                tbFail.FontSize = 11;
                tbFail.Foreground = Palette.B(Palette.Red);
                tbFail.TextWrapping = TextWrapping.Wrap;
                tbFail.Margin = new Thickness(0, 2, 0, 0);
                sp.Children.Add(tbFail);
            }

            // 步骤明细（展开时）
            var stepsPanel = new StackPanel();
            stepsPanel.Margin = new Thickness(0, 6, 0, 0);
            foreach (var s in steps) stepsPanel.Children.Add(BuildStepRow(s));
            stepsPanel.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            sp.Children.Add(stepsPanel);

            head.MouseLeftButtonUp += delegate
            {
                bool now = stepsPanel.Visibility == Visibility.Visible;
                stepsPanel.Visibility = now ? Visibility.Collapsed : Visibility.Visible;
                _expandOverride[item] = !now;
            };

            card.Child = sp;
            return card;
        }

        UIElement BuildStepRow(StepSnap s)
        {
            var row = new Grid();
            row.Margin = new Thickness(0, 2, 0, 2);
            row.ColumnDefinitions.Add(UiHelpers.StarCol());
            row.ColumnDefinitions.Add(UiHelpers.AutoCol());

            var name = new TextBlock();
            name.Text = string.IsNullOrEmpty(s.Name) ? "下载步骤" : s.Name;
            name.FontSize = 12;
            name.Foreground = Palette.B(Palette.Text);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);

            var state = new TextBlock();
            state.FontSize = 11;
            state.VerticalAlignment = VerticalAlignment.Center;
            switch (s.State)
            {
                case 0:
                    state.Text = "等待";
                    state.Foreground = Palette.B(Palette.Dim);
                    break;
                case 1:
                    string txt = "进行中 " + s.Cur + "/" + (s.Total > 0 ? s.Total.ToString() : "?");
                    if (s.Mbps > 0) txt += " · " + s.Mbps.ToString("F1") + " MB/s";
                    state.Text = txt;
                    state.Foreground = ThemeManager.Accent;
                    break;
                case 2:
                    state.Text = "已完成";
                    state.Foreground = Palette.B(Palette.Green);
                    break;
                default:
                    state.Text = "失败：" + (string.IsNullOrEmpty(s.Error) ? "未知错误" : s.Error);
                    state.Foreground = Palette.B(Palette.Red);
                    break;
            }
            Grid.SetColumn(state, 1);
            row.Children.Add(state);

            // 进行中步骤的进度条
            if (s.State == 1)
            {
                var wrap = new StackPanel();
                wrap.Children.Add(row);
                var bar = new ProgressBar();
                bar.Height = 4;
                bar.Margin = new Thickness(0, 2, 0, 4);
                bar.Foreground = ThemeManager.Accent;
                bar.Background = Palette.B(Palette.Dark);
                if (s.Total > 0)
                {
                    bar.Minimum = 0;
                    bar.Maximum = 100;
                    bar.Value = Math.Max(0, Math.Min(100, 100.0 * s.Cur / s.Total));
                }
                else
                {
                    bar.IsIndeterminate = true;
                }
                wrap.Children.Add(bar);
                return wrap;
            }
            return row;
        }
    }
}
