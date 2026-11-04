// HugeLauncher.Ui - 霜面材质：用户壁纸 -> 高斯模糊 40 -> 深色纱，缓存成一张图。
// 所有磨砂面共用这张底图；小控件只做半透明材质，不逐帧挂 BlurEffect。
using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using Microsoft.Win32;

namespace HugeLauncher.Ui
{
    static class Frost
    {
        public static bool On { get { return ThemeManager.IsFrost; } }

        const int BW = 1280;
        const int BH = 720;

        static ImageBrush backdrop;
        static bool building;

        public static ImageBrush Backdrop
        {
            get
            {
                if (backdrop == null) Build();
                return backdrop;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [StructLayout(LayoutKind.Sequential)]
        struct Margins { public int Left, Right, Top, Bottom; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("dwmapi.dll")] static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins m);

        public static bool TryDwmBackdrop(System.Windows.Controls.Primitives.Popup popup)
        {
            try
            {
                if (popup == null || popup.Child == null) return false;
                var src = PresentationSource.FromVisual(popup.Child) as HwndSource;
                if (src == null || src.Handle == IntPtr.Zero) return false;
                var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
                if (DwmExtendFrameIntoClientArea(src.Handle, ref margins) != 0) return false;
                if (src.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                int dark = 1;
                DwmSetWindowAttribute(src.Handle, 20, ref dark, sizeof(int));
                int corner = 2;
                DwmSetWindowAttribute(src.Handle, 33, ref corner, sizeof(int));
                int type = 3;
                return DwmSetWindowAttribute(src.Handle, 38, ref type, sizeof(int)) == 0;
            }
            catch { return false; }
        }

        public static bool TryAcrylic(System.Windows.Controls.Primitives.Popup popup)
        {
            try
            {
                if (popup == null || popup.Child == null) return false;
                var src = PresentationSource.FromVisual(popup.Child) as HwndSource;
                if (src == null || src.Handle == IntPtr.Zero) return false;
                if (src.CompositionTarget != null) src.CompositionTarget.BackgroundColor = Colors.Transparent;
                int a = 168, r = 18, g = 20, b = 26;
                var accent = new AccentPolicy();
                accent.AccentState = 4;
                accent.AccentFlags = 2;
                accent.GradientColor = (a << 24) | (b << 16) | (g << 8) | r;
                int size = Marshal.SizeOf(accent);
                IntPtr ptr = Marshal.AllocHGlobal(size);
                try
                {
                    Marshal.StructureToPtr(accent, ptr, false);
                    var data = new WindowCompositionAttributeData();
                    data.Attribute = 19;
                    data.Data = ptr;
                    data.SizeOfData = size;
                    return SetWindowCompositionAttribute(src.Handle, ref data) != 0;
                }
                finally { Marshal.FreeHGlobal(ptr); }
            }
            catch { return false; }
        }

        public static void Refresh()
        {
            backdrop = null;
            if (On) Build();
        }

        // 磨砂面板：深色主题用低透明白，亮色主题用低透明黑
        public static Brush Tile
        {
            get { return Solid(Palette.Light ? Color.FromArgb(120, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255)); }
        }

        public static Brush TileStrong
        {
            get { return Solid(Palette.Light ? Color.FromArgb(165, 255, 255, 255) : Color.FromArgb(40, 255, 255, 255)); }
        }

        public static Brush Edge
        {
            get { return Solid(Palette.Light ? Color.FromArgb(200, 0xCB, 0xD0, 0xDC) : Color.FromArgb(54, 255, 255, 255)); }
        }

        public static Brush Button
        {
            get { return Solid(Palette.Light ? Color.FromArgb(105, 255, 255, 255) : Color.FromArgb(22, 255, 255, 255)); }
        }

        public static Brush ButtonHover
        {
            get { return Solid(Palette.Light ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(46, 255, 255, 255)); }
        }

        public static Brush Input
        {
            get { return Solid(Palette.Light ? Color.FromArgb(150, 255, 255, 255) : Color.FromArgb(72, 8, 10, 14)); }
        }

        public static Brush Popup
        {
            get
            {
                ImageBrush b = Backdrop;
                if (b == null) return Solid(Palette.Light ? Color.FromArgb(245, 248, 248, 250) : Color.FromArgb(245, 16, 18, 24));
                return b;
            }
        }

        public static Brush Scrim
        {
            get { return Solid(Palette.Light ? Color.FromArgb(40, 255, 255, 255) : Color.FromArgb(120, 0, 0, 0)); }
        }

        static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static void Build()
        {
            if (building) return;
            building = true;
            try
            {
                BitmapSource src = LoadWallpaper();
                if (src == null) { HilLog.Write("[Frost] no wallpaper"); return; }
                BitmapSource blurred = Blur(src);
                var brush = new ImageBrush(blurred);
                brush.Stretch = Stretch.UniformToFill;
                brush.Freeze();
                backdrop = brush;
            }
            catch (Exception ex) { HilLog.Write("[Frost] build fail: " + ex); backdrop = null; }
            finally { building = false; }
        }

        static BitmapSource LoadWallpaper()
        {
            string path = null;
            try
            {
                object v = Registry.GetValue(@"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null);
                path = v as string;
            }
            catch { }
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    @"Microsoft\Windows\Themes\TranscodedWallpaper");
                if (File.Exists(t)) path = t;
            }
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 1600;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }

        static BitmapSource Blur(BitmapSource src)
        {
            double scale = Math.Max(BW / (double)src.PixelWidth, BH / (double)src.PixelHeight) * 1.16;
            var img = new Image();
            img.Source = src;
            img.Stretch = Stretch.UniformToFill;
            img.Width = src.PixelWidth * scale;
            img.Height = src.PixelHeight * scale;
            img.Effect = new BlurEffect { Radius = 40, KernelType = KernelType.Gaussian };

            var host = new Grid();
            host.Width = BW;
            host.Height = BH;
            host.ClipToBounds = true;
            host.Children.Add(img);
            host.Measure(new Size(BW, BH));
            host.Arrange(new Rect(0, 0, BW, BH));
            host.UpdateLayout();

            var rtb = new RenderTargetBitmap(BW, BH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(host);

            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawImage(rtb, new Rect(0, 0, BW, BH));
                dc.DrawRectangle(new SolidColorBrush(Palette.Light
                    ? Color.FromArgb(70, 255, 255, 255)
                    : Color.FromArgb(160, 6, 8, 12)), null, new Rect(0, 0, BW, BH));
            }
            var outBmp = new RenderTargetBitmap(BW, BH, 96, 96, PixelFormats.Pbgra32);
            outBmp.Render(dv);
            outBmp.Freeze();
            return outBmp;
        }
    }
}
