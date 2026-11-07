// HugeLauncher.Ui - 小部件仪表盘引擎（首页 / 找乐子共用）
// 12 列固定网格，行高按可用高度均分；首页禁滚动，所有卡片必须排进一屏。
// 编辑模式：拖动换格、右下角缩放、× 移除、＋ 添加；布局存 dashboard.dat（key;col;row;w;h 每行一条）。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HugeLauncher.Ui
{
    // 尺寸钳制容器：按可用空间测量子元素，但不把子元素的期望尺寸向上传。
    // 否则跨满 12 列的卡片（长标题/日期）会把星号列撑宽，整页横向溢出。
    class ClampPanel : Decorator
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            if (Child != null)
            {
                double w = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;
                double h = double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height;
                Child.Measure(new Size(w, h));
            }
            return new Size(0, 0);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            if (Child != null) Child.Arrange(new Rect(finalSize));
            return finalSize;
        }
    }

    class WidgetDef
    {
        public string Key;
        public string Title;
        public int W, H;          // 默认格数
        public int MinW, MinH;    // 最小格数
        public Func<UIElement> Build;
    }

    class WidgetSlot
    {
        public WidgetDef Def;
        public int Col, Row, W, H;
        public Border Host;
        public Border Chrome;     // 编辑态覆盖层
        public TextBlock TitleText;
    }

    class Dashboard : Grid
    {
        public const int Cols = 12;
        public const int Rows = 6;
        const double Gap = 10;

        readonly Dictionary<string, WidgetDef> defs = new Dictionary<string, WidgetDef>();
        readonly List<WidgetSlot> slots = new List<WidgetSlot>();
        readonly string storePath;
        bool edit;

        WidgetSlot dragSlot;
        Point dragStart;
        int dragCol, dragRow, dragW, dragH;
        bool resizing;
        TranslateTransform dragTf;   // 拖动期间的残余平移（松手补间归零 = 平滑吸附）
        Point ptrV;                  // 拖动时指针在视口里的位置（边缘自动滚动按它算）
        readonly DispatcherTimer autoTimer;
        Dictionary<WidgetSlot, Point> snap;   // 拖动开始时的布局快照（推挤排序的基准）
        int lastC = int.MinValue, lastR = int.MinValue;

        // 可滚动模式（找乐子）：行高固定、行数按「所有小部件都拉到最高并排成一列」算；
        // 首页保持一屏 6 行不滚动
        public bool Tall;
        public double TallRowHeight = 78;
        public ScrollViewer Scroller;   // 拖动到上下边缘时自动滚动

        public Action Changed;   // 布局变更回调（页面存盘/刷新按钮态）

        public Dashboard(string storePath)
        {
            this.storePath = storePath;
            for (int i = 0; i < Cols; i++) ColumnDefinitions.Add(UiHelpers.StarCol());
            for (int i = 0; i < Rows; i++) RowDefinitions.Add(UiHelpers.StarRow());
            ClipToBounds = true;
            autoTimer = new DispatcherTimer();
            autoTimer.Interval = TimeSpan.FromMilliseconds(16);
            autoTimer.Tick += AutoScrollTick;
        }

        // 切到可滚动模式：行高改为固定像素，行数 = 每个部件都能拉到最高（Rows 行）时的总和
        public void EnableTall(double rowHeight)
        {
            Tall = true;
            TallRowHeight = rowHeight;
            ClipToBounds = false;
            RebuildRows();
        }

        int RowsNow { get { return RowDefinitions.Count > 0 ? RowDefinitions.Count : Rows; } }

        void RebuildRows()
        {
            RowDefinitions.Clear();
            if (Tall)
            {
                int sum = 0;
                for (int i = 0; i < slots.Count; i++) sum += Rows;   // 每个部件最高 Rows 行
                int need = Math.Max(Rows, sum);
                for (int i = 0; i < need; i++)
                    RowDefinitions.Add(new RowDefinition { Height = new GridLength(TallRowHeight) });
            }
            else
            {
                for (int i = 0; i < Rows; i++) RowDefinitions.Add(UiHelpers.StarRow());
            }
        }

        public void Register(WidgetDef d) { defs[d.Key] = d; }

        public bool Has(string key)
        {
            for (int i = 0; i < slots.Count; i++) if (slots[i].Def.Key == key) return true;
            return false;
        }

        public void BeginEdit(bool on)
        {
            edit = on;
            for (int i = 0; i < slots.Count; i++) ApplyChrome(slots[i]);
        }

        // ===== 默认布局 + 读写盘 =====
        public void Load()
        {
            slots.Clear();
            Children.Clear();
            bool ok = false;
            try
            {
                if (File.Exists(storePath))
                {
                    string[] lines = File.ReadAllLines(storePath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string ln = lines[i].Trim();
                        if (ln.Length == 0) continue;
                        string[] p = ln.Split(';');
                        if (p.Length != 5) continue;
                        WidgetDef d;
                        if (!defs.TryGetValue(p[0], out d)) continue;
                        var s = new WidgetSlot();
                        s.Def = d;
                        s.Col = int.Parse(p[1], CultureInfo.InvariantCulture);
                        s.Row = int.Parse(p[2], CultureInfo.InvariantCulture);
                        s.W = int.Parse(p[3], CultureInfo.InvariantCulture);
                        s.H = int.Parse(p[4], CultureInfo.InvariantCulture);
                        slots.Add(s);
                        ok = true;
                    }
                }
            }
            catch { slots.Clear(); ok = false; }
            if (!ok) Defaults();
            // 先按当前部件数把行数撑开，再做位置钳制——否则找乐子里存下来的
            // 靠下的行号会被「6 行」的初始网格裁掉（stats 存在第 14 行就是这种情况）
            RebuildRows();
            for (int i = 0; i < slots.Count; i++) Clamp(slots[i]);
            for (int i = 0; i < slots.Count; i++) Attach(slots[i]);
            Loaded += delegate { DumpLayout(); };
        }

        // 调试用：把每张小部件的格子与实际尺寸写进日志
        void DumpLayout()
        {
            try
            {
                File.WriteAllText(Path.Combine(Paths.LauncherDir, "widget_err.log"), DebugDump());
            }
            catch { }
        }

        // 调试用：屏幕上看也行
        public string DebugDump()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("dash ").Append((int)ActualWidth).Append("x").Append((int)ActualHeight).Append('\n');
            for (int i = 0; i < slots.Count; i++)
            {
                WidgetSlot s = slots[i];
                sb.Append(s.Def.Key).Append(" r").Append(s.Row).Append(" h").Append(s.H)
                  .Append(" ph=").Append((int)(s.Host != null ? s.Host.ActualHeight : -1))
                  .Append('\n');
            }
            return sb.ToString();
        }

        public Action DefaultSetup;   // 各页自己定义默认摆放；为空用首页那套

        void Defaults()
        {
            if (DefaultSetup != null) { DefaultSetup(); return; }
            AddDefault("checkin", 0, 0, 4, 3);
            AddDefault("idea", 4, 0, 4, 3);
            AddDefault("servers", 8, 0, 4, 3);
            AddDefault("news", 0, 3, 12, 2);
            AddDefault("notice", 0, 5, 12, 1);
        }

        public void AddDefaultSlot(string key, int col, int row, int w, int h)
        {
            AddDefault(key, col, row, w, h);
        }

        void AddDefault(string key, int col, int row, int w, int h)
        {
            WidgetDef d;
            if (!defs.TryGetValue(key, out d)) return;
            var s = new WidgetSlot();
            s.Def = d;
            s.Col = col; s.Row = row;
            s.W = Math.Max(d.MinW, w); s.H = Math.Max(d.MinH, h);
            Clamp(s);
            slots.Add(s);
        }

        public void Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < slots.Count; i++)
                {
                    WidgetSlot s = slots[i];
                    sb.Append(s.Def.Key).Append(';')
                      .Append(s.Col).Append(';').Append(s.Row).Append(';')
                      .Append(s.W).Append(';').Append(s.H).Append('\n');
                }
                string dir = Path.GetDirectoryName(storePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(storePath, sb.ToString());
            }
            catch { }
        }

        void Clamp(WidgetSlot s)
        {
            s.W = Math.Max(1, Math.Min(Cols, s.W));
            s.H = Math.Max(1, Math.Min(Rows, s.H));
            if (s.Col < 0) s.Col = 0;
            if (s.Row < 0) s.Row = 0;
            if (s.Col + s.W > Cols) s.Col = Cols - s.W;
            int maxRow = RowsNow;
            if (s.Row + s.H > maxRow) s.Row = Math.Max(0, maxRow - s.H);
        }

        // ===== 组装 =====
        void Attach(WidgetSlot s)
        {
            var host = new Border();
            host.CornerRadius = new CornerRadius(10);
            host.Background = Palette.B(Palette.Tile);
            host.BorderThickness = new Thickness(1);
            host.BorderBrush = Palette.B(Palette.Line);
            host.Margin = new Thickness(Gap / 2);
            host.ClipToBounds = true;
            s.Host = host;

            var layer = new Grid();
            UIElement body = null;
            try { body = s.Def.Build != null ? s.Def.Build() : null; }
            catch (Exception ex)
            {
                body = null;
                try
                {
                    File.AppendAllText(Path.Combine(Paths.LauncherDir, "widget_err.log"),
                        s.Def.Key + ": " + ex.GetType().Name + " " + ex.Message + "\n");
                }
                catch { }
            }
            if (body != null)
            {
                var clamp = new ClampPanel();
                clamp.Child = body;
                layer.Children.Add(clamp);
            }

            // 编辑态覆盖层（平时 Collapsed）
            var chromeInner = new Grid();
            var chrome = new Border();
            chrome.Background = new SolidColorBrush(Color.FromArgb(24, 0, 0, 0));
            chrome.BorderThickness = new Thickness(2);
            chrome.BorderBrush = Palette.B(ThemeManager.CurrentColor);
            chrome.CornerRadius = new CornerRadius(10);
            chrome.Visibility = Visibility.Collapsed;
            chrome.Cursor = Cursors.SizeAll;
            chrome.IsHitTestVisible = true;
            chrome.Child = chromeInner;
            s.Chrome = chrome;

            var head = new StackPanel();
            head.Orientation = Orientation.Horizontal;
            head.VerticalAlignment = VerticalAlignment.Top;
            head.Margin = new Thickness(10, 8, 8, 0);
            var tt = new TextBlock();
            tt.Text = s.Def.Title;
            tt.FontSize = 11;
            tt.Foreground = Palette.B(ThemeManager.CurrentColor);
            s.TitleText = tt;
            head.Children.Add(tt);
            chromeInner.Children.Add(head);

            chrome.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e)
            {
                if (!edit) return;
                StartDrag(s, chrome, e, false);
            };
            chrome.MouseMove += delegate(object o, MouseEventArgs e)
            {
                if (dragSlot == s && e.LeftButton == MouseButtonState.Pressed) DoDrag(e);
            };
            chrome.MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs e)
            {
                if (dragSlot == s) EndDrag();
            };

            // 移除按钮
            var close = new Border();
            close.Width = 20; close.Height = 20;
            close.CornerRadius = new CornerRadius(10);
            close.Background = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255));
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.VerticalAlignment = VerticalAlignment.Top;
            close.Margin = new Thickness(0, 6, 6, 0);
            close.Cursor = Cursors.Hand;
            var x = new TextBlock();
            x.Text = "×";
            x.FontSize = 13;
            x.Foreground = Palette.B(Palette.Text);
            x.HorizontalAlignment = HorizontalAlignment.Center;
            x.VerticalAlignment = VerticalAlignment.Center;
            close.Child = x;
            close.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e)
            {
                e.Handled = true;
                Remove(s);
            };
            chromeInner.Children.Add(close);

            // 右下缩放手柄
            var grip = new Border();
            grip.Width = 18; grip.Height = 18;
            grip.Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
            grip.CornerRadius = new CornerRadius(6);
            grip.HorizontalAlignment = HorizontalAlignment.Right;
            grip.VerticalAlignment = VerticalAlignment.Bottom;
            grip.Margin = new Thickness(0, 0, 6, 6);
            grip.Cursor = Cursors.SizeNWSE;
            grip.MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e)
            {
                if (!edit) return;
                e.Handled = true;
                StartDrag(s, grip, e, true);
            };
            grip.MouseMove += delegate(object o, MouseEventArgs e)
            {
                if (dragSlot == s && e.LeftButton == MouseButtonState.Pressed) DoDrag(e);
            };
            grip.MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs e)
            {
                if (dragSlot == s) EndDrag();
            };
            chromeInner.Children.Add(grip);

            layer.Children.Add(chrome);
            host.Child = layer;

            Grid.SetColumn(host, s.Col);
            Grid.SetRow(host, s.Row);
            Grid.SetColumnSpan(host, s.W);
            Grid.SetRowSpan(host, s.H);
            Children.Add(host);
            ApplyChrome(s);
        }

        void ApplyChrome(WidgetSlot s)
        {
            if (s.Chrome == null) return;
            s.Chrome.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
            if (s.Host != null && s.Host.Child is Grid)
            {
                var layer = (Grid)s.Host.Child;
                for (int i = 0; i < layer.Children.Count; i++)
                    if (layer.Children[i] != s.Chrome) layer.Children[i].IsHitTestVisible = !edit;
            }
            if (s.TitleText != null)
            {
                if (edit) s.TitleText.Text = s.Def.Title + "   " + s.W + "×" + s.H;
                else s.TitleText.Text = s.Def.Title;
            }
        }

        // ===== 拖动 / 缩放 =====
        double CellW { get { return ActualWidth <= 0 ? 100 : ActualWidth / Cols; } }
        double CellH { get { return Tall ? TallRowHeight : (ActualHeight <= 0 ? 80 : ActualHeight / Rows); } }

        void StartDrag(WidgetSlot s, UIElement source, MouseButtonEventArgs e, bool isResize)
        {
            dragSlot = s;
            dragStart = e.GetPosition(this);
            dragCol = s.Col; dragRow = s.Row; dragW = s.W; dragH = s.H;
            resizing = isResize;
            // 推挤排序要用「拖动开始时的布局」当基准：每次目标格变化都从快照重算，
            // 这样来回拖不会因为上一次推挤的结果而漂移
            snap = new Dictionary<WidgetSlot, Point>();
            for (int i = 0; i < slots.Count; i++)
            {
                WidgetSlot o = slots[i];
                if (!ReferenceEquals(o, s)) snap[o] = new Point(o.Col, o.Row);
            }
            lastC = int.MinValue; lastR = int.MinValue;
            // 拖动期间用小部件自己的平移量抵消「整格跳」，鼠标跟手连续移动；
            // 松手时把这段残余平移补间到 0，就是平滑吸附
            var tf = s.Host.RenderTransform as TranslateTransform;
            if (tf == null) { tf = new TranslateTransform(); s.Host.RenderTransform = tf; }
            tf.X = 0; tf.Y = 0;
            dragTf = tf;
            if (Scroller != null) ptrV = e.GetPosition(Scroller);
            autoTimer.Start();          // 定时器负责边缘自动滚动（鼠标不动也滚）
            source.CaptureMouse();
            e.Handled = true;
        }

        void DoDrag(MouseEventArgs e)
        {
            if (dragSlot == null) return;
            ptrV = e.GetPosition(Scroller);          // 视口坐标：自动滚动只看它，鼠标不动也滚
            ApplyDrag(new Point(ptrV.X, ptrV.Y + (Scroller != null ? Scroller.VerticalOffset : 0)));
        }

        // 统一按「仪表盘坐标」重算格子与残余位移（鼠标事件和自动滚动定时器都走这里）
        void ApplyDrag(Point p)
        {
            if (dragSlot == null) return;
            double dx = p.X - dragStart.X;
            double dy = p.Y - dragStart.Y;
            int dc = (int)Math.Round(dx / CellW);
            int dr = (int)Math.Round(dy / CellH);
            if (!resizing)
            {
                int c = Math.Max(0, Math.Min(Cols - dragW, dragCol + dc));
                int r = Math.Max(0, Math.Min(RowsNow - dragH, dragRow + dr));
                if (c != lastC || r != lastR)
                {
                    lastC = c; lastR = r;
                    LayoutPush(c, r);      // 目标格变了才重算，避免每帧推挤抖动
                }
                // 残余位移 = 指针实际位移 - 已吸附的整格位移 → 视觉上跟手连续
                if (dragTf != null)
                {
                    dragTf.X = dx - (dragSlot.Col - dragCol) * CellW;
                    dragTf.Y = dy - (dragSlot.Row - dragRow) * CellH;
                }
            }
            else
            {
                int w = Math.Max(dragSlot.Def.MinW, Math.Min(Cols - dragSlot.Col, dragW + dc));
                int h = Math.Max(dragSlot.Def.MinH, Math.Min(RowsNow - dragSlot.Row, dragH + dr));
                dragSlot.W = w; dragSlot.H = h;
                Grid.SetColumnSpan(dragSlot.Host, w);
                Grid.SetRowSpan(dragSlot.Host, h);
                ApplyChrome(dragSlot);
            }
        }

        // ===== 推挤式排序（iOS 主屏） =====
        // 规则：
        //  • 目标格为空 → 直接占用，其他人回到快照位置（拖回原处自然复位）
        //  • 目标格被占 → 被压住的部件按「阅读顺序（先行后列）」依次去找它们之后最近的空位，
        //    后面的从上一个落点继续往后找，形成一条链；全程只往后顺延，不回绕
        //  • 网格满 / 链上找不到空位 → 退化为「与第一个被压住的部件交换」，保证不丢组件
        void LayoutPush(int c, int r)
        {
            int rows = RowsNow;
            c = Math.Max(0, Math.Min(Cols - dragSlot.W, c));
            r = Math.Max(0, Math.Min(rows - dragSlot.H, r));

            var occ = new bool[rows, Cols];
            var others = new List<WidgetSlot>();
            for (int i = 0; i < slots.Count; i++)
            {
                WidgetSlot o = slots[i];
                if (ReferenceEquals(o, dragSlot)) continue;
                others.Add(o);
                Point p = snap[o];
                Mark(occ, (int)p.X, (int)p.Y, o.W, o.H, true);
            }

            var pushed = new List<WidgetSlot>();
            for (int i = 0; i < others.Count; i++)
            {
                WidgetSlot o = others[i];
                Point p = snap[o];
                if (Overlap(c, r, dragSlot.W, dragSlot.H, (int)p.X, (int)p.Y, o.W, o.H)) pushed.Add(o);
            }
            pushed.Sort(delegate(WidgetSlot a, WidgetSlot b)
            {
                Point pa = snap[a], pb = snap[b];
                return (pa.Y * Cols + pa.X).CompareTo(pb.Y * Cols + pb.X);
            });

            // 情况 1：没人被压 —— 占位并让其他人回原位
            if (pushed.Count == 0)
            {
                MoveSlot(dragSlot, c, r, false);
                for (int i = 0; i < others.Count; i++)
                {
                    Point p = snap[others[i]];
                    MoveSlot(others[i], (int)p.X, (int)p.Y, true);
                }
                return;
            }

            // 情况 2：有部件被压 —— 先把它们从占用表里摘掉，再依次往后腾
            for (int i = 0; i < pushed.Count; i++)
            {
                Point p = snap[pushed[i]];
                Mark(occ, (int)p.X, (int)p.Y, pushed[i].W, pushed[i].H, false);
            }
            Mark(occ, c, r, dragSlot.W, dragSlot.H, true);

            var result = new Dictionary<WidgetSlot, Point>();
            result[dragSlot] = new Point(c, r);
            int scan = r * Cols + c;
            bool ok = true;
            for (int i = 0; i < pushed.Count; i++)
            {
                WidgetSlot o = pushed[i];
                int found = -1;
                for (int idx = scan; idx < rows * Cols; idx++)
                {
                    int rr = idx / Cols, cc = idx % Cols;
                    if (cc + o.W > Cols || rr + o.H > rows) continue;
                    if (IsFree(occ, cc, rr, o.W, o.H)) { found = idx; break; }
                }
                if (found < 0) { ok = false; break; }
                int nr = found / Cols, nc = found % Cols;
                Mark(occ, nc, nr, o.W, o.H, true);
                result[o] = new Point(nc, nr);
                scan = found;       // 保持顺序：下一个从这一格继续往后找
            }

            if (!ok)
            {
                // 情况 3：放不下 —— 与第一个被压的交换位置（永远不会丢组件）
                WidgetSlot first = pushed[0];
                Point fp = snap[first];
                Point dp = new Point(dragCol, dragRow);
                MoveSlot(dragSlot, (int)fp.X, (int)fp.Y, false);
                MoveSlot(first, (int)dp.X, (int)dp.Y, true);
                for (int i = 0; i < others.Count; i++)
                {
                    WidgetSlot o = others[i];
                    if (ReferenceEquals(o, first)) continue;
                    Point p = snap[o];
                    MoveSlot(o, (int)p.X, (int)p.Y, true);
                }
                return;
            }

            MoveSlot(dragSlot, c, r, false);
            for (int i = 0; i < others.Count; i++)
            {
                WidgetSlot o = others[i];
                Point np = result.ContainsKey(o) ? result[o] : snap[o];
                MoveSlot(o, (int)np.X, (int)np.Y, true);
            }
        }

        // 移动一个部件到指定格；animate=true 时用 180ms 补间从旧位置滑过去
        void MoveSlot(WidgetSlot s, int col, int row, bool animate)
        {
            if (s.Host == null) return;
            if (s.Col == col && s.Row == row) return;
            double dx = (s.Col - col) * CellW;
            double dy = (s.Row - row) * CellH;
            s.Col = col; s.Row = row;
            Grid.SetColumn(s.Host, col);
            Grid.SetRow(s.Host, row);
            if (!animate) return;
            var tf = s.Host.RenderTransform as TranslateTransform;
            if (tf == null) { tf = new TranslateTransform(); s.Host.RenderTransform = tf; }
            tf.X = dx; tf.Y = dy;
            // 每个部件用独立动画 key：共用 key 会互相打断，导致先被推的那个停在半路
            Ani.Start("push" + s.Host.GetHashCode(), Ani.AaProgress(delegate(double t)
            {
                tf.X = dx * (1 - t);
                tf.Y = dy * (1 - t);
            }, 180, new AniEaseOutFluent(Ani.Weak)));
        }

        static bool Overlap(int c1, int r1, int w1, int h1, int c2, int r2, int w2, int h2)
        {
            return c1 < c2 + w2 && c2 < c1 + w1 && r1 < r2 + h2 && r2 < r1 + h1;
        }

        static void Mark(bool[,] occ, int c, int r, int w, int h, bool v)
        {
            int rows = occ.GetLength(0), cols = occ.GetLength(1);
            for (int y = r; y < r + h && y < rows; y++)
                for (int x = c; x < c + w && x < cols; x++)
                    if (y >= 0 && x >= 0) occ[y, x] = v;
        }

        static bool IsFree(bool[,] occ, int c, int r, int w, int h)
        {
            int rows = occ.GetLength(0), cols = occ.GetLength(1);
            for (int y = r; y < r + h; y++)
                for (int x = c; x < c + w; x++)
                {
                    if (y < 0 || y >= rows || x < 0 || x >= cols) return false;
                    if (occ[y, x]) return false;
                }
            return true;
        }

        // 定时驱动的边缘自动滚动：只取决于指针落在边缘区的深度，鼠标静止也持续滚
        void AutoScrollTick(object sender, EventArgs e)
        {
            if (dragSlot == null || Scroller == null) return;
            double vh = Scroller.ViewportHeight;
            if (vh <= 1) return;
            const double Edge = 56;
            const double MaxSpeed = 420;   // 压到边缘最深处 ≈ 420px/s
            double y = ptrV.Y;
            double depth = 0;
            if (y > vh - Edge) depth = Math.Min(1, (y - (vh - Edge)) / Edge);
            else if (y < Edge) depth = -Math.Min(1, (Edge - y) / Edge);
            if (depth == 0) return;
            double before = Scroller.VerticalOffset;
            double target = Math.Max(0, Math.Min(Scroller.ScrollableHeight, before + depth * MaxSpeed * 0.016));
            if (Math.Abs(target - before) < 0.01) return;
            Scroller.ScrollToVerticalOffset(target);
            // 视口滚了，按新偏移重算卡片位置（鼠标没动，卡片仍跟着指针走）
            ApplyDrag(new Point(ptrV.X, ptrV.Y + Scroller.VerticalOffset));
        }

        void EndDrag()
        {
            if (dragSlot == null) return;
            WidgetSlot s = dragSlot;
            dragSlot = null;
            autoTimer.Stop();
            Mouse.Capture(null);
            if (Overlaps(s))
            {
                // 理论上推挤后不会重叠；真重叠了说明这轮布局非法 → 全体回到快照位置
                if (snap != null)
                {
                    foreach (KeyValuePair<WidgetSlot, Point> kv in snap)
                        MoveSlot(kv.Key, (int)kv.Value.X, (int)kv.Value.Y, true);
                }
                MoveSlot(s, dragCol, dragRow, true);
                Shake(s.Host);
            }
            else
            {
                Clamp(s);
                Save();
            }
            snap = null;
            SnapBack(s);
            ApplyChrome(s);
            if (Changed != null) Changed();
        }

        // 松手补间：把拖动残留的平移量平滑收回 0（= 平滑吸附到格子）
        void SnapBack(WidgetSlot s)
        {
            var tf = dragTf;
            dragTf = null;
            if (tf == null || s.Host == null) return;
            if (Math.Abs(tf.X) < 0.5 && Math.Abs(tf.Y) < 0.5) { tf.X = 0; tf.Y = 0; return; }
            double fx = tf.X, fy = tf.Y;
            Ani.Start("dashSnap", Ani.AaProgress(delegate(double t)
            {
                tf.X = fx * (1 - t);
                tf.Y = fy * (1 - t);
            }, 170, new AniEaseOutFluent(Ani.Weak)));
        }

        bool Overlaps(WidgetSlot a)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                WidgetSlot b = slots[i];
                if (ReferenceEquals(a, b)) continue;
                if (a.Col < b.Col + b.W && b.Col < a.Col + a.W &&
                    a.Row < b.Row + b.H && b.Row < a.Row + a.H) return true;
            }
            return false;
        }

        static void Shake(UIElement el)
        {
            var tf = el.RenderTransform as TranslateTransform;
            if (tf == null) { tf = new TranslateTransform(); el.RenderTransform = tf; }
            Ani.Start("wiggle",
                Ani.Aa(delegate(double d) { tf.X += d; }, 5, 60, new AniEaseOutFluent(Ani.Weak)),
                Ani.Aa(delegate(double d) { tf.X += d; }, -10, 90, new AniEaseOutFluent(Ani.Weak)),
                Ani.Aa(delegate(double d) { tf.X += d; }, 5, 90, new AniEaseOutFluent(Ani.Weak)));
        }

        // ===== 增删 =====
        public void Remove(WidgetSlot s)
        {
            if (s == null) return;
            slots.Remove(s);
            Children.Remove(s.Host);
            RebuildRows();
            Save();
            if (Changed != null) Changed();
        }

        public List<WidgetDef> Available()
        {
            var list = new List<WidgetDef>();
            foreach (KeyValuePair<string, WidgetDef> kv in defs)
                if (!Has(kv.Key)) list.Add(kv.Value);
            return list;
        }

        public bool Add(string key)
        {
            WidgetDef d;
            if (!defs.TryGetValue(key, out d) || Has(key)) return false;
            var s = new WidgetSlot();
            s.Def = d; s.W = d.W; s.H = d.H;
            slots.Add(s);
            RebuildRows();          // 先按新数量扩行，再找空位
            if (!FindFree(s))
            {
                slots.Remove(s);
                RebuildRows();
                return false;
            }
            Attach(s);
            Save();
            if (Changed != null) Changed();
            return true;
        }

        bool FindFree(WidgetSlot s)
        {
            for (int r = 0; r + s.H <= RowsNow; r++)
                for (int c = 0; c + s.W <= Cols; c++)
                {
                    s.Col = c; s.Row = r;
                    if (!Overlaps(s)) return true;
                }
            return false;
        }
    }
}
