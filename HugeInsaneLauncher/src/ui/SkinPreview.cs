// HugeLauncher.Ui - 皮肤正面预览（账号详情页用）
// 不需要 3D：把皮肤图集里「正面」的几块（头/身/双臂/双腿 + 帽子层）按站位拼成一张
// 16×32 的正面立绘，交给 Image 用最近邻放大显示，像素风且不会转。
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HugeLauncher.Ui
{
    class SkinPreview : Grid
    {
        const int W = 16, H = 32;   // 正面立绘画布（MC 单位）

        public SkinPreview(BitmapSource skin)
        {
            ClipToBounds = true;
            var img = new Image();
            img.Stretch = Stretch.Uniform;
            img.Margin = new Thickness(12);
            img.HorizontalAlignment = HorizontalAlignment.Center;
            img.VerticalAlignment = VerticalAlignment.Center;
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            img.Source = BuildFront(skin);
            Children.Add(img);
        }

        // 按正面站位拼图：头 (8,8) 身 (20,20) 右臂 (44,20) 左臂/腿 64×64 用左半、64×32 复用右半
        static BitmapSource BuildFront(BitmapSource skin)
        {
            var dst = new byte[W * H * 4];
            if (skin == null) return Make(dst);
            try
            {
                int s = Math.Max(1, skin.PixelWidth / 64);
                var bgra = new FormatConvertedBitmap(skin, PixelFormats.Bgra32, null, 0);
                int sw = bgra.PixelWidth, sh = bgra.PixelHeight;
                var px = new byte[sw * sh * 4];
                bgra.CopyPixels(px, sw * 4, 0);
                bool legacy = sh <= 32 * s;

                Blit(dst, px, sw, sh, 8 * s, 8 * s, 8, 8, 4, 0);      // 头正面
                Blit(dst, px, sw, sh, 40 * s, 8 * s, 8, 8, 4, 0);     // 帽子层正面（叠在头上）
                Blit(dst, px, sw, sh, 20 * s, 20 * s, 8, 12, 4, 8);   // 身体正面
                Blit(dst, px, sw, sh, 44 * s, 20 * s, 4, 12, 0, 8);   // 右臂正面（屏幕左）
                Blit(dst, px, sw, sh, (legacy ? 44 : 36) * s, (legacy ? 20 : 52) * s, 4, 12, 12, 8);  // 左臂
                Blit(dst, px, sw, sh, 4 * s, 20 * s, 4, 12, 4, 20);   // 右腿
                Blit(dst, px, sw, sh, (legacy ? 4 : 20) * s, (legacy ? 20 : 52) * s, 4, 12, 8, 20);    // 左腿
            }
            catch { }
            return Make(dst);
        }

        static BitmapSource Make(byte[] px)
        {
            var bmp = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, px, W * 4);
            bmp.Freeze();
            return bmp;
        }

        static void Blit(byte[] dst, byte[] src, int sw, int sh,
                         int sx, int sy, int w, int h, int dx, int dy)
        {
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int sxp = sx + x, syp = sy + y;
                    if (sxp < 0 || syp < 0 || sxp >= sw || syp >= sh) continue;
                    int si = (syp * sw + sxp) * 4;
                    if (src[si + 3] < 26) continue;          // 透明像素跳过，露出下面那层
                    int dxp = dx + x, dyp = dy + y;
                    if (dxp < 0 || dyp < 0 || dxp >= W || dyp >= H) continue;
                    int di = (dyp * W + dxp) * 4;
                    dst[di] = src[si];
                    dst[di + 1] = src[si + 1];
                    dst[di + 2] = src[si + 2];
                    dst[di + 3] = 255;
                }
            }
        }
    }
}
