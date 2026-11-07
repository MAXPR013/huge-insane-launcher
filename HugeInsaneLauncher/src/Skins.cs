// HugeLauncher - 账号皮肤：本地缓存 + 后台拉取（正版走 Mojang，外置走 Yggdrasil profile）
// 拉不到就用默认史蒂夫，绝不阻塞 UI。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;

namespace HugeLauncher
{
    static class Skins
    {
        static readonly Dictionary<string, BitmapSource> mem = new Dictionary<string, BitmapSource>();
        static readonly Dictionary<string, bool> busy = new Dictionary<string, bool>();
        static readonly JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static event Action Updated;   // 后台拉到新皮肤

        static string KeyOf(Account a)
        {
            if (a == null) return "";
            return a.Type + ":" + (string.IsNullOrEmpty(a.Uuid) ? a.Name : a.Uuid);
        }

        static string CacheFile(Account a)
        {
            string k = KeyOf(a);
            var sb = new StringBuilder();
            for (int i = 0; i < k.Length; i++)
            {
                char c = k[i];
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return Path.Combine(Paths.LauncherDir, "cache", "skins", sb.ToString() + ".png");
        }

        // 立即可用的皮肤：内存 → 磁盘缓存 → 默认；同时视情况起后台下载
        public static BitmapSource For(Account a)
        {
            if (a == null || a.Type == "offline") return null;
            string k = KeyOf(a);
            lock (mem)
            {
                BitmapSource b;
                if (mem.TryGetValue(k, out b)) return b;
            }
            try
            {
                string f = CacheFile(a);
                if (File.Exists(f))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(f);
                    bi.EndInit();
                    bi.Freeze();
                    lock (mem) mem[k] = bi;
                    return bi;
                }
            }
            catch { }
            Kick(a, k);
            return null;
        }

        static void Kick(Account a, string k)
        {
            lock (busy)
            {
                if (busy.ContainsKey(k)) return;
                busy[k] = true;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string url = ResolveSkinUrl(a);
                    if (string.IsNullOrEmpty(url)) return;
                    var wc = new WebClient();
                    wc.Headers["User-Agent"] = "HIL";
                    byte[] bytes = wc.DownloadData(url);
                    if (bytes == null || bytes.Length < 100) return;
                    string f = CacheFile(a);
                    Directory.CreateDirectory(Path.GetDirectoryName(f));
                    File.WriteAllBytes(f, bytes);
                    var bi = new BitmapImage();
                    using (var ms = new MemoryStream(bytes))
                    {
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                    }
                    bi.Freeze();
                    lock (mem) mem[k] = bi;
                    var h = Updated;
                    if (h != null) h();
                }
                catch { }
                finally
                {
                    lock (busy) busy.Remove(k);
                }
            });
        }

        // 从 profile 接口的 textures 属性里取皮肤地址
        static string ResolveSkinUrl(Account a)
        {
            string uuid = (a.Uuid ?? "").Replace("-", "");
            if (uuid.Length == 0) return null;
            string profileUrl;
            if (a.Type == "external")
            {
                if (string.IsNullOrEmpty(a.Server)) return null;
                profileUrl = a.Server.TrimEnd('/') + "/sessionserver/session/minecraft/profile/" + uuid;
            }
            else
            {
                profileUrl = "https://sessionserver.mojang.com/session/minecraft/profile/" + uuid;
            }
            var wc = new WebClient();
            wc.Headers["User-Agent"] = "HIL";
            string txt = wc.DownloadString(profileUrl);
            var d = Json.Parse(txt);
            if (d == null || !d.ContainsKey("properties")) return null;
            var props = Json.Arr(d["properties"]);
            if (props == null || props.Count == 0) return null;
            var p0 = Json.Obj(props[0]);
            if (p0 == null) return null;
            string b64 = Json.Str(p0, "value");
            if (string.IsNullOrEmpty(b64)) return null;
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(b64));
            var tex = Json.Parse(json);
            if (tex == null || !tex.ContainsKey("textures")) return null;
            var textures = Json.Obj(tex["textures"]);
            if (textures == null || !textures.ContainsKey("SKIN")) return null;
            var skin = Json.Obj(textures["SKIN"]);
            if (skin == null) return null;
            return Json.Str(skin, "url");
        }
    }
}
