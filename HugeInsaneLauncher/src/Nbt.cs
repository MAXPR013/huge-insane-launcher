// HugeLauncher - 极简 NBT 读取：只为了从 level.dat 里挖出世界种子
// 支持 gzip 压缩的 level.dat；解析成 Dictionary/List 树，够用就行。
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HugeLauncher
{
    static class Nbt
    {
        public static Dictionary<string, object> ReadFile(string path)
        {
            using (var fs = File.OpenRead(path))
            {
                // 1f 8b = gzip
                int b0 = fs.ReadByte(), b1 = fs.ReadByte();
                fs.Position = 0;
                if (b0 == 0x1f && b1 == 0x8b)
                {
                    using (var gz = new GZipStream(fs, CompressionMode.Decompress))
                    using (var ms = new MemoryStream())
                    {
                        gz.CopyTo(ms);
                        return ReadBytes(ms.ToArray());
                    }
                }
                using (var ms = new MemoryStream())
                {
                    fs.CopyTo(ms);
                    return ReadBytes(ms.ToArray());
                }
            }
        }

        public static Dictionary<string, object> ReadBytes(byte[] data)
        {
            int pos = 0;
            byte type = data[pos++];
            if (type != 10) return null;      // 根必须是 compound
            ReadString(data, ref pos);        // 根名（通常空）
            return ReadCompound(data, ref pos);
        }

        static Dictionary<string, object> ReadCompound(byte[] d, ref int p)
        {
            var map = new Dictionary<string, object>();
            while (p < d.Length)
            {
                byte t = d[p++];
                if (t == 0) break;            // TAG_End
                string name = ReadString(d, ref p);
                map[name] = ReadPayload(d, ref p, t);
            }
            return map;
        }

        static List<object> ReadList(byte[] d, ref int p)
        {
            byte elem = d[p++];
            int len = ReadInt(d, ref p);
            var list = new List<object>();
            for (int i = 0; i < len; i++) list.Add(ReadPayload(d, ref p, elem));
            return list;
        }

        static object ReadPayload(byte[] d, ref int p, byte t)
        {
            switch (t)
            {
                case 1: return (sbyte)d[p++];
                case 2: { short v = (short)((d[p] << 8) | d[p + 1]); p += 2; return v; }
                case 3: { int v = ReadInt(d, ref p); return v; }
                case 4: { long v = ReadLong(d, ref p); return v; }
                case 5: { float v = BitConverter.ToSingle(Flip(d, p, 4), 0); p += 4; return v; }
                case 6: { double v = BitConverter.ToDouble(Flip(d, p, 8), 0); p += 8; return v; }
                case 7: { int n = ReadInt(d, ref p); var arr = new byte[n]; Array.Copy(d, p, arr, 0, n); p += n; return arr; }
                case 8: return ReadString(d, ref p);
                case 9: return ReadList(d, ref p);
                case 10: return ReadCompound(d, ref p);
                case 11: { int n = ReadInt(d, ref p); var arr = new int[n]; for (int i = 0; i < n; i++) arr[i] = ReadInt(d, ref p); return arr; }
                case 12: { int n = ReadInt(d, ref p); var arr = new long[n]; for (int i = 0; i < n; i++) arr[i] = ReadLong(d, ref p); return arr; }
                default: throw new InvalidDataException("未知 NBT 类型 " + t);
            }
        }

        static byte[] Flip(byte[] d, int p, int n)
        {
            var tmp = new byte[n];
            for (int i = 0; i < n; i++) tmp[i] = d[p + n - 1 - i];
            return tmp;
        }

        static int ReadInt(byte[] d, ref int p)
        {
            int v = (d[p] << 24) | (d[p + 1] << 16) | (d[p + 2] << 8) | d[p + 3];
            p += 4;
            return v;
        }

        static long ReadLong(byte[] d, ref int p)
        {
            long v = 0;
            for (int i = 0; i < 8; i++) v = (v << 8) | d[p + i];
            p += 8;
            return v;
        }

        static string ReadString(byte[] d, ref int p)
        {
            int len = (d[p] << 8) | d[p + 1];
            p += 2;
            string s = Encoding.UTF8.GetString(d, p, len);
            p += len;
            return s;
        }

        // ---------- 业务入口 ----------

        // 读一个存档的种子；失败返回 null
        public static long? ReadWorldSeed(string levelDatPath)
        {
            try
            {
                var root = ReadFile(levelDatPath);
                if (root == null) return null;
                object dataObj;
                if (!root.TryGetValue("Data", out dataObj)) return null;
                var data = dataObj as Dictionary<string, object>;
                if (data == null) return null;

                // 1.16+：Data.WorldGenSettings.seed
                object wgsObj;
                if (data.TryGetValue("WorldGenSettings", out wgsObj))
                {
                    var wgs = wgsObj as Dictionary<string, object>;
                    if (wgs != null)
                    {
                        object sd;
                        if (wgs.TryGetValue("seed", out sd) && sd is long) return (long)sd;
                    }
                }
                // 老版本：Data.RandomSeed
                object rs;
                if (data.TryGetValue("RandomSeed", out rs) && rs is long) return (long)rs;
                return null;
            }
            catch { return null; }
        }

        // 列出存档目录（名字 + level.dat 路径）
        public static List<string[]> ListSaves()
        {
            var list = new List<string[]>();
            try
            {
                string saves = Path.Combine(Paths.McDir, "saves");
                if (!Directory.Exists(saves)) return list;
                foreach (string dir in Directory.GetDirectories(saves))
                {
                    string lv = Path.Combine(dir, "level.dat");
                    if (File.Exists(lv)) list.Add(new string[] { Path.GetFileName(dir), lv });
                }
            }
            catch { }
            return list;
        }
    }
}
