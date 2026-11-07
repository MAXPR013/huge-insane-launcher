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
                try { changed |= Fetch("notice", Notices); } catch { }
                if (changed)
                {
                    var h = Updated;
                    if (h != null) h();
                }
            });
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
                string hub = "https://www.minecraft.net/en-us/news";
                string[][] rows = new string[][]
                {
                    new[] { "26.4 Snapshot 7 发布", "新音乐唱片与一批平衡调整", "11-03" },
                    new[] { "26.3 正式版上线", "新的生物群系与结构", "10-21" },
                    new[] { "社区服主访谈：一年做十张地图", "来自苦力怕论坛", "10-18" },
                    new[] { "26.2 补丁：铜傀儡行为修正", "顺带修了 20 个崩溃", "10-09" },
                    new[] { "开发者日志：结构生成怎么调参", "关于要塞与村庄的距离", "09-28" },
                    new[] { "26.1 数据包格式变更", "自定义维度需要迁移", "09-15" },
                    new[] { "月度建筑大赛结果公布", "本期主题：下界交通", "09-02" },
                    new[] { "26.0 正式发布", "新的合成台与物品栏改动", "08-20" },
                    new[] { "服务器性能调优指南", "TPS 从 12 到 20 的实践", "08-06" }
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
                n.Title = "26.12 更新预告：首页小部件、种子地图内测、账号页重做";
                n.Date = "2 天前";
                Notices.Add(n);
            }
        }
    }
}
