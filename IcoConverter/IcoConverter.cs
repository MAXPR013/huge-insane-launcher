// HIL ICO 转换器 —— 工业风小工具：选图片 → 勾分辨率 → 导出 .ico 到本程序所在目录
// 编译: csc -nologo -target:winexe -out:IcoConverter.exe -r:System.Drawing.dll -r:System.Windows.Forms.dll IcoConverter.cs
// 无参数双击 = 窗口模式；也支持命令行: IcoConverter.exe 图片路径 [16,32,48,256]（转完直接退出）
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace HglTools
{
    class IcoConverterForm : Form
    {
        static readonly int[] AllSizes = { 16, 24, 32, 48, 64, 128, 256 };

        TextBox pathBox;
        PictureBox preview;
        CheckedListBox sizeList;
        Label status;
        Bitmap source;
        string sourcePath;

        [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 1)   // 命令行模式：供脚本/验证用
            {
                AttachConsole(-1);  // winexe 也能往父控制台写字
                try
                {
                    var sizes = args.Length >= 2
                        ? ParseSizes(args[1])
                        : new List<int> { 16, 32, 48, 256 };
                    string outPath;
                    using (var bmp = LoadBitmap(args[0]))
                        outPath = WriteIco(bmp, args[0], sizes);
                    Console.WriteLine("OK " + outPath);
                    return 0;
                }
                catch (Exception ex) { Console.WriteLine("FAIL " + ex.Message); return 1; }
            }
            Application.EnableVisualStyles();
            Application.Run(new IcoConverterForm());
            return 0;
        }

        static List<int> ParseSizes(string s)
        {
            var list = new List<int>();
            foreach (var part in s.Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int v;
                if (int.TryParse(part, out v) && v >= 1 && v <= 256 && !list.Contains(v)) list.Add(v);
            }
            if (list.Count == 0) throw new Exception("没有可用分辨率");
            list.Sort();
            return list;
        }

        // ---------- 核心转换（GUI 和命令行共用） ----------

        static Bitmap LoadBitmap(string path)
        {
            if (!File.Exists(path)) throw new Exception("找不到文件：" + path);
            // 经内存流克隆一份，脱离文件锁
            using (var tmp = new Bitmap(new MemoryStream(File.ReadAllBytes(path))))
                return new Bitmap(tmp);
        }

        // 源图 → ICO 文件，输出到本程序所在目录，文件名沿用源图名
        static string WriteIco(Bitmap src, string srcPath, List<int> sizes)
        {
            var pngs = new List<byte[]>();
            foreach (var s in sizes) pngs.Add(RenderPng(src, s));
            string outPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
                Path.GetFileNameWithoutExtension(srcPath) + ".ico");
            using (var fs = new FileStream(outPath, FileMode.Create))
            using (var w = new BinaryWriter(fs))
            {
                w.Write((ushort)0);                  // 保留
                w.Write((ushort)1);                  // 类型：1 = 图标
                w.Write((ushort)sizes.Count);
                int offset = 6 + 16 * sizes.Count;
                for (int i = 0; i < sizes.Count; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));   // 宽（256 记为 0）
                    w.Write((byte)(s >= 256 ? 0 : s));   // 高
                    w.Write((byte)0);                    // 调色板色数
                    w.Write((byte)0);                    // 保留
                    w.Write((ushort)1);                  // 颜色平面
                    w.Write((ushort)32);                 // 位深
                    w.Write((uint)pngs[i].Length);
                    w.Write((uint)offset);
                    offset += pngs[i].Length;
                }
                foreach (var p in pngs) w.Write(p);
            }
            return outPath;
        }

        // 等比缩放到 size×size 透明底正方形，输出 PNG 字节（Vista+ 的 ICO 直接内嵌 PNG 数据）
        static byte[] RenderPng(Bitmap src, int size)
        {
            using (var dst = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(dst))
                {
                    g.Clear(Color.Transparent);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.SmoothingMode = SmoothingMode.HighQuality;
                    double k = Math.Min((double)size / src.Width, (double)size / src.Height);
                    int w = Math.Max(1, (int)Math.Round(src.Width * k));
                    int h = Math.Max(1, (int)Math.Round(src.Height * k));
                    g.DrawImage(src, (size - w) / 2, (size - h) / 2, w, h);
                }
                using (var ms = new MemoryStream())
                {
                    dst.Save(ms, ImageFormat.Png);
                    return ms.ToArray();
                }
            }
        }

        // ---------- 工业风 GUI（深灰底、直角、等宽字体，无装饰） ----------

        static readonly Color Bg = Color.FromArgb(28, 28, 30);
        static readonly Color Panel = Color.FromArgb(16, 16, 18);
        static readonly Color Fg = Color.FromArgb(222, 222, 222);
        static readonly Color Dim = Color.FromArgb(150, 150, 155);
        static readonly Color Steel = Color.FromArgb(46, 92, 154);   // 工业钢蓝

        public IcoConverterForm()
        {
            Text = "HIL ICO 转换器";
            ClientSize = new Size(600, 360);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Bg;
            ForeColor = Fg;
            Font = new Font("Consolas", 9F);

            Controls.Add(MakeLabel("源图片", 16, 14));
            pathBox = new TextBox();
            pathBox.Bounds = new Rectangle(16, 36, 430, 23);
            pathBox.BackColor = Panel;
            pathBox.ForeColor = Fg;
            pathBox.BorderStyle = BorderStyle.FixedSingle;
            pathBox.ReadOnly = true;
            Controls.Add(pathBox);
            var btnPick = MakeButton("选择文件...", 454, 34, 130);
            btnPick.Click += delegate { Pick(); };
            Controls.Add(btnPick);

            preview = new PictureBox();
            preview.Bounds = new Rectangle(16, 74, 220, 220);
            preview.BackColor = Panel;
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            Controls.Add(preview);

            Controls.Add(MakeLabel("分辨率（多选，打进同一个 ico）", 252, 74));
            sizeList = new CheckedListBox();
            sizeList.Bounds = new Rectangle(252, 96, 160, 198);
            sizeList.BackColor = Panel;
            sizeList.ForeColor = Fg;
            sizeList.BorderStyle = BorderStyle.FixedSingle;
            sizeList.CheckOnClick = true;
            foreach (var s in AllSizes)
                sizeList.Items.Add(s + " x " + s, s == 16 || s == 32 || s == 48 || s == 256);
            Controls.Add(sizeList);

            var btnExport = MakeButton("导出 ICO", 428, 96, 156);
            btnExport.Height = 40;
            btnExport.BackColor = Steel;
            btnExport.ForeColor = Color.White;
            btnExport.Click += delegate { Export(); };
            Controls.Add(btnExport);
            Controls.Add(MakeLabel("导出位置：本程序所在目录", 428, 144));

            status = new Label();
            status.Bounds = new Rectangle(16, 306, 568, 40);
            status.ForeColor = Dim;
            status.Text = "选择一张 PNG / JPG / BMP 开始";
            Controls.Add(status);
        }

        Label MakeLabel(string text, int x, int y)
        {
            var l = new Label();
            l.Text = text;
            l.Location = new Point(x, y);
            l.AutoSize = true;
            l.ForeColor = Dim;
            return l;
        }

        Button MakeButton(string text, int x, int y, int w)
        {
            var b = new Button();
            b.Text = text;
            b.Bounds = new Rectangle(x, y, w, 27);
            b.FlatStyle = FlatStyle.Flat;
            b.BackColor = Color.FromArgb(52, 52, 56);
            b.ForeColor = Fg;
            b.FlatAppearance.BorderColor = Color.FromArgb(80, 80, 86);
            return b;
        }

        void SetStatus(string text, bool isError)
        {
            status.Text = text;
            status.ForeColor = isError ? Color.FromArgb(220, 90, 80) : Dim;
        }

        void Pick()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "图片文件|*.png;*.jpg;*.jpeg;*.bmp;*.gif|所有文件|*.*";
                dlg.Title = "选择源图片";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var bmp = LoadBitmap(dlg.FileName);
                    if (source != null) source.Dispose();
                    source = bmp;
                    sourcePath = dlg.FileName;
                    pathBox.Text = sourcePath;
                    var old = preview.Image;
                    preview.Image = new Bitmap(bmp);
                    if (old != null) old.Dispose();
                    SetStatus("已加载 " + bmp.Width + "x" + bmp.Height + "，勾选分辨率后导出", false);
                }
                catch (Exception ex) { SetStatus("加载失败：" + ex.Message, true); }
            }
        }

        void Export()
        {
            if (source == null) { SetStatus("先选择一张图片", true); return; }
            var sizes = new List<int>();
            for (int i = 0; i < sizeList.Items.Count; i++)
                if (sizeList.GetItemChecked(i)) sizes.Add(AllSizes[i]);
            if (sizes.Count == 0) { SetStatus("至少勾选一个分辨率", true); return; }
            try
            {
                string outPath = WriteIco(source, sourcePath, sizes);
                string joined = "";
                foreach (var s in sizes) joined += (joined.Length == 0 ? "" : " / ") + s;
                SetStatus("已导出 -> " + Path.GetFileName(outPath) + "（" + joined + " px），就在本程序旁边", false);
            }
            catch (Exception ex) { SetStatus("导出失败：" + ex.Message, true); }
        }
    }
}
