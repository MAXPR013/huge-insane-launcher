// HugeLauncher.Ui - 流动史莱姆背景：壁纸同色系取色的 metaball 软团
// 不规则变形（角度正弦波）+ 滑肥皂式互相穿过 + 接触即交融 + 边缘新陈代谢（旧缩小/新挤入）；
// 阈值以下暗色垫底 = 铺满整屏。三层结构最底层；中层磨砂靠 Palette 半透明叠色（见 Widgets.cs Palette.Apply）
// 性能红线：场渲染锁 1/7 分辨率 + 远场免 atan2 早退 + ~30fps 跳帧；不可见/最小化/动画速度 0 时静止
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace HugeLauncher.Ui
{
    // 从桌面壁纸聚类主色：缩样 16x16 → 按饱和度过滤灰白黑 → 色相分桶取前 5 桶均值
    static class WallpaperPalette
    {
        public static List<Color> Colors;   // 取色结果（后台线程填，UI 线程读）

        // 后台线程调用。失败/壁纸太素 → null（调用方用主题色衍生兜底）
        public static List<Color> Extract()
        {
            string stage = "入口";
            try
            {
                stage = "读注册表";
                string path = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null) as string;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    // 纯色/幻灯片壁纸的真实文件在 TranscodedWallpaper（无扩展名，是 JPEG）
                    string tw = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        @"Microsoft\Windows\Themes\TranscodedWallpaper");
                    if (File.Exists(tw)) path = tw;
                }
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) { HilLog.Write("[史莱姆] 取色中止: 壁纸路径不存在"); return null; }

                stage = "解码 " + path;
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.UriSource = new Uri(path, UriKind.Absolute);
                bi.DecodePixelWidth = 24;   // 解码即缩样；24x24 给平滑渐变壁纸留出统计余量
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.EndInit();
                bi.Freeze();
                var conv = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                conv.Freeze();
                int w = conv.PixelWidth, h = conv.PixelHeight;
                stage = "取像素";
                if (w < 2 || h < 2) { HilLog.Write("[史莱姆] 取色中止: 解码尺寸 " + w + "x" + h); return null; }
                var px = new byte[w * h * 4];
                conv.CopyPixels(px, w * 4, 0);

                // 色相 12 桶：饱和度 >0.10、亮度 0.12~0.95 的像素入桶。门槛特意放低：
                // 素壁纸的暗粉/灰紫（饱和度 0.1~0.2）正是它的第二色系，收进来才有肉眼可分的两种色
                var sumR = new double[12]; var sumG = new double[12]; var sumB = new double[12];
                var cnt = new int[12];
                int colorful = BinInto(px, w, h, 0.10, sumR, sumG, sumB, cnt);
                if (colorful < 8) { HilLog.Write("[史莱姆] 取色中止: 壁纸太素 colorful=" + colorful); return null; }   // 壁纸太素(近纯灰)
                var order = new List<int>();
                for (int i = 0; i < 12; i++) if (cnt[i] > 0) order.Add(i);
                order.Sort(delegate(int a, int b2) { return cnt[b2].CompareTo(cnt[a]); });
                var result = new List<Color>();
                for (int i = 0; i < order.Count && result.Count < 5; i++)
                {
                    int bin = order[i];
                    if (cnt[bin] * 40 < colorful) continue;   // 占比不足 2.5% 的桶不要
                    result.Add(Color.FromRgb(
                        (byte)(sumR[bin] / cnt[bin]),
                        (byte)(sumG[bin] / cnt[bin]),
                        (byte)(sumB[bin] / cnt[bin])));
                }
                // 只剩 1 桶也放行：KeepFamily 会贴着它 ±20° 内衍生补足（同色系，色差收敛）
                if (result.Count < 1) { HilLog.Write("[史莱姆] 取色中止: 桶为空 colorful=" + colorful); return null; }
                return result;
            }
            catch (Exception ex)
            {
                HilLog.Write("[史莱姆] 壁纸取色异常@" + stage + ": " + ex.GetType().Name + " " + ex.Message);
                return null;
            }
        }

        // 把像素按色相扫进 12 桶：亮度太极端（<0.12 / >0.95）的不要，返回入桶彩色像素数
        static int BinInto(byte[] px, int w, int h, double sMin,
            double[] sumR, double[] sumG, double[] sumB, int[] cnt)
        {
            int colorful = 0;
            for (int i = 0; i < w * h; i++)
            {
                byte b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
                double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
                double mx = Math.Max(rf, Math.Max(gf, bf)), mn = Math.Min(rf, Math.Min(gf, bf));
                double v = mx, s = mx < 0.001 ? 0 : (mx - mn) / mx;
                if (s < sMin || v < 0.12 || v > 0.95) continue;
                double hue = HueOf(rf, gf, bf, mx, mn);
                int bin = Math.Min(11, (int)(hue / 30.0));
                sumR[bin] += r; sumG[bin] += g; sumB[bin] += b; cnt[bin]++;
                colorful++;
            }
            return colorful;
        }

        static double HueOf(double r, double g, double b, double mx, double mn)
        {
            double d = mx - mn, h;
            if (d < 0.0001) return 0;
            if (mx == r) h = 60 * (((g - b) / d) % 6);
            else if (mx == g) h = 60 * ((b - r) / d + 2);
            else h = 60 * ((r - g) / d + 4);
            if (h < 0) h += 360;
            return h;
        }

        // 兜底：主题色明暗衍生 4 色（同色相，经过同色系保留补满 5 个）
        public static List<Color> DeriveFrom(Color c)
        {
            var l = new List<Color>();
            l.Add(c);
            l.Add(Palette.Lighten(c, 0.30));
            l.Add(Color.FromRgb((byte)(c.R * 0.55), (byte)(c.G * 0.55), (byte)(c.B * 0.55)));
            l.Add(Color.FromRgb((byte)(255 - (255 - c.R) * 0.4), (byte)(255 - (255 - c.G) * 0.4), (byte)(255 - (255 - c.B) * 0.4)));
            return KeepFamily(l);
        }

        // 同族保留 + 可辨识补位：壁纸取到什么用什么，只去掉几乎一样的重复色；
        // 不够 5 个就绕主色 ±30~55° 衍生——保证至少两种肉眼可分的颜色，但仍是类似色和谐（用户：区别大一点，但不要彩虹）
        public static List<Color> KeepFamily(List<Color> src)
        {
            var outp = new List<Color>();
            var hues = new List<double>();
            for (int i = 0; i < src.Count && outp.Count < 5; i++)
            {
                double h, s, v;
                RgbToHsv(src[i], out h, out s, out v);
                if (s < 0.18) s = 0.26;   // 太灰抬点饱和度，不然糊成一坨
                if (v < 0.20) v = 0.32;   // 太暗抬点亮度
                bool dup = false;
                for (int k = 0; k < hues.Count; k++)
                {
                    double d = Math.Abs(hues[k] - h);
                    if (d > 180) d = 360 - d;
                    if (d < 10) { dup = true; break; }   // 只去"几乎一样"的重
                }
                if (dup) continue;
                outp.Add(HsvToRgb(h, s, v));
                hues.Add(h);
            }
            // 贴着主色补位，但拉开到肉眼可辨：优先 ±30~55° 的邻近族（类似色和谐，非彩虹），再补同族明暗
            double h0 = 0, s0 = 0.5, v0 = 0.6;
            if (outp.Count > 0) RgbToHsv(outp[0], out h0, out s0, out v0);
            double[] offs = { 38, -32, 16, -14, 55, -52, 24 };
            for (int oi = 0; oi < offs.Length && outp.Count < 5; oi++)
            {
                double h = (h0 + offs[oi] + 360) % 360;
                bool ok = true;
                for (int k = 0; k < hues.Count; k++)
                {
                    double d = Math.Abs(hues[k] - h);
                    if (d > 180) d = 360 - d;
                    if (d < 8) { ok = false; break; }
                }
                if (!ok) continue;
                double vv = (outp.Count % 2 == 0) ? Math.Min(0.92, v0 * 1.15) : v0 * 0.85;   // 明暗交替
                outp.Add(HsvToRgb(h, s0, vv));
                hues.Add(h);
            }
            return outp;
        }

        internal static void RgbToHsv(Color c, out double h, out double s, out double v)
        {
            double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
            double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b));
            v = mx;
            s = mx < 0.0001 ? 0 : (mx - mn) / mx;
            h = HueOf(r, g, b, mx, mn);
        }

        internal static Color HsvToRgb(double h, double s, double v)
        {
            int i = (int)(h / 60) % 6; if (i < 0) i += 6;
            double f = h / 60 - Math.Floor(h / 60);
            double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
            double r, g, b;
            switch (i)
            {
                case 0: r = v; g = t; b = p; break;
                case 1: r = q; g = v; b = p; break;
                case 2: r = p; g = v; b = t; break;
                case 3: r = p; g = q; b = v; break;
                case 4: r = t; g = p; b = v; break;
                default: r = v; g = p; b = q; break;
            }
            return Color.FromRgb((byte)(r * 255 + 0.5), (byte)(g * 255 + 0.5), (byte)(b * 255 + 0.5));
        }
    }

    static class Slime
    {
        public static bool On = true;
        public static double BlurBg = 8;      // 背景模糊半径 px
        public static int FrostMid = 60;      // 中层（瓷砖/输入框/下拉框）不透明度 %
        public static double BlurFore = 0;    // 前景文字柔化 px（0 = 不糊）
        public static SlimeView Current;      // 主窗里活着的那块画布（滑条即时调模糊用）

        public static event EventHandler Changed;   // 开关/参数变了（主窗订阅重建）

        public static void FireChanged()
        {
            var h = Changed;
            if (h != null) h(null, EventArgs.Empty);
        }

        // 后台线程取壁纸色，完成回 UI 线程刷新（幂等，进行中再来就排队一次）
        static int busy;
        public static void RefreshColorsAsync()
        {
            if (Interlocked.Exchange(ref busy, 1) != 0) return;
            var th = new Thread(new ThreadStart(delegate
            {
                var cols = WallpaperPalette.Extract();
                Interlocked.Exchange(ref busy, 0);
                HilLog.Write("[史莱姆] 壁纸取色: " + (cols == null ? "null(用主题色兜底)" : cols.Count + " 色, 首色 #" + cols[0].ToString()));
                if (cols == null) return;   // 取不到保持现状（衍生色已在用）
                Application.Current.Dispatcher.BeginInvoke(new Action(delegate
                {
                    WallpaperPalette.Colors = WallpaperPalette.KeepFamily(cols);   // 同色系：贴着壁纸色走，色差收敛
                    // 只给活着的画布换色，绝不 FireChanged 重建整套 UI——
                    // 重建会把用户正在滚动的位置清回顶部（"往下滚过一会儿自动回顶部"的元凶）
                    if (Slime.Current != null) Slime.Current.Recolor();
                }));
            }));
            th.IsBackground = true;
            th.Start();
        }
    }

    // 史莱姆画布：低分辨率势能场 → WriteableBitmap → 双线性放大铺满（放大即免费模糊）
    class SlimeView : Image
    {
        class Ball
        {
            public double X, Y, Vx, Vy, R2; public Color C;
            // 变形：有效半径随角度起伏 eff(θ)=R·(1+A1·sin(N1·θ+P1)+A2·sin(N2·θ+P2))，
            // 相位缓慢转动 = 持续变形的不规则软团（变形虫，非正圆）
            public double A1, A2, P1, P2, W1, W2;
            public int N1, N2;
            // 新陈代谢：Age>Life 后 Scale 缩到 0 退场，新球 Scale 0→1 从边缘挤进来
            public double Age, Life, Scale;
        }

        const int Downscale = 7;         // 超采样：1/7 分辨率场渲染（变形要算 atan2，降档保 30fps；模糊照常盖住颗粒）
        const double Threshold = 0.9;    // 场强阈值：>它才算球体内部
        Ball[] balls;
        WriteableBitmap bmp;
        byte[] buf;
        int bw, bh;
        DateTime lastFrame = DateTime.MinValue;
        readonly Random rnd = new Random();
        bool hooked;

        // Image 基类在 Source==null 时 ArrangeOverride 返回 0x0 → ActualWidth 恒 0 → SizeChanged 永不触发（死锁）
        // 史莱姆画布必须占满网格槽位，尺寸变化由 SizeChanged → Rebuild 正常驱动
        protected override Size ArrangeOverride(Size arrangeSize)
        {
            base.ArrangeOverride(arrangeSize);
            return arrangeSize;
        }

        public SlimeView()
        {
            Stretch = Stretch.Fill;
            IsHitTestVisible = false;
            var blur = new BlurEffect();
            blur.Radius = Slime.BlurBg;
            Effect = blur;
            Slime.Current = this;
            Loaded += delegate { Hook(); };
            Unloaded += delegate { Unhook(); if (Slime.Current == this) Slime.Current = null; };
            SizeChanged += delegate
            {
                // 跟随主窗圆角（12），色块别溢出成方角
                Clip = new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight), 12, 12);
                Rebuild();
            };
            IsVisibleChanged += delegate { Hook(); };
        }

        void Hook()
        {
            if (hooked || !IsVisible) return;
            hooked = true;
            lastFrame = DateTime.MinValue;
            CompositionTarget.Rendering += OnFrame;
        }

        void Unhook()
        {
            if (!hooked) return;
            hooked = false;
            CompositionTarget.Rendering -= OnFrame;
        }

        void Rebuild()
        {
            int w = Math.Max(16, (int)(ActualWidth / Downscale));
            int h = Math.Max(16, (int)(ActualHeight / Downscale));
            if (w == bw && h == bh && bmp != null) return;
            bw = w; bh = h;
            buf = new byte[bw * bh * 4];
            bmp = new WriteableBitmap(bw, bh, 96, 96, PixelFormats.Bgra32, null);
            Source = bmp;
            Hook();   // 兜底：若此前 OnFrame 因 bmp==null 停过，画布建好后确保渲染回路活着
            var colors = WallpaperPalette.Colors ?? WallpaperPalette.DeriveFrom(ThemeManager.CurrentColor);
            int n = Math.Min(8, Math.Max(6, colors.Count + 2));
            balls = new Ball[n];
            for (int i = 0; i < n; i++)
            {
                var b = NewBall(Tone(colors[i % colors.Count]));
                b.X = rnd.NextDouble() * bw;
                b.Y = rnd.NextDouble() * bh;
                b.Age = rnd.NextDouble() * b.Life * 0.9;   // 寿命错开，不会齐刷刷一起换
                balls[i] = b;
            }
        }

        // 新球基础参数：大半径（铺满的前提）、双倍初速、随机变形波、随机寿命
        Ball NewBall(Color c)
        {
            double rr = Math.Min(bw, bh) * (0.26 + rnd.NextDouble() * 0.16);
            return new Ball
            {
                Vx = (rnd.NextDouble() - 0.5) * bw * 0.10,
                Vy = (rnd.NextDouble() - 0.5) * bh * 0.10,
                R2 = rr * rr,
                C = c,
                A1 = 0.14 + rnd.NextDouble() * 0.10,   // 主波：14~24% 半径起伏
                A2 = 0.05 + rnd.NextDouble() * 0.07,   // 次波：5~12%，叠出棱角感
                P1 = rnd.NextDouble() * 6.283,
                P2 = rnd.NextDouble() * 6.283,
                W1 = (0.08 + rnd.NextDouble() * 0.10) * (rnd.Next(2) == 0 ? -1.0 : 1.0),
                W2 = (0.10 + rnd.NextDouble() * 0.14) * (rnd.Next(2) == 0 ? -1.0 : 1.0),
                N1 = 2 + rnd.Next(2),   // 2~3 瓣
                N2 = 3 + rnd.Next(3),   // 3~5 瓣
                Age = 0,
                Life = 18 + rnd.NextDouble() * 22,   // 18~40 秒寿命
                Scale = 1,
            };
        }

        // 寿终球原地重生：从随机一条边的边缘外挤进来（朝内初速 + Scale 从 0 长起 + 换新色）
        void RespawnAtEdge(Ball b)
        {
            var colors = WallpaperPalette.Colors ?? WallpaperPalette.DeriveFrom(ThemeManager.CurrentColor);
            var nb = NewBall(Tone(colors[rnd.Next(colors.Count)]));
            b.Vy = nb.Vy; b.R2 = nb.R2; b.C = nb.C;
            b.A1 = nb.A1; b.A2 = nb.A2; b.P1 = nb.P1; b.P2 = nb.P2;
            b.W1 = nb.W1; b.W2 = nb.W2; b.N1 = nb.N1; b.N2 = nb.N2;
            b.Age = 0; b.Life = nb.Life; b.Scale = 0.05;
            b.Vx = nb.Vx;
            double sp = Math.Min(bw, bh) * (0.06 + rnd.NextDouble() * 0.05);   // 向内初速
            switch (rnd.Next(4))
            {
                case 0: b.X = -Math.Sqrt(b.R2) * 0.3; b.Y = rnd.NextDouble() * bh; b.Vx = sp; b.Vy = (rnd.NextDouble() - 0.5) * sp; break;
                case 1: b.X = bw + Math.Sqrt(b.R2) * 0.3; b.Y = rnd.NextDouble() * bh; b.Vx = -sp; b.Vy = (rnd.NextDouble() - 0.5) * sp; break;
                case 2: b.Y = -Math.Sqrt(b.R2) * 0.3; b.X = rnd.NextDouble() * bw; b.Vy = sp; b.Vx = (rnd.NextDouble() - 0.5) * sp; break;
                default: b.Y = bh + Math.Sqrt(b.R2) * 0.3; b.X = rnd.NextDouble() * bw; b.Vy = -sp; b.Vx = (rnd.NextDouble() - 0.5) * sp; break;
            }
        }

        void OnFrame(object sender, EventArgs e)
        {
            if (!Slime.On) { Unhook(); return; }
            if (bmp == null) return;   // 尺寸未就绪：等 SizeChanged 的 Rebuild，不能 Unhook（之后没人再 Hook 回来）
            var now = DateTime.UtcNow;
            double dt = (now - lastFrame).TotalSeconds;
            if (dt < 0.033) return;   // ~30fps
            lastFrame = now;
            double speed = ThemeManager.Speed;
            if (speed > 0.01) Step(Math.Min(dt, 0.05) * speed);   // 动画速度=0 → 静止磨砂壁纸
            Paint();
        }

        // 色球明度随明暗模式调校：深色模式整体压暗（亮团压成暗彩——"标题条泛白"的修法：
        // 浅壁纸取色+明暗交替补位能到 0.92 明度，核心再提亮 1.35 倍≈纯白，半透明压色罩盖不住）；
        // 亮色模式整体抬亮保通透。只缩放明度、不动色相饱和度，壁纸色系与明暗相对关系保留
        static Color Tone(Color c)
        {
            double h, s, v;
            WallpaperPalette.RgbToHsv(c, out h, out s, out v);
            v = Palette.Light ? 0.55 + v * 0.40 : v * 0.55;
            return WallpaperPalette.HsvToRgb(h, s, v);
        }

        // 壁纸色到手后原地换色（位置/速度不动，视觉连续）；无球则等 Rebuild 自然取用
        public void Recolor()
        {
            var bs = balls;
            if (bs == null) return;
            var colors = WallpaperPalette.Colors ?? WallpaperPalette.DeriveFrom(ThemeManager.CurrentColor);
            for (int i = 0; i < bs.Length; i++) bs[i].C = Tone(colors[i % colors.Count]);
        }

        void Step(double dt)
        {
            // 滑肥皂：弱向心 + 轻扰动 + 粘性阻尼 → 匀速长滑行（基础速度为初版两倍，但走直线滑翔，不乱窜）
            for (int i = 0; i < balls.Length; i++)
            {
                var b = balls[i];
                b.Vx += (bw * 0.5 - b.X) * 0.012 * dt + (rnd.NextDouble() - 0.5) * bw * 0.016 * dt;
                b.Vy += (bh * 0.5 - b.Y) * 0.012 * dt + (rnd.NextDouble() - 0.5) * bh * 0.016 * dt;
                double damp = 1 - 0.09 * dt;   // 阻尼略降 = 惯性更长，滑行更"溜"
                b.Vx *= damp; b.Vy *= damp;
                // 变形相位缓转 + 新陈代谢（退场收缩 / 入场长大）
                b.P1 += b.W1 * dt; b.P2 += b.W2 * dt;
                b.Age += dt;
                if (b.Age > b.Life)
                {
                    b.Scale -= dt / 4.0;   // 4 秒缩没
                    if (b.Scale <= 0.02) RespawnAtEdge(b);
                }
                else if (b.Scale < 1)
                {
                    b.Scale += dt / 3.0;   // 3 秒挤进来长全
                    if (b.Scale > 1) b.Scale = 1;
                }
            }
            // 极弱防坍缩：心距 < 0.45×有效半径和 才轻推——允许互相穿过"溜过去"，只是不全堆成一坨
            for (int i = 0; i < balls.Length; i++)
            {
                for (int j = i + 1; j < balls.Length; j++)
                {
                    var a = balls[i]; var c = balls[j];
                    double dx = c.X - a.X, dy = c.Y - a.Y;
                    double rr = (Math.Sqrt(a.R2) * a.Scale + Math.Sqrt(c.R2) * c.Scale) * 0.45;
                    double d2 = dx * dx + dy * dy;
                    if (d2 >= rr * rr || d2 < 0.0001) continue;
                    double d = Math.Sqrt(d2);
                    double push = (rr - d) / rr * 0.12;   // 软推，顶不开就切向滑走
                    double fx = dx / d * push * bw * dt, fy = dy / d * push * bh * dt;
                    a.Vx -= fx; a.Vy -= fy;
                    c.Vx += fx; c.Vy += fy;
                }
            }
            for (int i = 0; i < balls.Length; i++)
            {
                var b = balls[i];
                b.X += b.Vx * dt; b.Y += b.Vy * dt;
                double m = Math.Sqrt(b.R2) * Math.Max(b.Scale, 0.05) * 0.5;
                if (b.X < -m) { b.X = -m; b.Vx = Math.Abs(b.Vx); }
                if (b.X > bw + m) { b.X = bw + m; b.Vx = -Math.Abs(b.Vx); }
                if (b.Y < -m) { b.Y = -m; b.Vy = Math.Abs(b.Vy); }
                if (b.Y > bh + m) { b.Y = bh + m; b.Vy = -Math.Abs(b.Vy); }
            }
        }

        void Paint()
        {
            // 基底：窗口底色（深色模式深色/亮色模式浅色），球色从壁纸来
            byte baseR = Palette.BgBottom.R, baseG = Palette.BgBottom.G, baseB = Palette.BgBottom.B;
            var bs = balls;
            int idx = 0;
            for (int y = 0; y < bh; y++)
            {
                for (int x = 0; x < bw; x++)
                {
                    // 场强：每球 有效r²/d²（有效半径随角度起伏=变形虫），取前二名（颜色混合用）
                    double f1 = 0, f2 = 0; int i1 = -1, i2 = -1;
                    for (int i = 0; i < bs.Length; i++)
                    {
                        var bb = bs[i];
                        double dx = x - bb.X, dy = y - bb.Y;
                        double d2 = dx * dx + dy * dy;
                        double rE2 = bb.R2 * bb.Scale * bb.Scale;
                        if (rE2 < 0.01) continue;   // 缩没的球不参与
                        double f;
                        if (d2 > rE2 * 9)
                        {
                            f = rE2 / (d2 + 1);   // 远场：忽略变形，省 atan2（形变在这么远的距离已不可见）
                        }
                        else
                        {
                            double ang = Math.Atan2(dy, dx);
                            double sh = 1 + bb.A1 * Math.Sin(bb.N1 * ang + bb.P1)
                                          + bb.A2 * Math.Sin(bb.N2 * ang + bb.P2);
                            f = rE2 * sh * sh / (d2 + 1);
                        }
                        if (f > f1) { f2 = f1; i2 = i1; f1 = f; i1 = i; }
                        else if (f > f2) { f2 = f; i2 = i; }
                    }
                    byte r, g, b2;
                    if (i1 < 0)
                    {
                        r = baseR; g = baseG; b2 = baseB;
                    }
                    else
                    {
                        Color c1 = bs[i1].C;
                        Color c2 = bs[i2 >= 0 ? i2 : i1].C;
                        // 接触即交融：场强比 0.05 就起混，接触面（f2≈f1）直达 50/50 真混合；
                        // 上限 0.5 是关键——跨过中线时 i1/i2 对调，0.5A+0.5B = 0.5B+0.5A，混合连续不断裂
                        double mix = 0;
                        if (i2 >= 0 && f1 > 0.0001)
                        {
                            double mm = (f2 / f1 - 0.05) / 0.80;
                            if (mm < 0) mm = 0; if (mm > 1) mm = 1;
                            mm = mm * mm * (3 - 2 * mm);
                            mix = mm * 0.5;
                        }
                        double cr = c1.R + (c2.R - c1.R) * mix;
                        double cg = c1.G + (c2.G - c1.G) * mix;
                        double cb = c1.B + (c2.B - c1.B) * mix;
                        // 软边：宽带 smoothstep 过渡（抗锯齿：低分辨率场放大后边缘无阶梯）；
                        // 阈值以下垫 0.22 权重（铺满：任何角落都有颜色流动，亮团浮在暗色流动底上）
                        double t = (f1 - Threshold * 0.55) / (Threshold * 0.95);
                        if (t > 1) t = 1; if (t < 0) t = 0;
                        t = t * t * (3 - 2 * t);   // smoothstep：两端导数为 0，明暗无折线感
                        t = 0.22 + 0.78 * t;
                        // 核心提亮：场越强越亮（肉感），阈值以下不提
                        double glow = f1 > Threshold ? 1 + Math.Min(0.35, (f1 - Threshold) * 0.18) : 1;
                        r = (byte)Math.Min(255, (baseR + (cr * glow - baseR) * t));
                        g = (byte)Math.Min(255, (baseG + (cg * glow - baseG) * t));
                        b2 = (byte)Math.Min(255, (baseB + (cb * glow - baseB) * t));
                    }
                    // ±1 静态抖动：打散 8bit 色带（暗色大面积渐变经模糊后易出环状条纹）
                    int dth = ((x * 7 + y * 13) & 3) - 1;
                    int rr2 = r + dth, gg2 = g + dth, bb2 = b2 + dth;
                    if (rr2 < 0) rr2 = 0; else if (rr2 > 255) rr2 = 255;
                    if (gg2 < 0) gg2 = 0; else if (gg2 > 255) gg2 = 255;
                    if (bb2 < 0) bb2 = 0; else if (bb2 > 255) bb2 = 255;
                    buf[idx++] = (byte)bb2; buf[idx++] = (byte)gg2; buf[idx++] = (byte)rr2; buf[idx++] = 255;
                }
            }
            bmp.WritePixels(new Int32Rect(0, 0, bw, bh), buf, bw * 4, 0);
        }
    }
}
