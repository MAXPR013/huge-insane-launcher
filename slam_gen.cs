// slam_gen - 白底黑字砸脸公告视频生成器（无 ffmpeg 依赖：GDI+ 渲帧 + 手写 MJPEG AVI 封装）
// 规格：1280x720 @30fps，5 句各 2 秒砸脸入场，末句多停 4 秒，共 14 秒（420 帧）
// 顶部常驻：详情请访问官网hugeinsane.pages.dev
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Text;

class SlamGen
{
    const int W = 1280, H = 720, FPS = 30;
    const int SEG_FRAMES = 60;          // 每句 2 秒
    const int HOLD_FRAMES = 120;        // 末句追加 4 秒
    static readonly string[] Lines =
    {
        "我将会继续开发HIL启动器",
        "但是由于需要备考CSP",
        "所以开发进度会稍微慢一点",
        "这种情况会持续大约2个月",
        "感谢大家的支持，我不会停止开发，大家放心。"
    };
    const string Banner = "详情请访问官网hugeinsane.pages.dev";

    static int TotalFrames { get { return SEG_FRAMES * Lines.Length + HOLD_FRAMES; } }

    static void Main()
    {
        string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HIL公告.avi");
        int total = TotalFrames;
        using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.ReadWrite))
        using (var bw = new BinaryWriter(fs))
        {
            var avi = new AviWriter(bw, W, H, FPS, total);
            avi.WriteHeader();
            var jpeg = GetJpegEncoder();
            var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);

            using (var bmp = new Bitmap(W, H, PixelFormat.Format24bppRgb))
            using (var g = Graphics.FromImage(bmp))
            {
                for (int f = 0; f < total; f++)
                {
                    int seg = f / SEG_FRAMES;
                    if (seg >= Lines.Length) seg = Lines.Length - 1;   // 末段含追加停留
                    int lf = f - seg * SEG_FRAMES;
                    RenderFrame(g, bmp, seg, lf);
                    byte[] jpg;
                    using (var ms = new MemoryStream())
                    {
                        bmp.Save(ms, jpeg, ep);
                        jpg = ms.ToArray();
                    }
                    avi.WriteFrame(jpg);
                    if (f % 60 == 0) Console.WriteLine("frame " + f + "/" + total);
                }
            }
            avi.Finish();
        }
        Console.WriteLine("OK " + outPath + " " + new FileInfo(outPath).Length / 1024 + " KB");
    }

    // 砸脸入场：2.6 倍 → 1.0（ease-out 减速砸下），随后 2 帧回弹沉降；前 3 帧淡入
    static void RenderFrame(Graphics g, Bitmap bmp, int seg, int lf)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;
        g.Clear(Color.White);

        // 顶部常驻横幅（黑字，全程不动）
        using (var bf = new Font(UiFace(), 30, FontStyle.Bold))
        using (var black = new SolidBrush(Color.Black))
        {
            var sz = g.MeasureString(Banner, bf);
            g.DrawString(Banner, bf, black, (W - sz.Width) / 2, 40);
        }

        double scale = 1.0;   // 硬切无动画：恒定 1:1、不透明（用户拍板：不要砸脸）
        int alpha = 255;

        string text = Lines[seg];
        using (var f = FitFont(g, text))
        using (var br = new SolidBrush(Color.FromArgb(alpha, 0, 0, 0)))
        {
            var state = g.Save();
            g.TranslateTransform(W / 2f, H / 2f);
            g.ScaleTransform((float)scale, (float)scale);
            var sz = g.MeasureString(text, f);
            g.DrawString(text, f, br, -sz.Width / 2, -sz.Height / 2);
            g.Restore(state);
        }
    }

    static FontFamily face;
    static FontFamily UiFace()
    {
        if (face == null)
        {
            try { face = new FontFamily("Microsoft YaHei"); }
            catch { face = FontFamily.GenericSansSerif; }
        }
        return face;
    }

    // 自适应字号：再长的句子也缩到一行放得下（宽 ≤1180）
    static Font FitFont(Graphics g, string text)
    {
        int size = 68;
        while (size > 26)
        {
            var f = new Font(UiFace(), size, FontStyle.Bold);
            var sz = g.MeasureString(text, f);
            if (sz.Width <= 1180) return f;
            f.Dispose();
            size -= 2;
        }
        return new Font(UiFace(), 26, FontStyle.Bold);
    }

    static ImageCodecInfo GetJpegEncoder()
    {
        foreach (var c in ImageCodecInfo.GetImageEncoders())
            if (c.FormatID == ImageFormat.Jpeg.Guid) return c;
        throw new Exception("no jpeg encoder");
    }
}

