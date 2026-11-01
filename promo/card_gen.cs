// 宣传片字卡渲染器：输出 1530x870 PNG 字卡（标题卡/卖点卡/片尾卡）
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

static class CardGen
{
    const int W = 1530, H = 870;
    static readonly Color Bg = Color.FromArgb(20, 22, 29);
    static readonly Color Green = Color.FromArgb(85, 196, 90);
    static readonly Color FgDim = Color.FromArgb(160, 166, 178);

    static Bitmap NewCanvas()
    {
        var b = new Bitmap(W, H);
        using (var g = Graphics.FromImage(b))
        {
            g.Clear(Bg);
            // 淡淡的像素网格点缀（MC 血统）
            var rnd = new Random(42);
            using (var pen = new Pen(Color.FromArgb(14, 255, 255, 255)))
            {
                for (int i = 0; i < 26; i++)
                {
                    int x = rnd.Next(W), y = rnd.Next(H), s = rnd.Next(6, 22);
                    g.DrawRectangle(pen, x, y, s, s);
                }
            }
        }
        return b;
    }

    static void CenterText(Graphics g, string text, Font f, Brush brush, float y)
    {
        var size = g.MeasureString(text, f);
        g.DrawString(text, f, brush, (W - size.Width) / 2, y);
    }

    static void Pill(Graphics g, string text, Font f, float y)
    {
        var size = g.MeasureString(text, f);
        float pw = size.Width + 44, ph = size.Height + 16;
        float px = (W - pw) / 2, py = y;
        using (var path = Rounded(px, py, pw, ph, ph / 2))
        using (var bg = new SolidBrush(Color.FromArgb(28, Green.R, Green.G, Green.B)))
        using (var pen = new Pen(Green, 2))
        using (var fg = new SolidBrush(Green))
        {
            g.FillPath(bg, path);
            g.DrawPath(pen, path);
            g.DrawString(text, f, fg, px + 22, py + 8);
        }
    }

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

    static int Main()
    {
        string face = "Microsoft YaHei";
        PrivateFontCollection pfc = null;
        try { pfc = new PrivateFontCollection(); pfc.AddFontFile(@"C:\Windows\Fonts\msyh.ttc"); face = pfc.Families[0].Name; }
        catch { }

        // ---- 标题卡 ----
        using (var b = NewCanvas())
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var f1 = new Font(face, 68, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var f2 = new Font(face, 30, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var white = new SolidBrush(Color.White))
            using (var dim = new SolidBrush(FgDim))
            {
                CenterText(g, "HUGE INSANE LAUNCHER", f1, white, 330);
                CenterText(g, "一个创新性的 Minecraft 启动器", f2, dim, 430);
                Pill(g, "HIL · 26.8.12 Alpha", f2, 520);
            }
            b.Save("card_title.png");
        }

        // ---- 卖点卡 ----
        using (var b = NewCanvas())
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var f1 = new Font(face, 46, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var white = new SolidBrush(Color.White))
            using (var fg = new SolidBrush(Green))
            {
                string[] lines = { "离线模式 · 免费可玩", "一键开服 · 悬浮球助手", "液态玻璃 · 明暗双色" };
                for (int i = 0; i < lines.Length; i++)
                {
                    float y = 300 + i * 100;
                    var size = g.MeasureString(lines[i], f1);
                    float x = (W - size.Width) / 2;
                    g.FillRectangle(fg, x - 34, y + 14, 18, 18);   // 绿色方块项目符
                    g.DrawString(lines[i], f1, white, x, y);
                }
            }
            b.Save("card_feat.png");
        }

        // ---- 片尾卡 ----
        using (var b = NewCanvas())
        using (var g = Graphics.FromImage(b))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using (var f1 = new Font(face, 60, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var f2 = new Font(face, 28, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var white = new SolidBrush(Color.White))
            using (var dim = new SolidBrush(FgDim))
            {
                CenterText(g, "HUGE INSANE LAUNCHER", f1, white, 340);
                CenterText(g, "免费 · 免安装 · 社区驱动 · By MAX", f2, dim, 440);
                Pill(g, "现在就去下一把", f2, 520);
            }
            b.Save("card_end.png");
        }

        Console.WriteLine("cards saved");
        return 0;
    }
}
