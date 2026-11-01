// HugeLauncher.Ui - 液态玻璃材质库：Squircle 连续曲率几何 / VisualBrush 活底透镜 / 多环无缝折射
// 活底原理：玻璃件刷的是「自己正下方的前台图层」（VisualBrush 实时引用，GPU 逐帧自动更新），
// 源图层必须不包含玻璃件自己（循环引用 WPF 直接渲染成黑洞——沙盒实测）。
// 源图层透明处 = 画刷透明 = 真透视：底下的静态背景免费透出来，不用伪造。
// 折射 = N 个互不相交的 1px 甜甜圈环（SquircleRing），取景外扩量从边缘 RimMax 按 (1-t)² 平滑→0：
// 连续弯曲无接缝，中心恒等文字清晰。环必须是真环形——嵌套遮盖法在透明源上会叠出 N 层错位重影。
// 对齐 = 逐帧扫描所有透镜元素（TransformToVisual 相对源图层取景），矩形没变就跳过：静止零开销，动则全实时。
// ThemeManager.LiquidGlass=false 时所有工厂返回 null / 透明，控件走原有工业风。
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace HugeLauncher.Ui
{
    // 透镜参数记录：挂 el.Tag 上，Align 据此取景
    class GlassLens
    {
        public VisualBrush Brush;
        public Visual Source;      // 活底来源图层（绝不含 el 自己）
        public double ExpandPx;    // 取景外扩 = 折射位移量（0=中心层）
        public double Zoom;        // 中心缩放倍率（1=恒等）
        public Rect Last;          // 上帧取景框：没变就不碰画刷（静止零重绘）
        public bool HasLast;
    }

    static class Glass
    {
        public static bool On;
        public const double RimMax = 5.0;     // 折射强度：中带最大位移 px（沙盒里用户认可的浓度）
        public const double BandPx = 8.0;     // 折射带宽度 px = 环数（沙盒 8 环同款细度，梯度够细才读不出阶梯）
        const int MaxRings = 8;

        // 背景透镜源（史莱姆层的容器视觉，主窗口 BuildUi 时设置；null=史莱姆关）。
        // 只给「正下方没有前台图层可取」的玻璃件兜底（如启动按钮在史蒂夫视口下方）。
        // pill 类玻璃不许用它：pill 必须刷文字层，文字才会弯（14 曾改取背景层，扭曲全没——用户打回）
        public static Visual BgLensSource;

        static readonly List<WeakReference> lenses = new List<WeakReference>(); // 所有透镜元素（逐帧对齐登记处）
        static bool loopHooked;

        // [临时探针] 活着的透镜数量
        public static int LensCount
        {
            get
            {
                lenses.RemoveAll(delegate(WeakReference wr) { return !wr.IsAlive; });
                return lenses.Count;
            }
        }

        // 启动逐帧对齐循环（幂等；参数仅兼容旧调用，活底不需要根元素）
        public static void Init(FrameworkElement unused)
        {
            if (loopHooked) return;
            loopHooked = true;
            CompositionTarget.Rendering += delegate { if (On) AlignAll(); };
        }

        // ---------- 单件透镜刷 ----------
        // source = 玻璃件正下方的前台图层；expandPx>0 外扩取景（折射带），zoom=中心缩放
        public static Brush Lens(FrameworkElement el, Visual source, double expandPx, double zoom)
        {
            if (!On || source == null) return null;
            var b = new VisualBrush(source);
            b.Stretch = Stretch.Fill;
            b.ViewboxUnits = BrushMappingMode.Absolute;
            // Viewport 也用绝对坐标并钉死在元素自身矩形上：取景框（含外扩）→ 元素矩形，折射=均匀压缩采样。
            // 否则默认 Viewport=绘制几何包围盒，环形片的甜甜圈包围盒比元素小，画刷错位 → 透明源上文字重影
            b.ViewportUnits = BrushMappingMode.Absolute;
            el.Tag = new GlassLens { Brush = b, Source = source, ExpandPx = expandPx, Zoom = zoom };
            Register(el);
            Align(el);
            return b;
        }
        public static Brush Lens(FrameworkElement el, Visual source, double expandPx)
        {
            return Lens(el, source, expandPx, 1.0);
        }

        // ---------- 多环无缝折射透镜 ----------
        // 互不相交的层：中心恒等透镜（缩进 n px）+ N 个 1px 甜甜圈折射环（外扩量边缘 RimMax → 中心 0 平滑衰减）。
        // 关键：源图层透明处各层全是透明的，嵌套遮盖法会同时透出 N 层错位副本（文字重影）——
        // 所以环必须是真环形（SquircleRing），每个像素只由唯一一层绘制，透明处 = 真透视。
        // 返回最外容器（描边/阴影挂它身上），faceHost = 中心区内容位（罩色/高光/文字往里放）
        public static Border LensRings(Visual source, double radius, double bandPx, out Grid faceHost, bool opaqueBacker)
        {
            faceHost = null;
            if (!On || source == null) return null;
            int n = (int)Math.Round(bandPx);
            if (n < 3) n = 3;
            if (n > MaxRings) n = MaxRings;
            var outer = new SquircleBorder();
            outer.CornerRadius = new CornerRadius(radius);
            var layers = new Grid();
            if (opaqueBacker)
            {
                // 衬底：挡真文字透过画刷透明区和透镜副本叠出的亚像素重影。
                // 史莱姆开启时用 Palette.Tile 的色相，但 alpha 强制不低于 220——
                // FrostMid 调低时（如 30%）衬底太透，真文字渗上来和透镜副本叠成明显重影；
                // 220 封顶渗漏 ≤14% 肉眼不可见，色块流动感仍保留（用户拍板：罩就罩着）
                var backer = new SquircleBorder();
                backer.CornerRadius = new CornerRadius(radius);
                if (Slime.On)
                {
                    var tc = Palette.Tile;
                    backer.Background = Palette.B(Color.FromArgb((byte)Math.Max(220, (int)tc.A), tc.R, tc.G, tc.B));
                }
                else backer.Background = Palette.B(Palette.BgTop);
                layers.Children.Add(backer);
            }
            // 中心恒等透镜：取景≈1:1（ex 极小），缩进 n px 把边缘让给折射环
            var center = new SquircleBorder();
            center.CornerRadius = new CornerRadius(Math.Max(0.5, radius - n));
            center.Margin = new Thickness(n);
            center.Background = Lens(center, source, bandPx * Math.Pow(0.5 / n, 2), 1.0);
            layers.Children.Add(center);
            // 折射环：外扩按 (1-(i+0.5)/n)² 从外沿 exBase 平滑→0（13 定稿剖面，文字弯曲用户认可）；
            // 8 环细带宽让梯度比老 5 环更顺滑。位移基数按 RimMax 封顶，带宽 8 时不跟着变大
            double exBase = Math.Min(bandPx, RimMax);
            for (int i = 0; i < n; i++)
            {
                var ring = new SquircleRing();
                ring.Inset = i;
                ring.Radius = radius;
                ring.LensFill = Lens(ring, source, exBase * Math.Pow(1 - (i + 0.5) / n, 2), 1.0);
                layers.Children.Add(ring);
            }
            faceHost = new Grid();
            faceHost.Tag = radius;   // 叠层整脸铺满（0 内缩、半径=外层）：罩色/高光打在外表面，不再剥出内层板
            layers.Children.Add(faceHost);
            // 边缘受光描边压最顶层：罩色/高光都在它下面，折射带边缘的受光保持清脆
            var rim = new SquircleBorder();
            rim.CornerRadius = new CornerRadius(radius);
            rim.BorderBrush = Edge();
            rim.BorderThickness = new Thickness(1);
            rim.IsHitTestVisible = false;
            layers.Children.Add(rim);
            outer.Child = layers;
            return outer;
        }

        // 兼容旧调用：默认无衬底（真透视）
        public static Border LensRings(Visual source, double radius, double bandPx, out Grid faceHost)
        {
            return LensRings(source, radius, bandPx, out faceHost, false);
        }

        static void Register(FrameworkElement el)
        {
            lenses.RemoveAll(delegate(WeakReference wr) { return !wr.IsAlive; });
            foreach (var wr in lenses) if (wr.Target == el) return;
            lenses.Add(new WeakReference(el));
        }

        static bool AlignAll()
        {
            bool any = false;
            lenses.RemoveAll(delegate(WeakReference wr) { return !wr.IsAlive; });
            for (int i = 0; i < lenses.Count; i++)
            {
                var el = lenses[i].Target as FrameworkElement;
                if (el != null && Align(el)) any = true;
            }
            return any;
        }

        // 元素矩形 → 源图层坐标系的绝对取景框（含外扩/缩放）；与上帧相同则跳过。返回 true=取景框变了
        static bool Align(FrameworkElement el)
        {
            var lens = el.Tag as GlassLens;
            if (lens == null || lens.Source == null) return false;
            try
            {
                if (!el.IsVisible || el.ActualWidth < 2 || el.ActualHeight < 2) { lens.HasLast = false; return false; }
                var p = el.TransformToVisual(lens.Source).Transform(new Point(0, 0));
                // WPF 怪癖实证：VisualBrush 渲染源图层时，内容会出现在「局部坐标 + 源相对父级的布局偏移」处
                // （源自身的 VisualOffset 进了画刷）。要取元素正下方的内容，取景框必须同样加上这个偏移：
                // steveLayer 在左栏 g 里 y=147.3，取景框不加 147.3 就会在腿部位置取到头部画面。
                // （曾误改方向写成减，双减后气泡取到负区全空、启动按钮取到头——DumpLensImages 实证纠正）
                var off = VisualTreeHelper.GetOffset(lens.Source);
                double x = p.X + off.X - lens.ExpandPx, y = p.Y + off.Y - lens.ExpandPx;
                double w = el.ActualWidth + 2 * lens.ExpandPx, h = el.ActualHeight + 2 * lens.ExpandPx;
                if (lens.Zoom > 0 && Math.Abs(lens.Zoom - 1.0) > 0.001)
                {
                    double shrink = 1.0 / lens.Zoom;
                    double cx = x + w / 2, cy = y + h / 2;
                    w *= shrink; h *= shrink;
                    x = cx - w / 2; y = cy - h / 2;
                }
                var r = new Rect(x, y, w, h);
                if (lens.HasLast &&
                    Math.Abs(r.X - lens.Last.X) < 0.05 && Math.Abs(r.Y - lens.Last.Y) < 0.05 &&
                    Math.Abs(r.Width - lens.Last.Width) < 0.05 && Math.Abs(r.Height - lens.Last.Height) < 0.05) return false;
                lens.Last = r; lens.HasLast = true;
                lens.Brush.Viewbox = r;
                lens.Brush.Viewport = new Rect(0, 0, el.ActualWidth, el.ActualHeight);
                return true;
            }
            catch { return false; }
        }

        // 弹簧/拖动回调里的立即对齐（逐帧循环也会兜住，这里抢一拍更跟手）
        public static void Realign(FrameworkElement el) { if (On) Align(el); }

        // [临时探针] 转储所有透镜的取景框 vs 元素真实位置，排查折射错位
        public static string DumpLenses()
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < lenses.Count; i++)
            {
                var el = lenses[i].Target as FrameworkElement;
                if (el == null) continue;
                var lens = el.Tag as GlassLens;
                if (lens == null) continue;
                try
                {
                    var p = el.TransformToVisual(lens.Source).Transform(new Point(0, 0));
                    var off = VisualTreeHelper.GetOffset(lens.Source);
                    var src = lens.Source as FrameworkElement;
                    sb.AppendLine(string.Format(
                        "{0} pos=({1:F1},{2:F1}) size=({3:F1}x{4:F1}) vb=({5:F1},{6:F1} {7:F1}x{8:F1}) srcOff=({9:F1},{10:F1}) srcSize=({11:F1}x{12:F1})",
                        el.GetType().Name, p.X, p.Y, el.ActualWidth, el.ActualHeight,
                        lens.Brush.Viewbox.X, lens.Brush.Viewbox.Y, lens.Brush.Viewbox.Width, lens.Brush.Viewbox.Height,
                        off.X, off.Y,
                        src == null ? 0 : src.ActualWidth, src == null ? 0 : src.ActualHeight));
                }
                catch (Exception ex) { sb.AppendLine(el.GetType().Name + " ERR " + ex.Message); }
            }
            return sb.ToString();
        }

        // [临时探针] 把每个透镜画刷实际显示的内容渲成 PNG（所见即所取）
        public static void DumpLensImages(string dir)
        {
            try { System.IO.Directory.CreateDirectory(dir); } catch { }
            for (int i = 0; i < lenses.Count; i++)
            {
                var el = lenses[i].Target as FrameworkElement;
                if (el == null) continue;
                var lens = el.Tag as GlassLens;
                if (lens == null || !el.IsVisible) continue;
                try
                {
                    double w = el.ActualWidth, h = el.ActualHeight;
                    if (w < 2 || h < 2) continue;
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                        dc.DrawRectangle(lens.Brush, null, new Rect(0, 0, w, h));
                    var rt = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
                    rt.Render(dv);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rt));
                    using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir,
                        "lens_" + i.ToString("00") + "_" + el.GetType().Name + ".png"))) enc.Save(fs);
                    // 对照组：渲染元素本体（OnRender 真实路径）——画刷有图而本体空 = 几何/绘制断了
                    var rt2 = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
                    rt2.Render(el);
                    var enc2 = new PngBitmapEncoder();
                    enc2.Frames.Add(BitmapFrame.Create(rt2));
                    using (var fs2 = System.IO.File.Create(System.IO.Path.Combine(dir,
                        "el_" + i.ToString("00") + "_" + el.GetType().Name + ".png"))) enc2.Save(fs2);
                }
                catch { }
            }
        }

        // ---------- Squircle 几何（Lamé 超椭圆角，n=4，连续曲率） ----------

        static readonly Dictionary<string, StreamGeometry> squircleCache = new Dictionary<string, StreamGeometry>();

        public static StreamGeometry SquircleGeometry(double w, double h, double r)
        {
            if (w <= 0 || h <= 0) return null;
            string key = ((int)(w * 2)) + "|" + ((int)(h * 2)) + "|" + ((int)(r * 2));
            StreamGeometry cached;
            if (squircleCache.TryGetValue(key, out cached)) return cached;
            var g0 = BuildSquircle(w, h, r, 0, 0);
            if (g0 == null) return null;
            g0.Freeze();
            if (squircleCache.Count > 400) squircleCache.Clear();
            squircleCache[key] = g0;
            return g0;
        }

        // 甜甜圈环几何：外沿 squircle(w-2i, r-i) @ (i,i)，内沿 squircle(w-2i-2w2, r-i-w2) @ (i+w2,i+w2)，
        // EvenOdd 掏空 → 只画 w2 px 环带（嵌套遮盖法在透明源上会叠出 N 层重影，环形不会）
        static readonly Dictionary<string, Geometry> ringCache = new Dictionary<string, Geometry>();

        public static Geometry SquircleRingGeometry(double w, double h, double r, double inset, double width)
        {
            if (w <= 0 || h <= 0) return null;
            string key = ((int)(w * 2)) + "|" + ((int)(h * 2)) + "|" + ((int)(r * 2)) + "|" + ((int)(inset * 2)) + "|" + ((int)(width * 2));
            Geometry cached;
            if (ringCache.TryGetValue(key, out cached)) return cached;
            var outer = BuildSquircle(w - 2 * inset, h - 2 * inset, r - inset, inset, inset);
            if (outer == null) return null;
            var group = new GeometryGroup();
            group.FillRule = FillRule.EvenOdd;
            group.Children.Add(outer);
            var inner = BuildSquircle(w - 2 * (inset + width), h - 2 * (inset + width), r - inset - width, inset + width, inset + width);
            if (inner != null) group.Children.Add(inner);   // 内沿退化 → 实心
            group.Freeze();
            if (ringCache.Count > 400) ringCache.Clear();
            ringCache[key] = group;
            return group;
        }

        // 兼容旧调用：默认 1px 环宽
        public static Geometry SquircleRingGeometry(double w, double h, double r, double inset)
        {
            return SquircleRingGeometry(w, h, r, inset, 1);
        }

        // 原始 squircle 路径构建（ox/oy = 整体偏移；不冻结不进缓存，便于环形几何组合）
        static StreamGeometry BuildSquircle(double w, double h, double r, double ox, double oy)
        {
            if (w <= 0 || h <= 0) return null;
            r = Math.Max(0, Math.Min(r, Math.Min(w, h) / 2));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                if (r < 0.5)
                {
                    ctx.BeginFigure(new Point(ox, oy), true, true);
                    ctx.LineTo(new Point(ox + w, oy), true, false);
                    ctx.LineTo(new Point(ox + w, oy + h), true, false);
                    ctx.LineTo(new Point(ox, oy + h), true, false);
                }
                else if (r >= Math.Min(w, h) / 2 - 0.01)
                {
                    // 胶囊：两端标准半圆（iOS 开关轨道/滑条圆头）——端部弧度=正圆，
                    // 与圆形滑钮同心同曲率（超椭圆会偏方，肉眼可见不贴合）
                    double rr = Math.Min(w, h) / 2;
                    if (w >= h)
                    {
                        ctx.BeginFigure(new Point(ox + rr, oy), true, true);
                        ctx.LineTo(new Point(ox + w - rr, oy), true, false);
                        ctx.ArcTo(new Point(ox + w - rr, oy + h), new Size(rr, rr), 0, false, SweepDirection.Clockwise, true, false);
                        ctx.LineTo(new Point(ox + rr, oy + h), true, false);
                        ctx.ArcTo(new Point(ox + rr, oy), new Size(rr, rr), 0, false, SweepDirection.Clockwise, true, false);
                    }
                    else
                    {
                        ctx.BeginFigure(new Point(ox + w, oy + rr), true, true);
                        ctx.LineTo(new Point(ox + w, oy + h - rr), true, false);
                        ctx.ArcTo(new Point(ox, oy + h - rr), new Size(rr, rr), 0, false, SweepDirection.Clockwise, true, false);
                        ctx.LineTo(new Point(ox, oy + rr), true, false);
                        ctx.ArcTo(new Point(ox + w, oy + rr), new Size(rr, rr), 0, false, SweepDirection.Clockwise, true, false);
                    }
                }
                else
                {
                    // 四角采样 Lamé 曲线：指数 2/n=0.5（n=4，iOS 连续曲率近似）
                    const int seg = 8;
                    ctx.BeginFigure(new Point(ox + w - r, oy), true, true);
                    for (int i = 1; i <= seg; i++)
                    {
                        double t = Math.PI / 2 * i / seg;
                        ctx.LineTo(new Point(ox + w - r + r * Math.Pow(Math.Sin(t), 0.5),
                            oy + r - r * Math.Pow(Math.Cos(t), 0.5)), true, false);
                    }
                    ctx.LineTo(new Point(ox + w, oy + h - r), true, false);
                    for (int i = 1; i <= seg; i++)
                    {
                        double t = Math.PI / 2 * i / seg;
                        ctx.LineTo(new Point(ox + w - r + r * Math.Pow(Math.Cos(t), 0.5),
                            oy + h - r + r * Math.Pow(Math.Sin(t), 0.5)), true, false);
                    }
                    ctx.LineTo(new Point(ox + r, oy + h), true, false);
                    for (int i = 1; i <= seg; i++)
                    {
                        double t = Math.PI / 2 * i / seg;
                        ctx.LineTo(new Point(ox + r - r * Math.Pow(Math.Sin(t), 0.5),
                            oy + h - r + r * Math.Pow(Math.Cos(t), 0.5)), true, false);
                    }
                    ctx.LineTo(new Point(ox, oy + r), true, false);
                    for (int i = 1; i <= seg; i++)
                    {
                        double t = Math.PI / 2 * i / seg;
                        ctx.LineTo(new Point(ox + r - r * Math.Pow(Math.Cos(t), 0.5),
                            oy + r - r * Math.Pow(Math.Sin(t), 0.5)), true, false);
                    }
                }
            }
            return g;
        }

        // ---------- 画刷工厂（On=false 一律返回 null，调用方回退工业风） ----------

        // 模拟玻璃底：竖向微渐变罩（滚动内容/无源图层时的兜底；基调色随明暗=暗白亮黑）
        public static Brush SimulatedFace(double alpha)
        {
            if (!On) return null;
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(0, 1);
            byte a1 = (byte)(alpha * 255 + 26), a2 = (byte)(alpha * 255 + 6);
            g.GradientStops.Add(new GradientStop(Palette.GlassA(a1), 0));
            g.GradientStops.Add(new GradientStop(Palette.GlassA(a2), 1));
            return g;
        }

        // 模拟折射带（滚动内容用）：比 SimulatedFace 略浓的竖渐变，两层叠出边缘差
        public static Brush SimulatedLensEdge()
        {
            if (!On) return null;
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(0, 1);
            g.GradientStops.Add(new GradientStop(Palette.GlassA(58), 0));
            g.GradientStops.Add(new GradientStop(Palette.GlassA(22), 1));
            return g;
        }

        // 顶部受光高光罩（沙盒定稿：10% 起、30% 处收尾——倒影可以有，但不能太浓太占）
        public static Brush Sheen()
        {
            if (!On) return null;
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(0, 1);
            g.GradientStops.Add(new GradientStop(Color.FromArgb(26, 0xFF, 0xFF, 0xFF), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(5, 0xFF, 0xFF, 0xFF), 0.3));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0xFF, 0xFF, 0xFF), 0.55));
            return g;
        }

        // 边缘受光描边（顶亮底隐，模拟顶部受光；基调色随明暗——亮模式深色描边才有轮廓）
        public static Brush Edge()
        {
            if (!On) return null;
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(0.3, 1);
            g.GradientStops.Add(new GradientStop(Palette.GlassA(110), 0));
            g.GradientStops.Add(new GradientStop(Palette.GlassA(36), 0.5));
            g.GradientStops.Add(new GradientStop(Palette.GlassA(14), 1));
            return g;
        }
    }

    // Squircle 边框：Border 子类，OnRender 自绘连续曲率外形（半径取 CornerRadius.TopLeft）
    class SquircleBorder : Border
    {
        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            var geo = Glass.SquircleGeometry(w, h, CornerRadius.TopLeft);
            if (geo == null) return;
            if (Background != null) dc.DrawGeometry(Background, null, geo);
            if (BorderBrush != null && BorderThickness.Left > 0)
                dc.DrawGeometry(null, new Pen(BorderBrush, BorderThickness.Left), geo);
        }
    }

    // Squircle 甜甜圈环：只画 [Inset, Inset+BandWidth] 的环带（多环折射用）。
    // 嵌套遮盖法在透明源图层上会同时透出 N 层错位副本（文字重影），真环形每像素只画一次。
    class SquircleRing : FrameworkElement
    {
        double inset;
        double radius = 100;
        double bandWidth = 1;
        Brush lensFill;

        public double Inset { get { return inset; } set { inset = value; InvalidateVisual(); } }
        public double Radius { get { return radius; } set { radius = value; InvalidateVisual(); } }
        public double BandWidth { get { return bandWidth; } set { bandWidth = value; InvalidateVisual(); } }
        public Brush LensFill { get { return lensFill; } set { lensFill = value; InvalidateVisual(); } }

        protected override void OnRender(System.Windows.Media.DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0 || lensFill == null) return;
            var geo = Glass.SquircleRingGeometry(w, h, radius, inset, bandWidth);
            if (geo != null) dc.DrawGeometry(lensFill, null, geo);
        }
    }
}
