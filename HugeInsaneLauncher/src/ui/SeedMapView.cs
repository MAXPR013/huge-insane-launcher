// HugeLauncher.Ui - 种子地图：cubiomes 本地引擎 + 可拖动缩放瓦片 + 结构标记
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
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
            MouseLeftButtonUp += delegate
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
                dragDx = dragDy = 0;
                Render();
            };
            MouseWheel += delegate(object o, MouseWheelEventArgs e)
            {
                if (e.Delta > 0) scale = scale == 4 ? 16 : 64;
                else scale = scale == 64 ? 16 : 4;
                Render();
                e.Handled = true;
            };
            SizeChanged += delegate { Render(); };
        }

        public int Scale { get { return scale; } }
        public void SetSeed(ulong s) { seed = s; markers.Clear(); Render(); }

        void UpdateHud()
        {
            if (hudText == null) return;
            int wx = imgX0 + (int)(lastMouse.X / Math.Max(1, ActualWidth) * imgW) * scale;
            int wz = imgZ0 + (int)(lastMouse.Y / Math.Max(1, ActualHeight) * imgH) * scale;
            hudText.Text = "X " + wx + "   Z " + wz + "   ·   1 像素 = " + scale + " 格   ·   标记 " + markers.Count + "   ·   滚轮缩放 / 拖动平移";
        }

        public void Render()
        {
            if (!SeedEngine.Ensure()) return;
            if (ActualWidth < 32 || ActualHeight < 32) return;
            int w = (int)Math.Min(768, Math.Max(256, ActualWidth));
            int h = (int)Math.Min(768, Math.Max(256, ActualHeight));
            int mySeq = ++seq;
            int sc = scale;
            imgX0 = (int)(centerX - w * sc / 2.0);
            imgZ0 = (int)(centerZ - h * sc / 2.0);
            imgW = w; imgH = h;
            int x0 = imgX0, z0 = imgZ0;
            ulong sd = seed;
            busy = true;
            if (hudText != null) hudText.Text = "生成中…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                int[] buf = new int[w * h];
                var found = new List<MapMarker>();
                try
                {
                    int mc = SeedEngine.hil_version_newest();
                    SeedEngine.hil_setup(mc, sd);
                    SeedEngine.hil_tile(sc, x0, z0, w, h, buf);
                    // 结构只在不太远的视野里扫，避免卡顿
                    if (sc <= 16)
                    {
                        int[] tmp = new int[2 * 400];
                        for (int i = 0; i < StructWhich.Length; i++)
                        {
                            if (!enabled[i]) continue;
                            int type = SeedEngine.hil_struct_constant(StructWhich[i]);
                            int n = SeedEngine.hil_scan_structures(type, x0, z0, x0 + w * sc, z0 + h * sc, 400, tmp);
                            for (int k = 0; k < n; k++)
                            {
                                var m = new MapMarker();
                                m.X = tmp[k * 2];
                                m.Z = tmp[k * 2 + 1];
                                m.Name = StructNames[i];
                                m.Color = StructColors[i];
                                found.Add(m);
                            }
                        }
                        // 要塞
                        if (enabled[6])
                        {
                            int[] sh = new int[2 * 4];
                            int n = SeedEngine.hil_strongholds(4, sh);
                            for (int k = 0; k < n; k++)
                            {
                                var m = new MapMarker();
                                m.X = sh[k * 2]; m.Z = sh[k * 2 + 1];
                                m.Name = "要塞"; m.Color = ExtraColors[0];
                                found.Add(m);
                            }
                        }
                    }
                    // 出生点（不依赖缩放）
                    if (enabled[7])
                    {
                        int sx, sz;
                        if (SeedEngine.hil_spawn(out sx, out sz) == 1)
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
                    if (mySeq != seq) return;
                    busy = false;
                    var bytes = new byte[w * h * 4];
                    Buffer.BlockCopy(buf, 0, bytes, 0, bytes.Length);
                    var bmp = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
                    bmp.WritePixels(new Int32Rect(0, 0, w, h), bytes, w * 4, 0);
                    img.Source = bmp;
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
