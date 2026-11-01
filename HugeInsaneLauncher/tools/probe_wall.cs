// probe_wall - 复刻 WallpaperPalette.Extract 的路径解析与解码，定位壁纸取色失败点
using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

class ProbeWall
{
    static int Main()
    {
        string path = Microsoft.Win32.Registry.GetValue(
            @"HKEY_CURRENT_USER\Control Panel\Desktop", "Wallpaper", null) as string;
        Console.WriteLine("registry: " + (path ?? "(null)"));
        Console.WriteLine("exists: " + (path != null && File.Exists(path)));
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            string tw = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                @"Microsoft\Windows\Themes\TranscodedWallpaper");
            Console.WriteLine("fallback transcoded: " + tw + " exists=" + File.Exists(tw));
            if (File.Exists(tw)) path = tw;
        }
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path, UriKind.Absolute);
            bi.DecodePixelWidth = 16;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            Console.WriteLine("decoded: " + bi.PixelWidth + "x" + bi.PixelHeight + " fmt=" + bi.Format);
            var conv = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
            conv.Freeze();
            var px = new byte[conv.PixelWidth * conv.PixelHeight * 4];
            conv.CopyPixels(px, conv.PixelWidth * 4, 0);
            // 打印前 8 个像素 + 饱和度统计
            int colorful = 0, muted = 0, vrej = 0;
            for (int i = 0; i < conv.PixelWidth * conv.PixelHeight; i++)
            {
                byte b = px[i * 4], g = px[i * 4 + 1], r = px[i * 4 + 2];
                if (i < 8) Console.WriteLine("px" + i + ": " + r + "," + g + "," + b);
                double rf = r / 255.0, gf = g / 255.0, bf = b / 255.0;
                double mx = Math.Max(rf, Math.Max(gf, bf)), mn = Math.Min(rf, Math.Min(gf, bf));
                double v = mx, s = mx < 0.001 ? 0 : (mx - mn) / mx;
                if (v < 0.12 || v > 0.95) { vrej++; continue; }
                if (s >= 0.22) colorful++; else if (s >= 0.06) muted++;
            }
            Console.WriteLine("s>=0.22: " + colorful + " | 0.06<=s<0.22: " + muted + " | v-reject: " + vrej);
        }
        catch (Exception ex)
        {
            Console.WriteLine("DECODE FAIL: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
        return 0;
    }
}
