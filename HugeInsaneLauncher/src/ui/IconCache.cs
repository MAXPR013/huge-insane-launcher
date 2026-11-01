// HugeLauncher.Ui - Modrinth 图标异步缓存（禁止修改本文件）
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace HugeLauncher.Ui
{
    static class IconCache
    {
        static HashSet<string> downloading = new HashSet<string>();
        static Dictionary<string, string> ready = new Dictionary<string, string>();

        static string CachePath(string url)
        {
            string name;
            try { name = Path.GetFileName(new Uri(url).LocalPath); } catch { name = "icon"; }
            if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                && !name.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
                name += ".png";
            return Path.Combine(Paths.LauncherDir, "cache", "icons",
                Math.Abs(url.GetHashCode()).ToString("x") + "_" + name);
        }

        // 已缓存返回路径，否则返回 null 并启动后台下载；下载完成后 onReady(url, path) 在后台线程触发
        public static string Get(string url, Action<string, string> onReady)
        {
            if (string.IsNullOrEmpty(url)) return null;
            lock (ready) { string p; if (ready.TryGetValue(url, out p)) return p; }
            string dest = CachePath(url);
            if (File.Exists(dest) && new FileInfo(dest).Length > 0)
            {
                lock (ready) ready[url] = dest;
                return dest;
            }
            lock (downloading)
            {
                if (downloading.Contains(url)) return null;
                downloading.Add(url);
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.Timeout = 30000;
                    req.UserAgent = "HugeInsaneLauncher/3.0";
                    using (var resp = req.GetResponse())
                    using (var rs = resp.GetResponseStream())
                    using (var fs = new FileStream(dest, FileMode.Create))
                        rs.CopyTo(fs);
                    lock (ready) ready[url] = dest;
                    if (onReady != null) onReady(url, dest);
                }
                catch { }
                finally { lock (downloading) downloading.Remove(url); }
            });
            return null;
        }
    }
}
