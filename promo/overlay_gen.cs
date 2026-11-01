// 宣传片覆盖层渲染：秒表数字精灵表 + 9 条字幕 pill（透明底 PNG）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

static class OverlayGen
{
    static readonly Color Green = Color.FromArgb(85, 196, 90);

    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    static Graphics G(Bitmap b)
    {
        var g = Graphics.FromImage(b);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        return g;
    }

    static int Main()
    {
        string face = "Microsoft YaHei";
        try
        {
            var pfc = new PrivateFontCollection();
            pfc.AddFontFile(@"C:\Windows\Fonts\msyh.ttc");
            pfc.AddFontFile(@"C:\Windows\Fonts\msyhbd.ttc");
            face = pfc.Families[0].Name;
        }
        catch { }

        // ---- 秒表数字精灵表："0123456789.s" 等宽 40px 格子 ----
        using (var f = new Font(face, 44, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            string glyphs = "0123456789.s";
            int cell = 40, ch = 64;
            using (var b = new Bitmap(cell * glyphs.Length, ch))
            using (var g = G(b))
            using (var white = new SolidBrush(Color.White))
            {
                g.Clear(Color.Transparent);
                for (int i = 0; i < glyphs.Length; i++)
                {
                    var s = g.MeasureString(glyphs[i].ToString(), f);
                    g.DrawString(glyphs[i].ToString(), f, white,
                        i * cell + (cell - s.Width) / 2, (ch - s.Height) / 2);
                }
                b.Save("ov_digits.png");
            }
        }

        // ---- 字幕 pill：绿方块 + 白字，半透明深底胶囊 ----
        string[] caps = {
            "双击即用 · 免安装", "AI 深驻底层", "Fabric 完整安装 7.3 秒 · 全程原速",
            "一键启动", "悬浮球 · 最小化不占屏", "一键开服 · 自带联机",
            "明暗双色 · 液态玻璃", "H史 · 内置 AI 助手", "离线模式 · 畅玩服务器"
        };
        using (var f = new Font(face, 40, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            for (int i = 0; i < caps.Length; i++)
            {
                var size = g2Measure(caps[i], f);
                int tw = (int)Math.Ceiling(size.Width), th = (int)Math.Ceiling(size.Height);
                int pw = tw + 96, ph = th + 30;
                using (var b = new Bitmap(pw, ph))
                using (var g = G(b))
                using (var path = Rounded(1, 1, pw - 2, ph - 2, (ph - 2) / 2))
                using (var bg = new SolidBrush(Color.FromArgb(190, 16, 18, 24)))
                using (var pen = new Pen(Color.FromArgb(120, Green.R, Green.G, Green.B), 2))
                using (var fg = new SolidBrush(Green))
                using (var white = new SolidBrush(Color.White))
                {
                    g.FillPath(bg, path);
                    g.DrawPath(pen, path);
                    g.FillRectangle(fg, 24, (ph - 22) / 2, 22, 22);
                    g.DrawString(caps[i], f, white, 62, (ph - th) / 2 - 2);
                    b.Save("ov_cap" + i + ".png");
                }
            }
        }
        // ---- 顶部常驻横幅：绿底白字"详细信息和下载请访问 hugeinsane.pages.dev" ----
        using (var f = new Font(face, 34, FontStyle.Bold, GraphicsUnit.Pixel))
        {
            const int bw = 1920, bh = 64;
            using (var b = new Bitmap(bw, bh))
            using (var g = G(b))
            using (var bg = new SolidBrush(Green))
            using (var edge = new SolidBrush(Color.FromArgb(46, 125, 50)))
            using (var shadow = new SolidBrush(Color.FromArgb(90, 0, 0, 0)))
            using (var white = new SolidBrush(Color.White))
            {
                g.FillRectangle(bg, 0, 0, bw, bh);
                g.FillRectangle(edge, 0, bh - 3, bw, 3);   // 底部深绿描边
                string txt = "详细信息和下载请访问  hugeinsane.pages.dev";
                var size = g2Measure(txt, f);
                float tx = (bw - size.Width) / 2, ty = (bh - size.Height) / 2 - 2;
                g.DrawString(txt, f, shadow, tx + 1.5f, ty + 1.5f);
                g.DrawString(txt, f, white, tx, ty);
                b.Save("ov_banner.png");
            }
        }
        Console.WriteLine("overlays saved");
        return 0;
    }

    static SizeF g2Measure(string s, Font f)
    {
        using (var tmp = new Bitmap(1, 1))
        using (var g = Graphics.FromImage(tmp))
            return g.MeasureString(s, f);
    }
}
