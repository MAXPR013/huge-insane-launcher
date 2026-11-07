// HugeLauncher - 方块图标：和原版库存一样，用方块模型（assets/minecraft/models/*.json 的
// elements 立方体）做等距投影渲染，而不是拿一张别的贴图冒充。
// 只覆盖「没有平面物品贴图」的方块（栅栏/台阶/按钮/床…）；工具等有平面图标的仍用贴图。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HugeLauncher
{
    static class BlockIcon
    {
        const int Size = 64;
        const byte AlphaCut = 8;

        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();
        static readonly Dictionary<string, byte[]> texCache = new Dictionary<string, byte[]>();
        static ZipArchive jar;
        static bool jarTried;

        struct Face
        {
            public double[] P;      // 4 角 xyz（模型坐标 0..16）
            public double[] UV;     // 4 角 uv（0..16）
            public byte[] Tex;      // Bgra32
            public int TexW, TexH;
            public double Depth;
            public string Dir;
        }

        // 判断某个 id 是不是方块（有 block 模型）——方块一律走模型渲染，和原版一致
        static readonly Dictionary<string, bool> isBlockCache = new Dictionary<string, bool>();

        public static bool IsBlock(string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            bool v;
            if (isBlockCache.TryGetValue(id, out v)) return v;
            bool result = false;
            try
            {
                // 先看有没有预渲染好的 3D 图标（内嵌 blocks3d.zip 首次运行释放）。
                // 有了它，就算这台机器没有 .minecraft / 没有 jar，也能显示 3D。
                string pre = Path.Combine(Paths.LauncherDir, "Images", "Blocks3D", id + ".png");
                if (File.Exists(pre))
                {
                    isBlockCache[id] = true;
                    return true;
                }
                var z = Jar;
                if (z != null)
                {
                    if (z.GetEntry("assets/minecraft/models/block/" + id + ".json") != null) result = true;
                    else
                    {
                        var e = z.GetEntry("assets/minecraft/models/item/" + id + ".json");
                        if (e != null)
                        {
                            using (var sr = new StreamReader(e.Open()))
                            {
                                string txt = sr.ReadToEnd();
                                if (txt.IndexOf("\"block/", StringComparison.Ordinal) >= 0 ||
                                    txt.IndexOf("block/", StringComparison.Ordinal) >= 0) result = true;
                            }
                        }
                    }
                }
            }
            catch { }
            isBlockCache[id] = result;
            return result;
        }

        public static BitmapSource Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            BitmapSource b;
            if (cache.TryGetValue(id, out b)) return b;
            Log("=== " + id);
            // 磁盘缓存：渲染一次就存成 png，后面直接读。
            // 单独放 Blocks3D 目录——Items 里是同名平面贴图，不能互相覆盖。
            try
            {
                string f = Path.Combine(Paths.LauncherDir, "Images", "Blocks3D", id + ".png");
                Log("file " + f + " exists=" + File.Exists(f));
                if (!File.Exists(f))
                {
                    BitmapSource bmp = Render(id);
                    Log("render " + (bmp == null ? "null" : "ok"));
                    if (bmp != null) SavePng(bmp, f);
                }
                if (File.Exists(f))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(f, UriKind.Absolute);
                    bi.EndInit();
                    bi.Freeze();
                    cache[id] = bi;
                    return bi;
                }
            }
            catch (Exception ex) { Log("ERR " + ex.GetType().Name + " " + ex.Message); }
            cache[id] = null;
            return null;
        }

        static void Log(string s)
        {
            if (Environment.GetEnvironmentVariable("HIL_ICON_DEBUG") != "1") return;
            try
            {
                File.AppendAllText(Path.Combine(Paths.LauncherDir, "blockicon.log"), s + "\n");
            }
            catch { }
        }

        static void SavePng(BitmapSource bmp, string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bmp));
                using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
                    enc.Save(fs);
            }
            catch { }
        }

        // ---------- jar 访问 ----------
        static ZipArchive Jar
        {
            get
            {
                if (jarTried) return jar;
                jarTried = true;
                try
                {
                    // 优先用固定的原版版本（模型/贴图布局最稳定），没有才退回最新的 jar。
                    // 新版（26.x）把箱子、床这些改成了特殊渲染器，按模型加载会失败。
                    string[] preferred = { "1.21.1", "1.20.1", "1.21", "1.20.6" };
                    string dir = Paths.VersionsDir;
                    for (int i = 0; i < preferred.Length && jar == null; i++)
                    {
                        string p = Path.Combine(dir, preferred[i], preferred[i] + ".jar");
                        if (File.Exists(p)) jar = TryOpen(p);
                    }
                    if (jar == null)
                    {
                        string[] jars = Directory.Exists(dir)
                            ? Directory.GetFiles(dir, "*.jar", SearchOption.AllDirectories)
                            : new string[0];
                        Array.Sort(jars);
                        for (int i = jars.Length - 1; i >= 0 && jar == null; i--)
                            jar = TryOpen(jars[i]);
                    }
                }
                catch { }
                return jar;
            }
        }

        static ZipArchive TryOpen(string path)
        {
            try
            {
                var z = ZipFile.OpenRead(path);
                if (z.GetEntry("assets/minecraft/textures/block/stone.png") != null) return z;
                z.Dispose();
            }
            catch { }
            return null;
        }

        static string ReadText(string entry)
        {
            var z = Jar;
            if (z == null) return null;
            var e = z.GetEntry(entry);
            if (e == null) return null;
            using (var sr = new StreamReader(e.Open()))
                return sr.ReadToEnd();
        }

        static byte[] ReadBytes(string entry)
        {
            var z = Jar;
            if (z == null) return null;
            var e = z.GetEntry(entry);
            if (e == null) return null;
            using (var ms = new MemoryStream())
            {
                using (var s = e.Open()) s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        // ---------- 模型解析 ----------
        class Model
        {
            public Dictionary<string, string> Textures = new Dictionary<string, string>();
            public List<object> Elements = new List<object>();
        }

        static Model LoadModel(string id, int depth)
        {
            Model m = LoadModelRel("item/" + id, depth);
            if (m == null || m.Elements.Count == 0)
            {
                Model b = LoadModelRel("block/" + id, depth);
                if (b != null && b.Elements.Count > 0) m = b;
                else if (m == null) m = b;
            }
            // 栅栏这类没有单体模型，原版库存用的是 *_inventory 模型
            if (m == null || m.Elements.Count == 0)
            {
                Model inv = LoadModelRel("block/" + id + "_inventory", depth);
                if (inv != null && inv.Elements.Count > 0) m = inv;
            }
            if (m == null || m.Elements.Count == 0)
            {
                Model post = LoadModelRel("block/" + id + "_post", depth);
                if (post != null && post.Elements.Count > 0) m = post;
            }
            return m;
        }

        // rel 形如 "item/oak_fence" / "block/oak_fence_inventory"
        static Model LoadModelRel(string rel, int depth)
        {
            if (depth > 8 || string.IsNullOrEmpty(rel)) return null;
            string txt = ReadText("assets/minecraft/models/" + rel + ".json");
            if (txt == null) return null;

            Dictionary<string, object> json = Json(txt);
            if (json == null) return null;

            Model m = null;
            object parentName;
            if (json.TryGetValue("parent", out parentName) && parentName is string)
            {
                string p = (string)parentName;
                p = p.Replace("minecraft:", "");
                if (p.StartsWith("models/")) p = p.Substring(7);
                if (p == "builtin/generated") p = null;
                if (!string.IsNullOrEmpty(p)) m = LoadModelRel(p, depth + 1);
            }
            if (m == null) m = new Model();

            object tex;
            if (json.TryGetValue("textures", out tex) && tex is Dictionary<string, object>)
            {
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)tex)
                    if (kv.Value is string) m.Textures[kv.Key] = (string)kv.Value;
            }
            object els;
            if (json.TryGetValue("elements", out els))
            {
                Log("  elements type=" + (els == null ? "null" : els.GetType().FullName));
                var arr = els as object[];
                if (arr != null) m.Elements = new List<object>(arr);
                else
                {
                    var list = els as System.Collections.IList;
                    if (list != null)
                    {
                        m.Elements = new List<object>();
                        for (int i = 0; i < list.Count; i++) m.Elements.Add(list[i]);
                    }
                }
            }
            Log("  rel=" + rel + " parent=" + (json.ContainsKey("parent") ? json["parent"] : "-") + " els=" + m.Elements.Count);
            return m;
        }

        // 解析 #name → block/xxx
        static string ResolveTex(Dictionary<string, string> tex, string val, int depth)
        {
            if (val == null) return null;
            if (depth > 8) return null;
            if (!val.StartsWith("#")) return val.Replace("minecraft:", "");
            string key = val.Substring(1);
            string next;
            if (!tex.TryGetValue(key, out next)) return null;
            return ResolveTex(tex, next, depth + 1);
        }

        static byte[] LoadTexture(string texPath)
        {
            if (string.IsNullOrEmpty(texPath)) return null;
            byte[] px;
            if (texCache.TryGetValue(texPath, out px)) return px;
            byte[] raw = ReadBytes("assets/minecraft/textures/" + texPath + ".png");
            if (raw == null) { texCache[texPath] = null; return null; }
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(raw);
                bi.EndInit();
                bi.Freeze();
                var bgra = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                int w = bgra.PixelWidth, h = bgra.PixelHeight;
                px = new byte[w * h * 4];
                bgra.CopyPixels(px, w * 4, 0);
                texCache[texPath] = px;
            }
            catch { texCache[texPath] = null; }
            return px;
        }

        // ---------- 渲染 ----------
        static BitmapSource Render(string id)
        {
            Log("jar=" + (Jar == null ? "null" : "ok"));
            Model m = LoadModel(id, 0);
            Log("model=" + (m == null ? "null" : ("els=" + m.Elements.Count + " tex=" + m.Textures.Count)));
            if (m == null || m.Elements.Count == 0) return null;

            var faces = new List<Face>();
            for (int i = 0; i < m.Elements.Count; i++)
            {
                var el = m.Elements[i] as Dictionary<string, object>;
                if (el == null) continue;
                double x1, y1, z1, x2, y2, z2;
                if (!Vec(el, "from", out x1, out y1, out z1)) continue;
                if (!Vec(el, "to", out x2, out y2, out z2)) continue;
                object fobj;
                if (!el.TryGetValue("faces", out fobj)) continue;
                var fd = fobj as Dictionary<string, object>;
                if (fd == null) continue;

                // 模型元素可能带 rotation（火把、梯子这类斜放方块），先算好旋转参数
                double rx = 0, ry = 0, rz = 0;
                double ox = 0, oy = 0, oz = 0;
                object rotObj;
                if (el.TryGetValue("rotation", out rotObj))
                {
                    var rot = rotObj as Dictionary<string, object>;
                    if (rot != null)
                    {
                        double[] org = Nums(rot.ContainsKey("origin") ? rot["origin"] : null);
                        if (org != null && org.Length >= 3) { ox = org[0]; oy = org[1]; oz = org[2]; }
                        double ang = rot.ContainsKey("angle") ? D(rot["angle"]) : 0;
                        string axis = rot.ContainsKey("axis") ? rot["axis"] as string : null;
                        if (axis == "x") rx = ang;
                        else if (axis == "y") ry = ang;
                        else if (axis == "z") rz = ang;
                    }
                }

                foreach (KeyValuePair<string, object> kv in fd)
                {
                    var fc = kv.Value as Dictionary<string, object>;
                    if (fc == null) continue;
                    string texRef = null;
                    object tv;
                    if (fc.TryGetValue("texture", out tv) && tv is string) texRef = (string)tv;
                    string texPath = ResolveTex(m.Textures, texRef, 0);
                    byte[] tex = LoadTexture(texPath);
                    if (tex == null) continue;

                    double[] uv = null;
                    object uvObj;
                    if (fc.TryGetValue("uv", out uvObj))
                    {
                        double[] arr = Nums(uvObj);
                        if (arr != null && arr.Length == 4)
                            uv = new double[] { arr[0], arr[1], arr[2], arr[3] };
                    }
                    if (uv == null) uv = DefaultUV(kv.Key, x1, y1, z1, x2, y2, z2);

                    Face f = new Face();
                    f.P = Corners(kv.Key, x1, y1, z1, x2, y2, z2);
                    if (f.P == null) continue;
                    if (rx != 0 || ry != 0 || rz != 0)
                        Rotate(f.P, ox, oy, oz, rx, ry, rz);
                    f.UV = new double[] { uv[0], uv[1], uv[2], uv[1], uv[2], uv[3], uv[0], uv[3] };
                    f.Tex = tex;
                    f.TexW = TexW(texPath);
                    f.TexH = TexH(texPath);
                    f.Depth = (f.P[0] + f.P[3] + f.P[6] + f.P[9]) / 4.0
                            + (f.P[1] + f.P[4] + f.P[7] + f.P[10]) / 4.0
                            + (f.P[2] + f.P[5] + f.P[8] + f.P[11]) / 4.0;
                    f.Dir = kv.Key;
                    faces.Add(f);
                }
            }
            if (faces.Count == 0) return null;

            faces.Sort(delegate(Face a, Face b) { return a.Depth.CompareTo(b.Depth); });

            // 投影并算包围盒
            var proj = new double[faces.Count][];
            double minX = 1e9, maxX = -1e9, minY = 1e9, maxY = -1e9;
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                var p = new double[8];
                for (int k = 0; k < 4; k++)
                {
                    double X = f.P[k * 3], Y = f.P[k * 3 + 1], Z = f.P[k * 3 + 2];
                    double sx = (X - Z) * 0.8660254;
                    double sy = (X + Z) * 0.5 - Y;
                    p[k * 2] = sx;
                    p[k * 2 + 1] = sy;
                    if (sx < minX) minX = sx;
                    if (sx > maxX) maxX = sx;
                    if (sy < minY) minY = sy;
                    if (sy > maxY) maxY = sy;
                }
                proj[i] = p;
            }
            double spanX = maxX - minX, spanY = maxY - minY;
            if (spanX <= 0.001) spanX = 1;
            if (spanY <= 0.001) spanY = 1;
            double margin = 4;
            double scale = Math.Min((Size - margin * 2) / spanX, (Size - margin * 2) / spanY);
            double offX = (Size - spanX * scale) / 2 - minX * scale;
            double offY = (Size - spanY * scale) / 2 - minY * scale;

            var dst = new byte[Size * Size * 4];
            for (int i = 0; i < faces.Count; i++)
            {
                var f = faces[i];
                var sp = proj[i];
                double sx0 = sp[0] * scale + offX, sy0 = sp[1] * scale + offY;
                double sx1 = sp[2] * scale + offX, sy1 = sp[3] * scale + offY;
                double sx2 = sp[4] * scale + offX, sy2 = sp[5] * scale + offY;
                double sx3 = sp[6] * scale + offX, sy3 = sp[7] * scale + offY;
                double shade = Shade(f.Dir);
                DrawQuad(dst, f, sx0, sy0, sx1, sy1, sx2, sy2, sx3, sy3, shade);
            }

            var bmp = BitmapSource.Create(Size, Size, 96, 96, PixelFormats.Bgra32, null, dst, Size * 4);
            bmp.Freeze();
            return bmp;
        }

        static int TexW(string p) { return TexSize(p, 0); }
        static int TexH(string p) { return TexSize(p, 1); }
        static readonly Dictionary<string, int[]> texSize = new Dictionary<string, int[]>();
        static int TexSize(string path, int idx)
        {
            int[] wh;
            if (texSize.TryGetValue(path, out wh)) return wh[idx];
            int w = 16, h = 16;
            try
            {
                byte[] raw = ReadBytes("assets/minecraft/textures/" + path + ".png");
                if (raw != null)
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.StreamSource = new MemoryStream(raw);
                    bi.EndInit();
                    w = bi.PixelWidth; h = bi.PixelHeight;
                }
            }
            catch { }
            texSize[path] = new int[] { w, h };
            return texSize[path][idx];
        }

        static double Shade(string dir)
        {
            if (dir == "up") return 1.0;
            if (dir == "down") return 0.5;
            if (dir == "north" || dir == "south") return 0.8;
            return 0.6;
        }

        static double[] DefaultUV(string dir, double x1, double y1, double z1, double x2, double y2, double z2)
        {
            if (dir == "up" || dir == "down") return new double[] { x1, z1, x2, z2 };
            if (dir == "north" || dir == "south") return new double[] { x1, 16 - y2, x2, 16 - y1 };
            return new double[] { z1, 16 - y2, z2, 16 - y1 };
        }

        // 绕 origin 依次绕 X/Y/Z 旋转（模型坐标 0..16，角度制）
        static void Rotate(double[] p, double ox, double oy, double oz,
                           double rx, double ry, double rz)
        {
            const double Deg = Math.PI / 180.0;
            for (int i = 0; i < 4; i++)
            {
                double x = p[i * 3] - ox, y = p[i * 3 + 1] - oy, z = p[i * 3 + 2] - oz;
                if (rx != 0)
                {
                    double a = rx * Deg, c = Math.Cos(a), s = Math.Sin(a);
                    double ny = y * c - z * s, nz = y * s + z * c;
                    y = ny; z = nz;
                }
                if (ry != 0)
                {
                    double a = ry * Deg, c = Math.Cos(a), s = Math.Sin(a);
                    double nx = x * c + z * s, nz = -x * s + z * c;
                    x = nx; z = nz;
                }
                if (rz != 0)
                {
                    double a = rz * Deg, c = Math.Cos(a), s = Math.Sin(a);
                    double nx = x * c - y * s, ny = x * s + y * c;
                    x = nx; y = ny;
                }
                p[i * 3] = x + ox;
                p[i * 3 + 1] = y + oy;
                p[i * 3 + 2] = z + oz;
            }
        }

        static double[] Corners(string dir, double x1, double y1, double z1, double x2, double y2, double z2)
        {
            // 每个面 4 角，顺序与 UV 顺序对应（左上、右上、右下、左下）
            if (dir == "north") return new[] { x2, y2, z1, x1, y2, z1, x1, y1, z1, x2, y1, z1 };
            if (dir == "south") return new[] { x1, y2, z2, x2, y2, z2, x2, y1, z2, x1, y1, z2 };
            if (dir == "east") return new[] { x2, y2, z1, x2, y2, z2, x2, y1, z2, x2, y1, z1 };
            if (dir == "west") return new[] { x1, y2, z2, x1, y2, z1, x1, y1, z1, x1, y1, z2 };
            if (dir == "up") return new[] { x1, y2, z2, x2, y2, z2, x2, y2, z1, x1, y2, z1 };
            if (dir == "down") return new[] { x1, y1, z1, x2, y1, z1, x2, y1, z2, x1, y1, z2 };
            return null;
        }

        // 两个三角形填充 + 仿射 UV 采样
        static void DrawQuad(byte[] dst, Face f,
            double x0, double y0, double x1, double y1,
            double x2, double y2, double x3, double y3,
            double shade)
        {
            Tri(dst, f, x0, y0, x1, y1, x2, y2,
                f.UV[0], f.UV[1], f.UV[2], f.UV[3], f.UV[4], f.UV[5], shade);
            Tri(dst, f, x0, y0, x2, y2, x3, y3,
                f.UV[0], f.UV[1], f.UV[4], f.UV[5], f.UV[6], f.UV[7], shade);
        }

        static void Tri(byte[] dst, Face f,
            double ax, double ay, double bx, double by, double cx, double cy,
            double au, double av, double bu, double bv, double cu, double cv,
            double shade)
        {
            double minX = Math.Min(ax, Math.Min(bx, cx));
            double maxX = Math.Max(ax, Math.Max(bx, cx));
            double minY = Math.Min(ay, Math.Min(by, cy));
            double maxY = Math.Max(ay, Math.Max(by, cy));
            int x0 = Math.Max(0, (int)Math.Floor(minX)), x1 = Math.Min(Size - 1, (int)Math.Ceiling(maxX));
            int y0 = Math.Max(0, (int)Math.Floor(minY)), y1 = Math.Min(Size - 1, (int)Math.Ceiling(maxY));
            double d = (by - cy) * (ax - cx) + (cx - bx) * (ay - cy);
            if (Math.Abs(d) < 1e-9) return;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    double px = x + 0.5, py = y + 0.5;
                    double w0 = ((by - cy) * (px - cx) + (cx - bx) * (py - cy)) / d;
                    double w1 = ((cy - ay) * (px - cx) + (ax - cx) * (py - cy)) / d;
                    double w2 = 1 - w0 - w1;
                    if (w0 < -0.001 || w1 < -0.001 || w2 < -0.001) continue;
                    double u = w0 * au + w1 * bu + w2 * cu;
                    double v = w0 * av + w1 * bv + w2 * cv;
                    int tw = f.TexW, th = f.TexH;
                    int tx = (int)(u / 16.0 * tw);
                    int ty = (int)(v / 16.0 * th);
                    if (tx < 0) tx = 0; if (ty < 0) ty = 0;
                    if (tx >= tw) tx = tw - 1;
                    if (ty >= th) ty = th - 1;
                    int si = (ty * tw + tx) * 4;
                    if (f.Tex[si + 3] < AlphaCut) continue;
                    int di = (y * Size + x) * 4;
                    dst[di] = Clamp(f.Tex[si] * shade);
                    dst[di + 1] = Clamp(f.Tex[si + 1] * shade);
                    dst[di + 2] = Clamp(f.Tex[si + 2] * shade);
                    dst[di + 3] = 255;
                }
            }
        }

        static byte Clamp(double v)
        {
            if (v <= 0) return 0;
            if (v >= 255) return 255;
            return (byte)v;
        }

        static double D(object o)
        {
            if (o == null) return 0;
            try { return Convert.ToDouble(o, CultureInfo.InvariantCulture); } catch { return 0; }
        }

        static double[] Nums(object o)
        {
            // JavaScriptSerializer 把 JSON 数组反序列化成 ArrayList，不一定是 object[]
            var arr = o as object[];
            if (arr != null)
            {
                var r0 = new double[arr.Length];
                for (int i = 0; i < arr.Length; i++) r0[i] = D(arr[i]);
                return r0;
            }
            var list = o as System.Collections.IList;
            if (list == null) return null;
            var r = new double[list.Count];
            for (int i = 0; i < list.Count; i++) r[i] = D(list[i]);
            return r;
        }

        static bool Vec(Dictionary<string, object> el, string key, out double x, out double y, out double z)
        {
            x = y = z = 0;
            object v;
            if (!el.TryGetValue(key, out v)) return false;
            double[] a = Nums(v);
            if (a == null || a.Length < 3) return false;
            x = a[0]; y = a[1]; z = a[2];
            return true;
        }

        // 极简 JSON 解析（只用于 models/*.json）
        static Dictionary<string, object> Json(string s)
        {
            try
            {
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                ser.MaxJsonLength = int.MaxValue;
                return ser.Deserialize<Dictionary<string, object>>(s);
            }
            catch { return null; }
        }
    }
}
