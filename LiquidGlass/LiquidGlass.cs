// LiquidGlass 沙盒：液态玻璃渲染实验台（与 HIL 解耦，随便折腾）
// 棋盘格底（静止）+ 呼吸活块 + 粘鼠标的胶囊 pill（弹簧跟随/果冻挤压/按压回弹）
// 折射 = 8 层嵌套细环，外扩量从边缘到中心平滑→0：连续弯曲无接缝，中心文字清晰
// 按键：1=静态快照  2=VisualBrush 活底  3=活底+真实模糊
//      ↑/↓ 高光浓度  ←/→ 折射强度  Z/X 中心缩放  R 重截快照
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LiquidGlassLab
{
    public class LabWindow : Window
    {
        const int N = 8;                       // 折射环层数
        const double PW = 170, PH = 44;        // pill 尺寸（一级标签同款比例放大版）

        Grid root, bgLayer, overlay, pill;
        Border[] rings = new Border[N];
        Border pillTint, pillSheen, liveBlock;
        Rectangle checker;
        TextBlock hud;
        Point mouse = new Point(450, 280);
        Point pos = new Point(450, 280), vel;
        double pressScale = 1, pressVel;
        bool pressed;
        int mode = 4, brushesMode = -1;   // 启动默认方案4：循环引用测试
        System.Windows.Media.Effects.Effect shadowFx;
        double sheenAlpha = 0.10;   // 高光浓度
        double rimPx = 5.0;         // 折射强度（边缘最大外扩）
        double innerZoom = 1.0;     // 中心缩放（1=恒等）
        BitmapSource snap;
        TimeSpan lastFrame;

        public LabWindow()
        {
            Title = "LiquidGlass 沙盒  [1/2/3 方案  ↑↓高光  ←→折射  Z/X缩放  R重截]";
            Width = 960; Height = 600;
            Background = Brushes.Black;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            root = new Grid();

            // ---------- 底层：棋盘格（静止）+ 活块，所有玻璃只许刷这一层 ----------
            bgLayer = new Grid();
            checker = new Rectangle();
            checker.Width = 2400; checker.Height = 2400;
            checker.Margin = new Thickness(-600, -600, 0, 0);
            checker.HorizontalAlignment = HorizontalAlignment.Left;
            checker.VerticalAlignment = VerticalAlignment.Top;
            var tile = new DrawingBrush();
            tile.TileMode = TileMode.Tile;
            tile.Viewport = new Rect(0, 0, 32, 32);
            tile.ViewportUnits = BrushMappingMode.Absolute;
            var dg = new GeometryDrawing();
            var gg = new GeometryGroup();
            gg.Children.Add(new RectangleGeometry(new Rect(0, 0, 16, 16)));
            gg.Children.Add(new RectangleGeometry(new Rect(16, 16, 16, 16)));
            dg.Geometry = gg;
            dg.Brush = new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xE8));
            var dg2 = new GeometryDrawing();
            var gg2 = new GeometryGroup();
            gg2.Children.Add(new RectangleGeometry(new Rect(16, 0, 16, 16)));
            gg2.Children.Add(new RectangleGeometry(new Rect(0, 16, 16, 16)));
            dg2.Geometry = gg2;
            dg2.Brush = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A));
            var all = new DrawingGroup();
            all.Children.Add(dg); all.Children.Add(dg2);
            tile.Drawing = all;
            tile.Freeze();
            checker.Fill = tile;
            bgLayer.Children.Add(checker);

            var t1 = MkText("液态玻璃 LIQUID GLASS", 42, FontWeights.Bold, 80, 90);
            var t2 = MkText("文字可读性测试 The quick brown fox 0123456789", 20, FontWeights.Normal, 80, 170);
            var t3 = MkText("拖动 pill 滑过这些字，看边缘扭曲和倒影", 16, FontWeights.Normal, 80, 220);
            bgLayer.Children.Add(t1); bgLayer.Children.Add(t2); bgLayer.Children.Add(t3);

            liveBlock = new Border();
            liveBlock.Width = 70; liveBlock.Height = 70;
            liveBlock.Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x30));
            liveBlock.CornerRadius = new CornerRadius(12);
            liveBlock.Margin = new Thickness(80, 300, 0, 0);
            liveBlock.HorizontalAlignment = HorizontalAlignment.Left;
            liveBlock.VerticalAlignment = VerticalAlignment.Top;
            bgLayer.Children.Add(liveBlock);
            var t4 = MkText("← 活块：呼吸闪烁，方案1里它在玻璃下是定格的，方案2/3里实时闪", 14, FontWeights.Normal, 165, 325);
            bgLayer.Children.Add(t4);
            root.Children.Add(bgLayer);

            // ---------- 覆盖层：胶囊 pill（8 环嵌套） ----------
            overlay = new Grid();
            overlay.IsHitTestVisible = false;
            pill = new Grid();
            pill.Width = PW; pill.Height = PH;
            pill.IsHitTestVisible = false;

            Border parent = null;
            for (int i = 0; i < N; i++)
            {
                var b = new Border();
                b.CornerRadius = new CornerRadius(Math.Max(0.5, PH / 2 - i));
                if (i > 0) b.Margin = new Thickness(1);
                rings[i] = b;
                if (parent == null) pill.Children.Add(b); else parent.Child = b;
                parent = b;
            }
            rings[0].BorderThickness = new Thickness(1);   // 最外环：边缘受光描边 + 软阴影
            rings[0].BorderBrush = MakeEdge();
            shadowFx = new System.Windows.Media.Effects.DropShadowEffect
            { Color = Colors.Black, BlurRadius = 14, ShadowDepth = 3, Opacity = 0.35 };
            rings[0].Effect = shadowFx;

            var face = new Grid();   // 最内环之上：罩色 + 高光罩
            pillTint = new Border();
            pillTint.CornerRadius = new CornerRadius(Math.Max(0.5, PH / 2 - N + 1));
            pillTint.Background = new SolidColorBrush(Color.FromArgb(10, 255, 255, 255));
            pillSheen = new Border();
            pillSheen.CornerRadius = new CornerRadius(Math.Max(0.5, PH / 2 - N + 1));
            face.Children.Add(pillTint);
            face.Children.Add(pillSheen);
            parent.Child = face;

            overlay.Children.Add(pill);
            root.Children.Add(overlay);

            hud = new TextBlock();
            hud.Foreground = Brushes.Yellow;
            hud.FontFamily = new FontFamily("Consolas");
            hud.FontSize = 13;
            hud.Margin = new Thickness(10, 6, 0, 0);
            hud.HorizontalAlignment = HorizontalAlignment.Left;
            hud.VerticalAlignment = VerticalAlignment.Top;
            hud.IsHitTestVisible = false;
            var hudBg = new Border();
            hudBg.Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0));
            hudBg.HorizontalAlignment = HorizontalAlignment.Left;
            hudBg.VerticalAlignment = VerticalAlignment.Top;
            hudBg.Child = hud;
            hudBg.IsHitTestVisible = false;
            root.Children.Add(hudBg);

            Content = root;

            MouseMove += delegate(object s, MouseEventArgs e) { mouse = e.GetPosition(root); };
            MouseLeftButtonDown += delegate { pressed = true; };
            MouseLeftButtonUp += delegate { pressed = false; };
            KeyDown += OnKey;
            SizeChanged += delegate { snap = null; brushesMode = -1; };
            CompositionTarget.Rendering += OnFrame;
            UpdateSheen();

            // 自画像：启动 2.5s 后把自身窗口落成 PNG（只拍自己，不碰屏幕其它窗口）
            var t = new DispatcherTimer();
            t.Interval = TimeSpan.FromMilliseconds(2500);
            t.Tick += delegate
            {
                t.Stop();
                try
                {
                    double w = root.ActualWidth, h = root.ActualHeight;
                    var rt = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
                    rt.Render(root);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rt));
                    using (var fs = System.IO.File.Create(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lab_self.png")))
                        enc.Save(fs);
                }
                catch { }
            };
            t.Start();
        }

        TextBlock MkText(string s, double size, FontWeight w, double x, double y)
        {
            var t = new TextBlock();
            t.Text = s;
            t.FontSize = size;
            t.FontWeight = w;
            t.Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x66, 0xFF));
            t.Margin = new Thickness(x, y, 0, 0);
            t.HorizontalAlignment = HorizontalAlignment.Left;
            t.VerticalAlignment = VerticalAlignment.Top;
            return t;
        }

        static LinearGradientBrush MakeEdge()
        {
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0); g.EndPoint = new Point(0.3, 1);
            g.GradientStops.Add(new GradientStop(Color.FromArgb(120, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(30, 255, 255, 255), 1));
            return g;
        }

        void UpdateSheen()
        {
            byte a = (byte)(sheenAlpha * 255);
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0); g.EndPoint = new Point(0, 1);
            g.GradientStops.Add(new GradientStop(Color.FromArgb(a, 255, 255, 255), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(a * 0.2), 255, 255, 255), 0.3));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(0, 255, 255, 255), 0.55));
            pillSheen.Background = g;
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.D1) mode = 1;
            else if (e.Key == Key.D2) mode = 2;
            else if (e.Key == Key.D3) mode = 3;
            else if (e.Key == Key.D4) mode = 4;   // 循环引用测试：VisualBrush(root)，pill 在被刷的视觉树里
            else if (e.Key == Key.Up) { sheenAlpha = Math.Min(0.5, sheenAlpha + 0.02); UpdateSheen(); return; }
            else if (e.Key == Key.Down) { sheenAlpha = Math.Max(0, sheenAlpha - 0.02); UpdateSheen(); return; }
            else if (e.Key == Key.Right) { rimPx = Math.Min(12, rimPx + 0.5); return; }
            else if (e.Key == Key.Left) { rimPx = Math.Max(0, rimPx - 0.5); return; }
            else if (e.Key == Key.X) { innerZoom = Math.Min(1.3, innerZoom + 0.02); return; }
            else if (e.Key == Key.Z) { innerZoom = Math.Max(0.8, innerZoom - 0.02); return; }
            else if (e.Key == Key.R) { snap = null; brushesMode = -1; return; }
            else return;
        }

        // ---------- 取景对齐：绝对坐标 + 按各环自身矩形 ----------
        void AlignBrush(Brush b, double offX, double offY, double w0, double h0, double expandPx, double zoom)
        {
            double x = pos.X + offX - expandPx, y = pos.Y + offY - expandPx;
            double w = w0 + 2 * expandPx, h = h0 + 2 * expandPx;
            if (Math.Abs(zoom - 1.0) > 0.001)
            {
                double shrink = 1.0 / zoom;
                double cx = x + w / 2, cy = y + h / 2;
                w *= shrink; h *= shrink;
                x = cx - w / 2; y = cy - h / 2;
            }
            if (b is VisualBrush) { ((VisualBrush)b).ViewboxUnits = BrushMappingMode.Absolute; ((VisualBrush)b).Viewbox = new Rect(x, y, w, h); }
            else if (b is ImageBrush) { ((ImageBrush)b).ViewboxUnits = BrushMappingMode.Absolute; ((ImageBrush)b).Viewbox = new Rect(x, y, w, h); }
        }

        void EnsureBrushes()
        {
            if (mode == 1 && snap == null)
            {
                double w = bgLayer.ActualWidth, h = bgLayer.ActualHeight;
                if (w < 10) return;
                var rt = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
                rt.Render(bgLayer);
                rt.Freeze();
                snap = rt;
                brushesMode = -1;
            }
            if (brushesMode == mode) return;
            if (mode == 1 && snap == null) return;
            var blur = mode == 3 ? new System.Windows.Media.Effects.BlurEffect { Radius = 6 } : null;
            for (int i = 0; i < N; i++)
            {
                Brush b;
                if (mode == 1) b = new ImageBrush(snap) { Stretch = Stretch.Fill };
                else if (mode == 4) b = new VisualBrush(root) { Stretch = Stretch.Fill };   // 循环：root 包含 pill 自己
                else b = new VisualBrush(bgLayer) { Stretch = Stretch.Fill };
                rings[i].Background = b;
                rings[i].Effect = mode == 3 ? blur : (i == 0 ? shadowFx : null);
            }
            brushesMode = mode;
        }

        void OnFrame(object s, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            double dt = lastFrame == TimeSpan.Zero ? 0.016 : (now - lastFrame).TotalSeconds;
            lastFrame = now;
            if (dt <= 0 || dt > 0.1) dt = 0.016;

            liveBlock.Opacity = 0.55 + 0.45 * Math.Sin(now.TotalSeconds * 3);   // 活块呼吸：证明活底实时

            // 弹簧跟随（子步进，k=260 d=22）
            double tx = mouse.X - PW / 2, ty = mouse.Y - PH / 2;
            double rem = dt;
            while (rem > 0)
            {
                double h = Math.Min(rem, 0.016);
                double ax = (tx - pos.X) * 260 - vel.X * 22;
                double ay = (ty - pos.Y) * 260 - vel.Y * 22;
                vel.X += ax * h; vel.Y += ay * h;
                pos.X += vel.X * h; pos.Y += vel.Y * h;
                rem -= h;
            }
            // 按压回弹弹簧
            double ptarget = pressed ? 1.10 : 1.0;
            double pa = (ptarget - pressScale) * 320 - pressVel * 18;
            pressVel += pa * dt;
            pressScale += pressVel * dt;

            // 果冻挤压：沿速度方向拉伸
            double speed = Math.Sqrt(vel.X * vel.X + vel.Y * vel.Y);
            double stretch = Math.Min(0.28, speed * 0.00045);
            double ang = speed > 30 ? Math.Atan2(vel.Y, vel.X) * 180 / Math.PI : 0;
            var tg = new TransformGroup();
            tg.Children.Add(new RotateTransform(ang));
            tg.Children.Add(new ScaleTransform((1 + stretch) * pressScale, (1 - stretch * 0.55) * pressScale));
            tg.Children.Add(new RotateTransform(-ang));
            pill.RenderTransform = tg;
            pill.RenderTransformOrigin = new Point(0.5, 0.5);
            pill.Margin = new Thickness(pos.X, pos.Y, 0, 0);
            pill.HorizontalAlignment = HorizontalAlignment.Left;
            pill.VerticalAlignment = VerticalAlignment.Top;

            // 逐环取景：外扩量从边缘 rimPx 平滑→0（(1-t)² 曲线），中心恒等无变形
            EnsureBrushes();
            for (int i = 0; i < N; i++)
            {
                var br = rings[i].Background;
                if (br == null) continue;
                double ex = rimPx * Math.Pow(1 - (i + 0.5) / N, 2);
                AlignBrush(br, i, i, PW - 2 * i, PH - 2 * i, ex, i == N - 1 ? innerZoom : 1.0);
            }

            hud.Text = "方案 " + mode + (mode == 1 ? " 静态快照(R重截)" : mode == 2 ? " 活底" : " 活底+模糊") +
                "  |  高光 " + sheenAlpha.ToString("F2") +
                "  折射 " + rimPx.ToString("F1") + "px" +
                "  中心缩放 " + innerZoom.ToString("F2") +
                "  速度 " + ((int)speed);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            new Application().Run(new LabWindow());
        }
    }
}
