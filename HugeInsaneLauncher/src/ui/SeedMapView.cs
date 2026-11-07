// HugeLauncher.Ui - 种子地图：cubiomes 本地引擎 + 可拖动缩放瓦片 + 结构标记
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HugeLauncher.Ui
{
    static class SeedEngine
    {
        static bool ready;
        static bool failed;

        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_version_newest();
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_setup(int mc, ulong seed);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_biome(int scale, int x, int z);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern void hil_tile(int scale, int x0, int z0, int w, int h, int[] outp);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_biome_color(int biome);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_structure_pos(int t, int rx, int rz, out int px, out int pz);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_struct_constant(int which);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_region_size(int t);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_scan_structures(int t, int x0, int z0, int x1, int z1, int maxOut, int[] outp);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_slime_chunk(int cx, int cz);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_spawn(out int px, out int pz);
        [DllImport("cubiomes", CallingConvention = CallingConvention.Cdecl)]
        public static extern int hil_strongholds(int maxCount, int[] outp);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool SetDllDirectory(string path);

        public static bool Ensure()
        {
            if (ready) return true;
            if (failed) return false;
            try
            {
                string dir = Path.Combine(Paths.LauncherDir, "native");
                Directory.CreateDirectory(dir);
                string dll = Path.Combine(dir, "cubiomes.dll");
                var asm = Assembly.GetExecutingAssembly();
                using (var s = asm.GetManifestResourceStream("cubiomes.dll"))
                {
                    if (s != null)
                    {
                        bool need = true;
                        try { need = !File.Exists(dll) || new FileInfo(dll).Length != s.Length; } catch { }
                        if (need)
                        {
                            using (var fs = new FileStream(dll, FileMode.Create, FileAccess.Write))
                                s.CopyTo(fs);
                        }
                    }
                }
                if (!File.Exists(dll)) { failed = true; return false; }
                SetDllDirectory(dir);
                int v = hil_version_newest();
                ready = v > 0;
                return ready;
            }
            catch { failed = true; return false; }
        }
    }

    class MapMarker
    {
        public int X, Z;
        public string Name;
        public Color Color;
    }

    class SeedMapView : Grid
    {
        Image img;
        Canvas overlay;
        Border hud;
        TextBlock hudText;
        int size = 512;
        double centerX, centerZ;
        int scale = 4;
        // 细分缩放级别：滚轮一格一档，不再 4→16→64 三档跳
        // 等比生成：每档固定约 1.45 倍，最低 2 格/像素、最高 128，跨度手感一致
        static readonly int[] ZoomSteps = BuildZoomSteps();
        // 12 档，从 2 格/像素到 128 格/像素等比分布（每档都是约 1.47 倍，末档也一致）
        static int[] BuildZoomSteps()
        {
            const int n = 12;
            double lo = 2.0, hi = 128.0;
            double ratio = Math.Pow(hi / lo, 1.0 / (n - 1));
            var l = new List<int>();
            for (int i = 0; i < n; i++)
            {
                int v = (int)Math.Round(lo * Math.Pow(ratio, i));
                if (l.Count == 0 || v != l[l.Count - 1]) l.Add(v);
            }
            if (l[l.Count - 1] != (int)hi) l.Add((int)hi);
            return l.ToArray();
        }
        Point dragStart;
        Point lastMouse;
        bool dragging;
        bool busy;
        int seq;
        ulong seed;
        int imgW, imgH, imgX0, imgZ0;
        readonly List<MapMarker> markers = new List<MapMarker>();
        readonly bool[] enabled = { true, true, true, true, true, true, true, true };
        static readonly string[] ExtraNames = { "要塞", "出生点" };
        static readonly Color[] ExtraColors = { Color.FromRgb(0x6E, 0x7B, 0xFF), Color.FromRgb(0xFF, 0x5C, 0x8A) };
        Border legend;
        TranslateTransform dragTf;
        double dragDx, dragDy;

        static readonly int[] StructWhich = { 0, 5, 6, 7, 12, 8 };
        static readonly string[] StructNames = { "村庄", "海底神殿", "林地府邸", "掠夺者前哨", "远古城市", "废弃传送门" };
        static readonly Color[] StructColors =
        {
            Color.FromRgb(0xE8, 0xC5, 0x47),
            Color.FromRgb(0x4A, 0xC6, 0xD6),
            Color.FromRgb(0x8E, 0x6B, 0xD6),
            Color.FromRgb(0xE0, 0x7B, 0x4A),
            Color.FromRgb(0xB0, 0xB0, 0xC0),
            Color.FromRgb(0x9B, 0x6B, 0xD6)
        };

        public SeedMapView(ulong seed, double spawnX, double spawnZ)
        {
            this.seed = seed;
            centerX = spawnX;
            centerZ = spawnZ;
            ClipToBounds = true;
            Background = Brushes.Transparent;   // 没有背景的 Grid 收不到鼠标事件
            dragTf = new TranslateTransform();

            img = new Image();
            img.Stretch = Stretch.Fill;
            img.IsHitTestVisible = false;
            Children.Add(img);

            overlay = new Canvas();
            overlay.IsHitTestVisible = true;
            Children.Add(overlay);

            hud = new Border();
            hud.Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
            hud.CornerRadius = new CornerRadius(5);
            hud.Padding = new Thickness(7, 3, 7, 4);
            hud.Margin = new Thickness(8);
            hud.HorizontalAlignment = HorizontalAlignment.Left;
            hud.VerticalAlignment = VerticalAlignment.Top;
            hud.IsHitTestVisible = false;
            hudText = new TextBlock();
            hudText.FontSize = 10.5;
            hudText.Foreground = new SolidColorBrush(Color.FromArgb(240, 255, 255, 255));
            hud.Child = hudText;
            Children.Add(hud);
            DrawLegend();

            MouseLeftButtonDown += delegate(object o, MouseButtonEventArgs e)
            {
                dragging = true;
                dragStart = e.GetPosition(this);
                dragDx = dragDy = 0;
                dragTf.X = dragTf.Y = 0;
                img.RenderTransform = dragTf;
                overlay.RenderTransform = dragTf;
                CaptureMouse();
                Cursor = Cursors.SizeAll;
            };
            MouseMove += delegate(object o, MouseEventArgs e)
            {
                lastMouse = e.GetPosition(this);
                if (dragging)
                {
                    // 拖动时只平移现有画面，松手才重算，避免每帧重画一张图
                    dragDx = lastMouse.X - dragStart.X;
                    dragDy = lastMouse.Y - dragStart.Y;
                    dragTf.X = dragDx;
                    dragTf.Y = dragDy;
                    return;
                }
                UpdateHud();
            };
            MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs e)
            {
                dragging = false;
                Cursor = Cursors.Arrow;
                ReleaseMouseCapture();
                if (Math.Abs(dragDx) > 3 || Math.Abs(dragDy) > 3)
                {
                    centerX -= dragDx * scale;
                    centerZ -= dragDy * scale;
                }
                img.RenderTransform = null;
                overlay.RenderTransform = null;
                bool wasDrag = Math.Abs(dragDx) > 3 || Math.Abs(dragDy) > 3;
                dragDx = dragDy = 0;
                if (!wasDrag) CopyTpAt(e.GetPosition(this));
                Render();
            };
            MouseWheel += delegate(object o, MouseWheelEventArgs e)
            {
                int idx = 0;
                for (int i = 0; i < ZoomSteps.Length; i++) if (ZoomSteps[i] == scale) { idx = i; break; }
                idx += e.Delta > 0 ? -1 : 1;
                if (idx < 0) idx = 0;
                if (idx >= ZoomSteps.Length) idx = ZoomSteps.Length - 1;
                scale = ZoomSteps[idx];
                Render();
                e.Handled = true;
            };
            SizeChanged += delegate { Render(); };
        }

        public int Scale { get { return scale; } }
        public void SetSeed(ulong s) { seed = s; markers.Clear(); Render(); }

        // 点一下地图：把该点的 /tp 指令复制到剪贴板（不拖动才算点击）
        void CopyTpAt(Point p)
        {
            if (imgW <= 0 || imgH <= 0 || ActualWidth <= 1 || ActualHeight <= 1) return;
            int wx = imgX0 + (int)(p.X / ActualWidth * imgW) * scale;
            int wz = imgZ0 + (int)(p.Y / ActualHeight * imgH) * scale;
            string cmd = "/tp " + wx + " ~ " + wz;
            try { Clipboard.SetText(cmd); } catch { }
            if (hudText != null)
            {
                hudText.Text = "已复制  " + cmd;
                var timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(1400);
                timer.Tick += delegate { timer.Stop(); UpdateHud(); };
                timer.Start();
            }
        }
        void UpdateHud()
        {
            if (hudText == null) return;
            int wx = imgX0 + (int)(lastMouse.X / Math.Max(1, ActualWidth) * imgW) * scale;
            int wz = imgZ0 + (int)(lastMouse.Y / Math.Max(1, ActualHeight) * imgH) * scale;
            hudText.Text = "X " + wx + "   Z " + wz + "   ·   1 像素 = " + scale + " 格   ·   标记 " + markers.Count + "   ·   滚轮缩放 / 拖动平移";
        }

        // ===== 分块缓存 + 后台预取 =====
        // 每块 256×256 像素，按 (缩放, 块X, 块Z) 缓存；可见块缺了就后台算，
        // 算好一块立刻贴上去，缩放/平移优先命中缓存，所以不卡。
        const int TilePx = 256;        // 一块占 256×256 显示像素
        const int SampleScale = 2;     // 每 2×2 显示像素采样一次（生物群系 4 格一个单位，看不出差）
        const int Samples = TilePx / SampleScale;
        static string TileDir { get { return System.IO.Path.Combine(Paths.LauncherDir, "cache", "seedmap"); } }
        readonly Dictionary<long, int[]> tiles = new Dictionary<long, int[]>();
        readonly HashSet<long> queued = new HashSet<long>();
        static readonly object hilLock = new object();
        static int prepWorker;   // 0=空闲 1=有线程在跑
        readonly Queue<long> prepQueue = new Queue<long>();
        WriteableBitmap wbmp;
        int[] lastBuf;               // 上一帧整图（缩放瞬间先拉伸顶住，避免空白重载感）
        int lastX0, lastZ0, lastSc, lastW, lastH;
        int curSeq;

        static long TileKey(int sc, int tx, int tz)
        {
            return ((long)sc << 40) ^ ((long)(tx & 0xFFFFF) << 20) ^ (long)(tz & 0xFFFFF);
        }

        // 入队：可见块优先，只预热相邻 ±1 级（最多 4 块）；视野变化会清掉过期任务
        void EnsureTiles(int x0, int z0, int w, int h, int sc)
        {
            int tw = w * sc, th = h * sc;
            int tx0 = FloorDiv(x0, TilePx * sc), tx1 = FloorDiv(x0 + tw - 1, TilePx * sc);
            int tz0 = FloorDiv(z0, TilePx * sc), tz1 = FloorDiv(z0 + th - 1, TilePx * sc);

            // 视野变了：把还没算的排队任务全丢掉（否则会乖乖算完几十块过期瓦片）
            lock (prepQueue) { prepQueue.Clear(); queued.Clear(); }

            for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                    RequestTile(sc, tx, tz, false);

            // 相邻缩放级别预热（±1 级，各最多 2 块）
            int idx = IndexOfScale(sc);
            for (int d = -1; d <= 1; d += 2)
            {
                int ii = idx + d;
                if (ii < 0 || ii >= ZoomSteps.Length) continue;
                int other = ZoomSteps[ii];
                int ox0 = FloorDiv(x0, TilePx * other), ox1 = FloorDiv(x0 + tw - 1, TilePx * other);
                int oz0 = FloorDiv(z0, TilePx * other), oz1 = FloorDiv(z0 + th - 1, TilePx * other);
                int n = 0;
                for (int tz = oz0; tz <= oz1 && n < 2; tz++)
                    for (int tx = ox0; tx <= ox1 && n < 2; tx++)
                    {
                        RequestTile(other, tx, tz, true);
                        n++;
                    }
            }
            KickPrep();
        }

        static string TilePath(ulong sd, int sc, int tx, int tz)
        {
            return System.IO.Path.Combine(TileDir, sd.ToString("x16") + "_" + sc + "_" + tx + "_" + tz + ".bin");
        }

        // 先查内存 → 再查磁盘 → 都没有才排队算
        void RequestTile(int sc, int tx, int tz, bool lowPriority)
        {
            long k = TileKey(sc, tx, tz);
            if (tiles.ContainsKey(k) || queued.Contains(k)) return;
            int[] disk = LoadTileFromDisk(sc, tx, tz);
            if (disk != null)
            {
                tiles[k] = disk;
                return;
            }
            queued.Add(k);
            lock (prepQueue) prepQueue.Enqueue(k);
        }

        int[] LoadTileFromDisk(int sc, int tx, int tz)
        {
            try
            {
                string p = TilePath(seed, sc, tx, tz);
                if (!System.IO.File.Exists(p)) return null;
                var bytes = System.IO.File.ReadAllBytes(p);
                if (bytes.Length != Samples * Samples * 4) return null;
                var arr = new int[Samples * Samples];
                Buffer.BlockCopy(bytes, 0, arr, 0, bytes.Length);
                return arr;
            }
            catch { return null; }
        }

        void SaveTileToDisk(int sc, int tx, int tz, int[] buf)
        {
            try
            {
                System.IO.Directory.CreateDirectory(TileDir);
                var bytes = new byte[buf.Length * 4];
                Buffer.BlockCopy(buf, 0, bytes, 0, bytes.Length);
                System.IO.File.WriteAllBytes(TilePath(seed, sc, tx, tz), bytes);
            }
            catch { }
        }
        static int IndexOfScale(int sc)
        {
            for (int i = 0; i < ZoomSteps.Length; i++) if (ZoomSteps[i] == sc) return i;
            return 3;
        }
        static int FloorDiv(int a, int b)
        {
            int q = a / b;
            if ((a % b != 0) && ((a < 0) != (b < 0))) q--;
            return q;
        }

        void KickPrep()
        {
            if (Interlocked.CompareExchange(ref prepWorker, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    while (true)
                    {
                        long key;
                        lock (prepQueue)
                        {
                            if (prepQueue.Count == 0) break;
                            key = prepQueue.Dequeue();
                        }
                        int sc = (int)(key >> 40);
                        int tx = (int)((key >> 20) & 0xFFFFF);
                        int tz = (int)(key & 0xFFFFF);
                        if ((tx & 0x80000) != 0) tx -= 0x100000;
                        if ((tz & 0x80000) != 0) tz -= 0x100000;

                        int[] buf = new int[Samples * Samples];
                        try
                        {
                            lock (hilLock)
                            {
                                SeedEngine.hil_setup(SeedEngine.hil_version_newest(), seed);
                                // 每 2×2 显示像素采样一次：世界步长 = sc * SampleScale
                                SeedEngine.hil_tile(sc * SampleScale, tx * TilePx * sc, tz * TilePx * sc, Samples, Samples, buf);
                            }
                        }
                        catch { }
                        int[] done = buf;
                        int sc2 = sc, tx2 = tx, tz2 = tz;
                        Dispatcher.BeginInvoke(new Action(delegate
                        {
                            tiles[key] = done;
                            queued.Remove(key);
                            SaveTileToDisk(sc2, tx2, tz2, done);
                            Paint();
                        }));
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref prepWorker, 0);
                    bool more;
                    lock (prepQueue) more = prepQueue.Count > 0;
                    if (more) KickPrep();
                }
            });
        }
        // 只用缓存拼当前视野；缺的块先空着，算好会自动重画
        void Paint()
        {
            if (ActualWidth < 32 || ActualHeight < 32) return;
            if (imgW <= 0 || imgH <= 0) return;
            int w = imgW, h = imgH, sc = scale;
            if (wbmp == null || wbmp.PixelWidth != w || wbmp.PixelHeight != h)
                wbmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
            var buf = new int[w * h];
            // 先把上一帧按新旧缩放比例最近邻拉过来打底（缩放那一刻立刻有画面）
            if (lastBuf != null && lastSc > 0 && lastW > 0 && lastH > 0)
            {
                for (int py = 0; py < h; py++)
                {
                    int worldZ = imgZ0 + py * sc;
                    int ly = (worldZ - lastZ0) / lastSc;
                    if (ly < 0 || ly >= lastH) continue;
                    int rowDst = py * w;
                    int rowSrc = ly * lastW;
                    for (int px = 0; px < w; px++)
                    {
                        int worldX = imgX0 + px * sc;
                        int lx = (worldX - lastX0) / lastSc;
                        if (lx < 0 || lx >= lastW) continue;
                        buf[rowDst + px] = lastBuf[rowSrc + lx];
                    }
                }
            }
            int tx0 = FloorDiv(imgX0, TilePx * sc), tz0 = FloorDiv(imgZ0, TilePx * sc);
            int tx1 = FloorDiv(imgX0 + w * sc - 1, TilePx * sc), tz1 = FloorDiv(imgZ0 + h * sc - 1, TilePx * sc);
            int have = 0, need = 0;
            int myIdx = IndexOfScale(sc);
            for (int tz = tz0; tz <= tz1; tz++)
            {
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    need++;
                    int tileSc = sc;
                    int tX = tx, tZ = tz;
                    int[] tile;
                    if (!tiles.TryGetValue(TileKey(sc, tx, tz), out tile))
                    {
                        // 没算好：先拿更粗一级的缓存块拉伸顶住（渐进出图，不空白）
                        tile = null;
                        for (int d = 1; d <= 2 && tile == null; d++)
                        {
                            int oi = myIdx + d;
                            if (oi >= ZoomSteps.Length) break;
                            int other = ZoomSteps[oi];
                            int otx = FloorDiv(tx * TilePx * sc, TilePx * other);
                            int otz = FloorDiv(tz * TilePx * sc, TilePx * other);
                            if (tiles.TryGetValue(TileKey(other, otx, otz), out tile))
                            {
                                tileSc = other; tX = otx; tZ = otz;
                            }
                        }
                        if (tile == null) continue;
                    }
                    else have++;
                    int tileX0 = tX * TilePx * tileSc, tileZ0 = tZ * TilePx * tileSc;
                    int step = tileSc * SampleScale;
                    for (int py = 0; py < h; py++)
                    {
                        int wz = imgZ0 + py * sc;
                        int sy = (wz - tileZ0) / step;
                        if (sy < 0 || sy >= Samples) continue;
                        int rowSrc = sy * Samples, rowDst = py * w;
                        for (int px = 0; px < w; px++)
                        {
                            int wx = imgX0 + px * sc;
                            int sxi = (wx - tileX0) / step;
                            if (sxi < 0 || sxi >= Samples) continue;
                            buf[rowDst + px] = tile[rowSrc + sxi];
                        }
                    }
                }
            }            var bytes = new byte[w * h * 4];
            Buffer.BlockCopy(buf, 0, bytes, 0, bytes.Length);
            wbmp.WritePixels(new Int32Rect(0, 0, w, h), bytes, w * 4, 0);
            img.Source = wbmp;
            lastBuf = buf;
            lastX0 = imgX0; lastZ0 = imgZ0; lastSc = sc; lastW = w; lastH = h;
            if (hudText != null && !busy)
                hudText.Text = "已加载 " + have + "/" + need + " 块 · 1 像素 = " + sc + " 格 · 滚轮缩放 / 拖动平移";
        }

        public void Render()
        {
            if (!SeedEngine.Ensure()) return;
            if (ActualWidth < 32 || ActualHeight < 32) return;
            int w = (int)Math.Min(768, Math.Max(256, ActualWidth));
            int h = (int)Math.Min(768, Math.Max(256, ActualHeight));
            int sc = scale;
            imgX0 = (int)(centerX - w * sc / 2.0);
            imgZ0 = (int)(centerZ - h * sc / 2.0);
            // 对齐到 sc 的整数倍：分块贴图时 (tileX0 - imgX0) 才能整除，避免像素错位成噪声
            if (sc > 0)
            {
                imgX0 -= ((imgX0 % sc) + sc) % sc;
                imgZ0 -= ((imgZ0 % sc) + sc) % sc;
            }
            imgW = w; imgH = h;
            EnsureTiles(imgX0, imgZ0, w, h, sc);
            Paint();
            ScanMarkers(w, h, sc);
        }

        // 结构/要塞/出生点：跟地图分开算，算完只更新标记
        void ScanMarkers(int w, int h, int sc)
        {
            int mySeq = ++curSeq;
            int x0 = imgX0, z0 = imgZ0;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var found = new List<MapMarker>();
                try
                {
                    lock (hilLock) { SeedEngine.hil_setup(SeedEngine.hil_version_newest(), seed); }
                    if (sc <= 16)
                    {
                        int[] tmp = new int[2 * 400];
                        for (int i = 0; i < StructWhich.Length; i++)
                        {
                            if (!enabled[i]) continue;
                            int type = SeedEngine.hil_struct_constant(StructWhich[i]);
                            int n;
                            lock (hilLock)
                                n = SeedEngine.hil_scan_structures(type, x0, z0, x0 + w * sc, z0 + h * sc, 400, tmp);
                            for (int k = 0; k < n; k++)
                            {
                                var m = new MapMarker();
                                m.X = tmp[k * 2]; m.Z = tmp[k * 2 + 1];
                                m.Name = StructNames[i]; m.Color = StructColors[i];
                                found.Add(m);
                            }
                        }
                        if (enabled[6])
                        {
                            int[] sh = new int[2 * 4];
                            int n;
                            lock (hilLock) n = SeedEngine.hil_strongholds(4, sh);
                            for (int k = 0; k < n; k++)
                            {
                                var m = new MapMarker();
                                m.X = sh[k * 2]; m.Z = sh[k * 2 + 1];
                                m.Name = "要塞"; m.Color = ExtraColors[0];
                                found.Add(m);
                            }
                        }
                    }
                    if (enabled[7])
                    {
                        int sx, sz;
                        int ok;
                        lock (hilLock) ok = SeedEngine.hil_spawn(out sx, out sz);
                        if (ok == 1)
                        {
                            var m = new MapMarker();
                            m.X = sx; m.Z = sz;
                            m.Name = "出生点"; m.Color = ExtraColors[1];
                            found.Add(m);
                        }
                    }
                }
                catch { }
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (mySeq != curSeq) return;
                    markers.Clear();
                    markers.AddRange(found);
                    DrawMarkers();
                    UpdateHud();
                }));
            });
        }
        // 图例：点一下开关对应结构类型
        void DrawLegend()
        {
            if (legend != null) { Children.Remove(legend); legend = null; }
            legend = new Border();
            legend.Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0));
            legend.CornerRadius = new CornerRadius(5);
            legend.Padding = new Thickness(8, 5, 8, 6);
            legend.Margin = new Thickness(8);
            legend.HorizontalAlignment = HorizontalAlignment.Left;
            legend.VerticalAlignment = VerticalAlignment.Bottom;
            var wrap = new WrapPanel();
            legend.Child = wrap;
            for (int i = 0; i < StructNames.Length + ExtraNames.Length; i++)
            {
                int idx = i;
                string nm = i < StructNames.Length ? StructNames[i] : ExtraNames[i - StructNames.Length];
                Color col = i < StructNames.Length ? StructColors[i] : ExtraColors[i - StructNames.Length];
                var chip = new StackPanel();
                chip.Orientation = Orientation.Horizontal;
                chip.Margin = new Thickness(0, 0, 10, 2);
                chip.Cursor = Cursors.Hand;
                chip.Opacity = enabled[i] ? 1 : 0.35;
                chip.ToolTip = "点击开关「" + nm + "」标记";
                var dot = new Border();
                dot.Width = 8; dot.Height = 8;
                dot.CornerRadius = new CornerRadius(4);
                dot.Background = new SolidColorBrush(col);
                dot.VerticalAlignment = VerticalAlignment.Center;
                dot.Margin = new Thickness(0, 0, 4, 0);
                chip.Children.Add(dot);
                var t = new TextBlock();
                t.Text = nm;
                t.FontSize = 10.5;
                t.Foreground = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255));
                chip.Children.Add(t);
                chip.MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    enabled[idx] = !enabled[idx];
                    chip.Opacity = enabled[idx] ? 1 : 0.35;
                    Render();
                };
                wrap.Children.Add(chip);
            }
            Children.Add(legend);
        }

        void DrawMarkers()
        {
            overlay.Children.Clear();
            double cw = ActualWidth, ch = ActualHeight;
            if (cw < 8 || ch < 8 || imgW == 0) return;
            for (int i = 0; i < markers.Count; i++)
            {
                MapMarker m = markers[i];
                double px = (m.X - imgX0) / (double)(imgW * scale) * cw;
                double py = (m.Z - imgZ0) / (double)(imgH * scale) * ch;
                if (px < -8 || py < -8 || px > cw + 8 || py > ch + 8) continue;

                var dot = new Border();
                dot.Width = 11;
                dot.Height = 11;
                dot.CornerRadius = new CornerRadius(6);
                dot.Background = new SolidColorBrush(m.Color);
                dot.BorderThickness = new Thickness(1);
                dot.BorderBrush = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255));
                dot.Cursor = Cursors.Hand;
                dot.ToolTip = m.Name + "  " + m.X + ", " + m.Z + "（点击复制传送指令）";
                int mx = m.X, mz = m.Z;
                dot.MouseLeftButtonUp += delegate(object o, MouseButtonEventArgs e)
                {
                    e.Handled = true;
                    try
                    {
                        Clipboard.SetText("/tp @s " + mx + " 100 " + mz);
                        HugeLauncher.Ui.Pages.PageBase.Toast("已复制传送指令：" + mx + ", " + mz);
                    }
                    catch { }
                };
                Canvas.SetLeft(dot, px - 5.5);
                Canvas.SetTop(dot, py - 5.5);
                overlay.Children.Add(dot);
            }
        }
    }
}