// 最小 MJPEG AVI 封装：RIFF/avih/strh/strf/movi/idx1，每帧一个 00dc JPEG 块
class AviWriter
{
    BinaryWriter bw;
    int w, h, fps, total;
    long riffSizePos, moviSizePos, moviDataStart;
    MemoryStream idx = new MemoryStream();
    int frames;

    public AviWriter(BinaryWriter bw, int w, int h, int fps, int total)
    {
        this.bw = bw; this.w = w; this.h = h; this.fps = fps; this.total = total;
    }

    void U32(uint v) { bw.Write(v); }
    void Four(string s) { bw.Write(Encoding.ASCII.GetBytes(s)); }

    void Chunk(string fourcc, byte[] data)
    {
        Four(fourcc); U32((uint)data.Length); bw.Write(data);
        if ((data.Length & 1) != 0) bw.Write((byte)0);
    }

    public void WriteHeader()
    {
        Four("RIFF"); riffSizePos = bw.BaseStream.Position; U32(0); Four("AVI ");

        var hdrl = new MemoryStream();
        var hb = new BinaryWriter(hdrl);
        // avih（56 字节）
        var avih = new MemoryStream(); var a = new BinaryWriter(avih);
        a.Write((uint)(1000000 / fps));   // 每帧微秒
        a.Write((uint)(w * h * fps));     // 码率上限（宽松值，播放器不较真）
        a.Write((uint)0);
        a.Write((uint)0x10);              // AVIF_HASINDEX
        a.Write((uint)total);
        a.Write((uint)0);
        a.Write((uint)1);                 // 1 路流
        a.Write((uint)(w * h * 3));       // 建议缓冲
        a.Write((uint)w); a.Write((uint)h);
        a.Write(new byte[16]);
        Chunk("avih", avih.ToArray());
        // strl：strh + strf
        var strl = new MemoryStream(); var s = new BinaryWriter(strl);
        var strh = new MemoryStream(); var sh = new BinaryWriter(strh);
        sh.Write(Encoding.ASCII.GetBytes("vids"));
        sh.Write(Encoding.ASCII.GetBytes("MJPG"));
        sh.Write((uint)0); sh.Write((uint)0); sh.Write((uint)0);
        sh.Write((uint)1);                // dwScale
        sh.Write((uint)fps);              // dwRate
        sh.Write((uint)0);
        sh.Write((uint)total);            // dwLength
        sh.Write((uint)(w * h * 3));
        sh.Write((uint)0xFFFFFFFF);
        sh.Write((uint)0);
        sh.Write((ushort)0); sh.Write((ushort)0);
        sh.Write((ushort)w); sh.Write((ushort)h);
        Chunk("strh", strh.ToArray());
        var strf = new MemoryStream(); var sf = new BinaryWriter(strf);
        sf.Write((uint)40);
        sf.Write((int)w); sf.Write((int)h);
        sf.Write((ushort)1); sf.Write((ushort)24);
        sf.Write((uint)0x47504A4D);       // 'MJPG'
        sf.Write((uint)(w * h * 3));
        sf.Write((int)0); sf.Write((int)0); sf.Write((uint)0); sf.Write((uint)0);
        Chunk("strf", strf.ToArray());
        s.Flush();
        Four("LIST"); U32((uint)(4 + strl.Length)); Four("strl"); bw.Write(strl.ToArray());
        hb.Flush();
        Four("LIST"); U32((uint)(4 + hdrl.Length)); Four("hdrl"); bw.Write(hdrl.ToArray());

        Four("LIST"); moviSizePos = bw.BaseStream.Position; U32(0); Four("movi");
        moviDataStart = bw.BaseStream.Position;
    }

    public void WriteFrame(byte[] jpg)
    {
        long pos = bw.BaseStream.Position;
        Four("00dc"); U32((uint)jpg.Length); bw.Write(jpg);
        if ((jpg.Length & 1) != 0) bw.Write((byte)0);
        var ib = new BinaryWriter(idx);
        ib.Write(Encoding.ASCII.GetBytes("00dc"));
        ib.Write((uint)0x10);                       // keyframe
        ib.Write((uint)(pos - moviDataStart));      // 相对 movi 数据起点
        ib.Write((uint)jpg.Length);
        frames++;
    }

    public void Finish()
    {
        long moviEnd = bw.BaseStream.Position;
        Chunk("idx1", idx.ToArray());
        long fileEnd = bw.BaseStream.Position;
        // 回填 RIFF / movi 尺寸
        bw.BaseStream.Position = riffSizePos; U32((uint)(fileEnd - 8));
        bw.BaseStream.Position = moviSizePos; U32((uint)(moviEnd - moviDataStart + 4));
        bw.BaseStream.Position = fileEnd;
        bw.Flush();
    }
}
