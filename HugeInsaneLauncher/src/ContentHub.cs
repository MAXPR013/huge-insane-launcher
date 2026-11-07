// HugeLauncher - 内容通道：MC 新闻 / HIL 公告 / 点子词库
// 只读官方站点的 JSON（/api/*.json），落盘缓存；拉不到就用缓存，缓存也没有就用内置兜底。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace HugeLauncher
{
    class NewsItem
    {
        public string Title = "";
        public string Sub = "";
        public string Date = "";
        public string Cover = "";
        public string Url = "";
    }

    class NoticeItem
    {
        public string Title = "";
        public string Date = "";
        public string Url = "";
    }

    static class ContentHub
    {
        const string Base = "https://hugeinsane.pages.dev/api/";
        public static readonly List<NewsItem> News = new List<NewsItem>();
        public static readonly List<NoticeItem> Notices = new List<NoticeItem>();
        public static event Action Updated;
        static bool started;

        static string CacheDir
        {
            get { return Path.Combine(Paths.LauncherDir, "cache", "content"); }
        }

        static string CachePath(string name) { return Path.Combine(CacheDir, name + ".json"); }

        // 读缓存 → 起后台刷新（一轮只起一次）
        public static void Init()
        {
            LoadCache("news", News);
            LoadCache("notice", Notices);
            if (started) return;
            started = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool changed = false;
                try { changed |= Fetch("news", News); } catch { }
                if (!changed) { try { changed |= FetchMcNews(); } catch { } }
                try { changed |= Fetch("notice", Notices); } catch { }
                if (changed)
                {
                    var h = Updated;
                    if (h != null) h();
                }
            });
        }

        // 强制刷新（设置页/调试用）
        public static void Refresh()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool changed = false;
                try { changed |= Fetch("news", News); } catch { }
                if (!changed) { try { changed |= FetchMcNews(); } catch { } }
                try { changed |= Fetch("notice", Notices); } catch { }
                if (changed)
                {
                    var h = Updated;
                    if (h != null) h();
                }
            });
        }

        // 内容后端没部署时，直接抓 minecraft.net 的真实文章列表（不再编造标题）
        static bool FetchMcNews()
        {
            string html;
            try
            {
                try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; } catch { }
                var wc = new WebClient();
                wc.Headers["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) HIL/" + HugeLauncher.Ui.MainWindow.Version;
                wc.Encoding = System.Text.Encoding.UTF8;
                html = wc.DownloadString("https://www.minecraft.net/en-us/article");
            }
            catch (Exception ex) { return false; }
            if (string.IsNullOrEmpty(html)) return false;
            var seen = new System.Collections.Generic.HashSet<string>();
            var list = new List<NewsItem>();
            var ms = System.Text.RegularExpressions.Regex.Matches(html, "href=\"(/en-us/article/[a-z0-9\\-]+)\"");
            for (int i = 0; i < ms.Count && list.Count < 9; i++)
            {
                string path = ms[i].Groups[1].Value;
                if (!seen.Add(path)) continue;
                var n = new NewsItem();
                n.Title = SlugTitle(path);
                n.Sub = "来自 minecraft.net";
                n.Date = "";
                n.Url = "https://www.minecraft.net" + path;
                list.Add(n);
            }
            if (list.Count == 0) return false;
            lock (News) { News.Clear(); News.AddRange(list); }
            return true;
        }

        static string SlugTitle(string path)
        {
            int slash = path.LastIndexOf('/');
            string slug = slash >= 0 ? path.Substring(slash + 1) : path;
            string[] parts = slug.Split('-');
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpper(parts[i][0])).Append(parts[i].Substring(1));
            }
            return sb.ToString();
        }
        static bool Fetch<T>(string name, List<T> target) where T : class
        {
            var wc = new WebClient();
            wc.Headers["User-Agent"] = "HIL/" + HugeLauncher.Ui.MainWindow.Version;
            wc.Encoding = System.Text.Encoding.UTF8;
            string txt;
            try { txt = wc.DownloadString(Base + name + ".json"); }
            catch { return false; }
            if (string.IsNullOrEmpty(txt) || txt.TrimStart().StartsWith("<")) return false;   // SPA 兜底返回了网页
            var parsed = Parse<T>(txt, name);
            if (parsed == null || parsed.Count == 0) return false;
            lock (target)
            {
                target.Clear();
                target.AddRange(parsed);
            }
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(CachePath(name), txt);
            }
            catch { }
            return true;
        }

        static void LoadCache<T>(string name, List<T> target) where T : class
        {
            try
            {
                string p = CachePath(name);
                if (!File.Exists(p)) return;
                var list = Parse<T>(File.ReadAllText(p), name);
                if (list == null) return;
                target.Clear();
                target.AddRange(list);
            }
            catch { }
        }

        static List<T> Parse<T>(string json, string kind) where T : class
        {
            try
            {
                var d = Json.Parse(json);
                if (d == null || !d.ContainsKey("items")) return null;
                var arr = Json.Arr(d["items"]);
                if (arr == null) return null;
                var list = new List<T>();
                for (int i = 0; i < arr.Count; i++)
                {
                    var o = Json.Obj(arr[i]);
                    if (o == null) continue;
                    if (kind == "news")
                    {
                        var n = new NewsItem();
                        n.Title = Json.Str(o, "title") ?? "";
                        n.Sub = Json.Str(o, "sub") ?? "";
                        n.Date = Json.Str(o, "date") ?? "";
                        n.Cover = Json.Str(o, "cover") ?? "";
                        n.Url = Json.Str(o, "url") ?? "";
                        if (n.Title.Length > 0) list.Add(n as T);
                    }
                    else
                    {
                        var n = new NoticeItem();
                        n.Title = Json.Str(o, "title") ?? "";
                        n.Date = Json.Str(o, "date") ?? "";
                        n.Url = Json.Str(o, "url") ?? "";
                        if (n.Title.Length > 0) list.Add(n as T);
                    }
                }
                return list;
            }
            catch { return null; }
        }

        // 内置兜底：网络和缓存都没有时也要有东西看。
        // 条数给到 9（3 页），「换一批」才有东西可换；每条都带真实外链，
        // 不能空着退回 HIL 官网——那不是新闻链接。
        public static void EnsureFallback()
        {
            if (News.Count == 0)
            {
                // 离线兜底：不编造标题，只提示取不到；链接给能打开的地址
                string hub = "https://www.minecraft.net/en-us/article";
                string[][] rows = new string[][]
                {
                    new[] { "暂时取不到新闻（离线）", "点这里打开 minecraft.net 文章列表", "" },
                    new[] { "检查网络后会自动刷新", "首页新闻每 30 分钟重新拉一次", "" },
                    new[] { "新闻源：minecraft.net", "官方文章列表，打开即跳原文", "" }
                };
                for (int i = 0; i < rows.Length; i++)
                {
                    var n = new NewsItem();
                    n.Title = rows[i][0];
                    n.Sub = rows[i][1];
                    n.Date = rows[i][2];
                    n.Url = hub;
                    News.Add(n);
                }
            }
            if (Notices.Count == 0)
            {
                var n = new NoticeItem();
                n.Title = "HIL " + HugeLauncher.Ui.MainWindow.Version + " 已发布：导航自定义、新手引导、种子地图加速";
                n.Date = "本次更新";
                Notices.Add(n);
            }
        }
    }
}
