// HugeLauncher - 基础设施：路径/配置/JSON/网络/下载池/进度模型
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace HugeLauncher
{
    static class Paths
    {
        public static string Root;
        public static string McDir;
        public static string LauncherDir;
        public static string ConfigFile;
        public static string CliLogFile;

        static Paths()
        {
            Root = AppDomain.CurrentDomain.BaseDirectory;
            McDir = Path.Combine(Root, ".minecraft");
            LauncherDir = Path.Combine(Root, "HugeInsaneLauncher");
            ConfigFile = Path.Combine(LauncherDir, "config.ini");
            CliLogFile = Path.Combine(LauncherDir, "cli.log");
        }

        public static void EnsureDirs()
        {
            // 品牌改名迁移：旧配置目录 HugeGoingLauncher → HugeInsaneLauncher（老用户首次升级时整体搬走）
            string oldDir = Path.Combine(Root, "HugeGoingLauncher");
            if (!Directory.Exists(LauncherDir) && Directory.Exists(oldDir))
            {
                try { Directory.Move(oldDir, LauncherDir); } catch { }
            }
            Directory.CreateDirectory(McDir);
            Directory.CreateDirectory(LauncherDir);
            EnsureImages();
        }

        // 内嵌界面贴图：单文件 exe 分发时（旁边没有 Images/），启动时从 exe 资源释放到配置目录。
        // 资源名规则见 build.bat：img.<相对路径的点号形式>，例如 img.Blocks.Grass.png
        // 已存在的文件一律跳过：老用户目录里的图不会被覆盖，删掉某张也会自动补回。
        static readonly string[] ImageFiles = new string[]
        {
            "Blocks/Anvil.png", "Blocks/CobbleStone.png", "Blocks/CommandBlock.png",
            "Blocks/CraftingTable.png", "Blocks/Egg.png", "Blocks/Furnace.png",
            "Blocks/GoldBlock.png", "Blocks/Grass.png", "Blocks/GrassPath.png",
            "Blocks/RedstoneBlock.png", "Loaders/Fabric.png", "Loaders/NeoForge.png"
        };

        public static void EnsureImages()
        {
            try
            {
                string imgRoot = Path.Combine(LauncherDir, "Images");
                Assembly asm = Assembly.GetExecutingAssembly();
                for (int i = 0; i < ImageFiles.Length; i++)
                {
                    string rel = ImageFiles[i];
                    string dst = Path.Combine(imgRoot, rel.Replace('/', '\\'));
                    if (File.Exists(dst)) continue;
                    string res = "img." + rel.Replace('/', '.');
                    using (Stream s = asm.GetManifestResourceStream(res))
                    {
                        if (s == null) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        using (FileStream fs = new FileStream(dst, FileMode.Create, FileAccess.Write))
                            s.CopyTo(fs);
                    }
                }
            }
            catch { }   // 只读目录等异常不影响启动
        }

        public static string Fwd(string p) { return p.Replace('\\', '/'); }
        public static string VersionsDir { get { return Path.Combine(McDir, "versions"); } }
        public static string VersionDir(string id) { return Path.Combine(VersionsDir, id); }
        public static string LibrariesDir { get { return Path.Combine(McDir, "libraries"); } }
        public static string AssetsDir { get { return Path.Combine(McDir, "assets"); } }
        public static string RuntimeDir { get { return Path.Combine(McDir, "runtime", "jre"); } }
        public static string JavawExe { get { return Path.Combine(RuntimeDir, "bin", "javaw.exe"); } }
        public static string JavaExe { get { return Path.Combine(RuntimeDir, "bin", "java.exe"); } }
        public static string InstancesDir { get { return Path.Combine(McDir, "instances"); } }
        public static string InstanceDir(string name) { return Path.Combine(InstancesDir, name); }
        // 多运行时：按 Mojang 组件名分目录（如 java-runtime-gamma=17 / delta=21），老版本不再被强行塞新 Java
        public static string RuntimeDirFor(string component)
        {
            return Path.Combine(McDir, "runtime", string.IsNullOrEmpty(component) ? "jre" : component);
        }
        public static string JavawExeFor(string component)
        {
            return Path.Combine(RuntimeDirFor(component), "bin", "javaw.exe");
        }
        // 任意可用的 java.exe：旧 jre 优先，否则第一个组件运行时（Forge/NeoForge 安装器用，不挑版本）
        public static string AnyJavaExe
        {
            get
            {
                if (File.Exists(JavaExe)) return JavaExe;
                try
                {
                    string rtRoot = Path.Combine(McDir, "runtime");
                    if (Directory.Exists(rtRoot))
                        foreach (var dir in Directory.GetDirectories(rtRoot))
                        {
                            string p = Path.Combine(dir, "bin", "java.exe");
                            if (File.Exists(p)) return p;
                        }
                }
                catch { }
                return JavaExe;   // 全没有就返回默认路径，让调用方得到"找不到文件"的明确错误
            }
        }
    }

    class Config
    {
        public string Username = "Player";
        public List<string> UserHistory = new List<string>();
        public List<string> RecentLaunches = new List<string>();   // 最近启动的版本（新→旧，悬浮球快速启动用）
        public string VersionId = "";
        public bool ShowSnapshots = false;
        public int MemoryMb = 2048;
        public string Priority = "正常";       // 高 / 高于正常 / 正常
        public double AnimSpeed = 1.0;          // 0 - 2
        public string Theme = "绿";             // 绿/蓝/紫/橙/红/白
        public bool InstallJavaToSystemAsked = false;
        public bool LogExpanded = false;        // 底部日志面板是否展开
        public bool DefenderTipShown = false;   // 是否已提示过 Defender 排除项建议
        public bool AiEnabled = true;           // 开启 AI 史蒂夫（H史），默认开
        public string AiApiKey = "";            // DeepSeek API Key（选填；空 = 内置免费通道 AiClient.BuiltinRelay）
        public string AiEndpoint = "https://api.deepseek.com/v1";   // 仅自有 Key 时生效
        public bool AiAnim = true;              // H史拟人动画（ACTING 演戏开关）
        public bool LiquidGlass = true;         // 液态玻璃（兼容旧字段；true=流光）
        public string Skin = "liquid";          // liquid=流光 / frost=霜面 / solid=素底
        public string Layout = "classic";       // classic=经典横排 / vertical=纵向舞台
        public bool GcBoost = true;             // JVM GC 优化参数（治卡顿，默认开）
        public string ModrinthMirror = "";      // Modrinth 反代基址（空=官方直连）
        public bool FloatBallOn = true;         // 桌面悬浮球（H史/下载/服务器 极简面板）
        public double FloatBallX = -1;          // 球心屏幕坐标；-1 = 默认右下角
        public double FloatBallY = -1;
        public bool LightMode = false;          // 亮色模式（白底深字；渐变罩切换）
        public bool AutoDayNight = true;        // 自动调色：7:00-19:00 亮色，其余暗色（开着时手动开关被托管）
        public bool AutoUpdate = true;          // 启动时自动检查新版本（先问再更）
        public bool SlimeOn = true;             // 流动史莱姆背景（壁纸取色的 metaball 色块）
        public bool SlimeUserSet;               // 用户是否手动动过史莱姆开关（首次按主题给默认值）
        public double SlimeBlurBg = 8;          // 背景模糊半径 px
        public int SlimeFrostMid = 60;          // [已废弃] 中层不透明度调节已砍，启动时锁定 60；字段仅为读旧配置不报错
        public double SlimeBlurFore = 0;        // [已废弃] 前景柔化已砍，启动时锁定 0

        public void PushUserHistory(string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            UserHistory.Remove(name);
            UserHistory.Insert(0, name);
            while (UserHistory.Count > 10) UserHistory.RemoveAt(UserHistory.Count - 1);
        }

        public void PushRecentLaunch(string ver)
        {
            if (string.IsNullOrEmpty(ver)) return;
            RecentLaunches.Remove(ver);
            RecentLaunches.Insert(0, ver);
            while (RecentLaunches.Count > 10) RecentLaunches.RemoveAt(RecentLaunches.Count - 1);
        }

        static string JoinCsv(List<string> l) { return string.Join(",", l.ToArray()); }

        public static Config Load()
        {
            var c = new Config();
            bool hasSkin = false;
            try
            {
                if (!File.Exists(Paths.ConfigFile)) return c;
                foreach (var line in File.ReadAllLines(Paths.ConfigFile, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    string k = line.Substring(0, i).Trim();
                    string v = line.Substring(i + 1).Trim();
                    int n; double d;
                    switch (k)
                    {
                        case "Username": c.Username = v; break;
                        case "UserHistory":
                            c.UserHistory.Clear();
                            foreach (var s in v.Split(',')) if (s.Length > 0) c.UserHistory.Add(s);
                            break;
                        case "RecentLaunches":
                            c.RecentLaunches.Clear();
                            foreach (var s in v.Split(',')) if (s.Length > 0) c.RecentLaunches.Add(s);
                            break;
                        case "VersionId": c.VersionId = v; break;
                        case "ShowSnapshots": c.ShowSnapshots = (v == "1"); break;
                        case "MemoryMb": if (int.TryParse(v, out n)) c.MemoryMb = n; break;
                        case "Priority": c.Priority = v; break;
                        case "AnimSpeed": if (double.TryParse(v, out d)) c.AnimSpeed = d; break;
                        case "Theme": c.Theme = v; break;
                        case "InstallJavaToSystemAsked": c.InstallJavaToSystemAsked = (v == "1"); break;
                        case "LogExpanded": c.LogExpanded = (v == "1"); break;
                        case "DefenderTipShown": c.DefenderTipShown = (v == "1"); break;
                        case "AiEnabled": c.AiEnabled = (v == "1"); break;
                        case "AiApiKey": c.AiApiKey = v; break;
                        case "AiEndpoint": c.AiEndpoint = v; break;
                        case "AiAnim": c.AiAnim = (v == "1"); break;
                        case "LiquidGlass": c.LiquidGlass = (v == "1"); break;
                        case "Skin": c.Skin = v; hasSkin = true; break;
                        case "Layout": c.Layout = v; break;
                        case "GcBoost": c.GcBoost = (v == "1"); break;
                        case "ModrinthMirror": c.ModrinthMirror = v; break;
                        case "FloatBallOn": c.FloatBallOn = (v == "1"); break;
                        case "FloatBallX": if (double.TryParse(v, out d)) c.FloatBallX = d; break;
                        case "FloatBallY": if (double.TryParse(v, out d)) c.FloatBallY = d; break;
                        case "LightMode": c.LightMode = (v == "1"); break;
                        case "AutoDayNight": c.AutoDayNight = (v == "1"); break;
                        case "AutoUpdate": c.AutoUpdate = (v == "1"); break;
                        case "SlimeOn": c.SlimeOn = (v == "1"); break;
                        case "SlimeUserSet": c.SlimeUserSet = (v == "1"); break;
                        case "SlimeBlurBg": if (double.TryParse(v, out d)) c.SlimeBlurBg = d; break;
                        case "SlimeFrostMid": if (int.TryParse(v, out n)) c.SlimeFrostMid = n; break;
                        case "SlimeBlurFore": if (double.TryParse(v, out d)) c.SlimeBlurFore = d; break;
                    }
                }
            }
            catch { }
            if (!hasSkin) c.Skin = c.LiquidGlass ? "liquid" : "solid";
            if (c.Skin != "liquid" && c.Skin != "frost" && c.Skin != "solid") c.Skin = "liquid";
            if (c.Layout != "classic" && c.Layout != "vertical") c.Layout = "classic";
            if (!c.SlimeUserSet) c.SlimeOn = (c.Skin != "frost");   // 史莱姆默认：霜面关，其余开
            c.LiquidGlass = (c.Skin == "liquid");
            return c;
        }

        public void Save()
        {
            try
            {
                LiquidGlass = (Skin == "liquid");
                Paths.EnsureDirs();
                var lines = new List<string>
                {
                    "Username=" + Username,
                    "UserHistory=" + JoinCsv(UserHistory),
                    "RecentLaunches=" + JoinCsv(RecentLaunches),
                    "VersionId=" + VersionId,
                    "ShowSnapshots=" + (ShowSnapshots ? "1" : "0"),
                    "MemoryMb=" + MemoryMb,
                    "Priority=" + Priority,
                    "AnimSpeed=" + AnimSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "Theme=" + Theme,
                    "InstallJavaToSystemAsked=" + (InstallJavaToSystemAsked ? "1" : "0"),
                    "LogExpanded=" + (LogExpanded ? "1" : "0"),
                    "DefenderTipShown=" + (DefenderTipShown ? "1" : "0"),
                    "AiEnabled=" + (AiEnabled ? "1" : "0"),
                    "AiApiKey=" + AiApiKey,
                    "AiEndpoint=" + AiEndpoint,
                    "AiAnim=" + (AiAnim ? "1" : "0"),
                    "LiquidGlass=" + (LiquidGlass ? "1" : "0"),
                    "Skin=" + Skin,
                    "Layout=" + Layout,
                    "GcBoost=" + (GcBoost ? "1" : "0"),
                    "ModrinthMirror=" + ModrinthMirror,
                    "FloatBallOn=" + (FloatBallOn ? "1" : "0"),
                    "FloatBallX=" + FloatBallX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "FloatBallY=" + FloatBallY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "LightMode=" + (LightMode ? "1" : "0"),
                    "AutoDayNight=" + (AutoDayNight ? "1" : "0"),
                    "AutoUpdate=" + (AutoUpdate ? "1" : "0"),
                    "SlimeOn=" + (SlimeOn ? "1" : "0"),
                    "SlimeUserSet=" + (SlimeUserSet ? "1" : "0"),
                    "SlimeBlurBg=" + SlimeBlurBg.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    "SlimeFrostMid=" + SlimeFrostMid,
                    "SlimeBlurFore=" + SlimeBlurFore.ToString(System.Globalization.CultureInfo.InvariantCulture),
                };
                File.WriteAllLines(Paths.ConfigFile, lines.ToArray(), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // ---------- JSON (JavaScriptSerializer 封装) ----------

    class Json
    {
        static JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        public static Dictionary<string, object> Parse(string text) { return ser.Deserialize<Dictionary<string, object>>(text); }
        public static Dictionary<string, object> Obj(object o) { return o as Dictionary<string, object>; }
        public static System.Collections.ArrayList Arr(object o) { return o as System.Collections.ArrayList; }
        public static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return v.ToString();
            return null;
        }
        public static long Long(Dictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            { long n; if (long.TryParse(v.ToString(), out n)) return n; }
            return 0;
        }
        public static bool Bool(Dictionary<string, object> d, string key, bool def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            { bool b; if (bool.TryParse(v.ToString(), out b)) return b; }
            return def;
        }
    }

    // ---------- 进度模型 ----------

    class StepEvent
    {
        public enum Kind { StepStart, StepProgress, StepDone, Log }
        public Kind EventKind;
        public int StepIndex;
        public string StepName = "";
        public long Current, Total;      // 文件数
        public double MbPerSec;          // 当前速度
        public bool Success;
        public string Message = "";

        public static StepEvent Start(int idx, string name) { return new StepEvent { EventKind = Kind.StepStart, StepIndex = idx, StepName = name }; }
        public static StepEvent Prog(int idx, long cur, long tot, double mbps) { return new StepEvent { EventKind = Kind.StepProgress, StepIndex = idx, Current = cur, Total = tot, MbPerSec = mbps }; }
        public static StepEvent Done(int idx, bool ok, string err) { return new StepEvent { EventKind = Kind.StepDone, StepIndex = idx, Success = ok, Message = err ?? "" }; }
        public static StepEvent LogLine(string msg) { return new StepEvent { EventKind = Kind.Log, Message = msg }; }
    }

    // ---------- 网络 ----------

    class DownloadTask
    {
        public string Url, Dest, Sha1;
        public long Size;
        public string Mirror = null;   // 镜像 URL，可为 null（接线前保持 null）
        public DownloadTask(string url, string dest, string sha1, long size)
        { Url = url; Dest = dest; Sha1 = sha1; Size = size; }
    }

    static class Net
    {
        static Net()
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }
            ServicePointManager.DefaultConnectionLimit = 64;
        }

        public static string GetString(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 60000;
            req.UserAgent = "HugeInsaneLauncher/3.0";
            using (var resp = req.GetResponse())
            using (var s = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return s.ReadToEnd();
        }

        static string Sha1Of(string path)
        {
            using (var sha = SHA1.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        // 杀毒软件/索引器会短暂锁住刚落盘的文件：IO 操作带退避重试，重试约 12 秒才放弃
        static T RetryIo<T>(Func<T> f)
        {
            int wait = 400;
            for (int i = 0; ; i++)
            {
                try { return f(); }
                catch (IOException) { if (i >= 7) throw; Thread.Sleep(wait); wait = Math.Min(wait * 2, 2000); }
                catch (UnauthorizedAccessException) { if (i >= 7) throw; Thread.Sleep(wait); wait = Math.Min(wait * 2, 2000); }
            }
        }

        public static bool Verify(string path, string sha1, long size)
        {
            if (!File.Exists(path)) return false;
            var fi = new FileInfo(path);
            if (fi.Length == 0) return false;
            if (size > 0 && fi.Length != size) return false;
            if (!string.IsNullOrEmpty(sha1)) return RetryIo(() => Sha1Of(path) == sha1);
            return true;
        }

        const long ChunkThreshold = 2L * 1024 * 1024;    // >=2MB 尝试分块下载
        const long BigFileThreshold = 16L * 1024 * 1024; // >=16MB 切 8 段
        const int MaxAttempts = 4;                       // 每文件/每分块最大尝试次数

        static HttpWebRequest NewRequest(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = 120000;
            req.UserAgent = "HugeInsaneLauncher/3.0";
            return req;
        }

        // 收到 429/403 时通知下载池降低全局并发
        static void ReportIfThrottled(WebException we)
        {
            var resp = we.Response as HttpWebResponse;
            if (resp != null && ((int)resp.StatusCode == 429 || resp.StatusCode == HttpStatusCode.Forbidden))
                DownloadPool.OnThrottled();
        }

        // 返回本次实际下载字节数（已存在跳过返回 0，失败返回 -1）
        public static long DownloadFile(DownloadTask t, Action<long> bytesCallback)
        {
            if (Verify(t.Dest, t.Sha1, t.Size)) { ResumeClear(t); return 0; }
            Directory.CreateDirectory(Path.GetDirectoryName(t.Dest));
            long r;
            bool chunked = t.Size >= ChunkThreshold && ProbeRange(t.Url);
            if (chunked)
                r = DownloadChunked(t, bytesCallback);
            else
                r = DownloadStreamed(t, bytesCallback);
            if (r < 0)
            {
                // 分块下载保留现场（dest + .hilpart）供断点续传；
                // 整文件下载删掉残留的不完整文件，避免半截文件留盘
                if (!chunked) { try { if (File.Exists(t.Dest)) File.Delete(t.Dest); } catch { } }
            }
            return r;
        }

        // ---------- 分块断点续传：sidecar 记录每块已收字节 ----------

        static string SidecarPath(string dest) { return dest + ".hilpart"; }

        static readonly object resumeLock = new object();

        // 读取续传现场；现场与本次任务不符（大小/哈希/块数/文件缺失）返回全零数组
        static long[] ResumeLoad(DownloadTask t, int chunks)
        {
            var zero = new long[chunks];
            try
            {
                string sc = SidecarPath(t.Dest);
                if (!File.Exists(sc) || !File.Exists(t.Dest)) return zero;
                if (new FileInfo(t.Dest).Length != t.Size) return zero;
                var lines = File.ReadAllLines(sc);
                if (lines.Length != chunks + 3) return zero;
                if (long.Parse(lines[0]) != t.Size) return zero;
                string sha = string.IsNullOrEmpty(t.Sha1) ? "-" : t.Sha1;
                if (lines[1] != sha) return zero;
                if (int.Parse(lines[2]) != chunks) return zero;
                var r = new long[chunks];
                long chunkLen = (t.Size + chunks - 1) / chunks;
                for (int i = 0; i < chunks; i++)
                {
                    long v = long.Parse(lines[3 + i]);
                    long maxLen = Math.Min(chunkLen, t.Size - (long)i * chunkLen);
                    r[i] = (v >= 0 && v <= maxLen) ? v : 0;
                }
                return r;
            }
            catch { return zero; }
        }

        // 落盘续传现场（多线程调用，lock 串行化；崩溃顶多损失一点进度）
        static void ResumeSave(DownloadTask t, long[] received)
        {
            lock (resumeLock)
            {
                try
                {
                    var sb = new StringBuilder();
                    sb.AppendLine(t.Size.ToString());
                    sb.AppendLine(string.IsNullOrEmpty(t.Sha1) ? "-" : t.Sha1);
                    sb.AppendLine(received.Length.ToString());
                    foreach (var r in received) sb.AppendLine(r.ToString());
                    File.WriteAllText(SidecarPath(t.Dest), sb.ToString());
                }
                catch { }
            }
        }

        static void ResumeClear(DownloadTask t)
        {
            try { File.Delete(SidecarPath(t.Dest)); } catch { }
        }

        // 探测服务器是否支持 Range（发 bytes=0-0 看是否回 206）
        static bool ProbeRange(string url)
        {
            try
            {
                var req = NewRequest(url);
                req.AddRange(0, 0);
                using (var resp = (HttpWebResponse)req.GetResponse())
                    return resp.StatusCode == HttpStatusCode.PartialContent;
            }
            catch (WebException we) { ReportIfThrottled(we); return false; }
            catch { return false; }
        }

        // 整文件下载：边下边算 SHA1，失败按 300/600/1200ms 退避并换源重试
        static long DownloadStreamed(DownloadTask t, Action<long> bytesCallback)
        {
            bool hasMirror = !string.IsNullOrEmpty(t.Mirror);
            int backoff = 300;
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                string url = (hasMirror && attempt % 2 == 1) ? t.Mirror : t.Url;
                try
                {
                    long bytes = 0;
                    string hash;
                    using (var resp = NewRequest(url).GetResponse())
                    using (var rs = resp.GetResponseStream())
                    using (var fs = new FileStream(t.Dest, FileMode.Create))
                    using (var sha = SHA1.Create())
                    {
                        var buf = new byte[81920];
                        int n;
                        while ((n = rs.Read(buf, 0, buf.Length)) > 0)
                        {
                            fs.Write(buf, 0, n);
                            sha.TransformBlock(buf, 0, n, null, 0); // 流式喂给 SHA1
                            bytes += n;
                            if (bytesCallback != null) bytesCallback(n);
                        }
                        sha.TransformFinalBlock(buf, 0, 0);
                        hash = BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
                    }
                    bool ok = bytes > 0
                        && (t.Size <= 0 || bytes == t.Size)
                        && (string.IsNullOrEmpty(t.Sha1) || hash == t.Sha1);
                    if (ok) return bytes;
                    // 校验失败同样视为本次失败，继续换源重试
                }
                catch (WebException we) { ReportIfThrottled(we); }
                catch { }
                if (attempt < MaxAttempts - 1) { Thread.Sleep(backoff); backoff *= 2; }
            }
            return -1;
        }

        // 大文件分块并行下载（前提是 ProbeRange 已确认支持 Range）
        static long DownloadChunked(DownloadTask t, Action<long> bytesCallback)
        {
            // 镜像不一定支持 Range（BMCLAPI 分流节点常直接回 200）：先探测，不支持就移出轮换，
            // 免得每个轮到镜像的分块都白跑一次完整请求
            string mirror = t.Mirror;
            if (!string.IsNullOrEmpty(mirror) && !ProbeRange(mirror)) mirror = null;
            int chunks = t.Size >= BigFileThreshold ? 8 : 4;
            long chunkLen = (t.Size + chunks - 1) / chunks;
            // 断点续传：有现场则跳过已下完的分块、半拉子分块从断点续拉
            long[] resumed = ResumeLoad(t, chunks);
            // 预分配总长，各分块 seek 到自己的偏移写（已存在的现场文件 SetLength 等长不毁内容）
            using (var fs = RetryIo(() => new FileStream(t.Dest, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)))
                fs.SetLength(t.Size);
            var results = new bool[chunks];
            var threads = new Thread[chunks];
            for (int i = 0; i < chunks; i++)
            {
                int idx = i;
                long start = i * chunkLen;
                long end = Math.Min(t.Size, start + chunkLen) - 1; // 含端点
                threads[i] = new Thread(delegate() { results[idx] = DownloadChunk(t, mirror, idx, start, end, resumed, bytesCallback); });
                threads[i].IsBackground = true;
                threads[i].Start();
            }
            for (int i = 0; i < chunks; i++) threads[i].Join();
            for (int i = 0; i < chunks; i++) if (!results[i]) return -1;   // 保留现场等下次续传
            // 并行乱序写无法边下边校，最后一次性读盘校验
            if (Verify(t.Dest, t.Sha1, t.Size)) { ResumeClear(t); return t.Size; }
            // 校验失败说明内容不可信，连现场一起清掉重来
            ResumeClear(t);
            try { RetryIo(() => { File.Delete(t.Dest); return 0; }); } catch { }
            return -1;
        }

        // 单分块下载：重试时 Range 从 块起点+已收 继续，且每次换源（主↔镜像交替）
        // resumed：断点续传进度数组（可为 null），每收 512KB 或完成时落盘一次
        static bool DownloadChunk(DownloadTask t, string mirror, int index, long start, long end, long[] resumed, Action<long> bytesCallback)
        {
            bool hasMirror = !string.IsNullOrEmpty(mirror);
            long len = end - start + 1;
            long received = resumed != null ? resumed[index] : 0;
            if (received >= len) return true;   // 续传：该块此前已下完
            long lastSaved = received;
            int backoff = 300;
            for (int attempt = 0; attempt < MaxAttempts && received < len; attempt++)
            {
                // 相邻分块轮流用主/镜像（分块0用主、1用镜像……），重试再换一次源
                string url = t.Url;
                if (hasMirror && (index + attempt) % 2 == 1) url = mirror;
                try
                {
                    var req = NewRequest(url);
                    req.AddRange(start + received, end);
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (resp.StatusCode != HttpStatusCode.PartialContent)
                            throw new WebException("服务器未按 Range 返回 206");
                        using (var rs = resp.GetResponseStream())
                        using (var fs = new FileStream(t.Dest, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
                        {
                            fs.Seek(start + received, SeekOrigin.Begin);
                            var buf = new byte[81920];
                            int n;
                            while (received < len && (n = rs.Read(buf, 0, buf.Length)) > 0)
                            {
                                // 钳位：服务器多吐也不许越过分块边界
                                int w = (int)Math.Min((long)n, len - received);
                                fs.Write(buf, 0, w);
                                received += w;
                                if (bytesCallback != null) bytesCallback(w);
                                if (resumed != null)
                                {
                                    resumed[index] = received;
                                    if (received - lastSaved >= 524288 || received >= len)
                                    {
                                        lastSaved = received;
                                        ResumeSave(t, resumed);
                                    }
                                }
                                if (w < n) break;
                            }
                        }
                    }
                }
                catch (WebException we) { ReportIfThrottled(we); }
                catch { }
                if (received < len && attempt < MaxAttempts - 1) { Thread.Sleep(backoff); backoff *= 2; }
            }
            return received >= len;
        }
    }

    class DownloadPool
    {
        Queue<DownloadTask> queue = new Queue<DownloadTask>();
        List<string> failures = new List<string>();
        int done, total;
        long bytesTotal;
        Stopwatch watch = Stopwatch.StartNew();
        double lastMb;
        Action<long, long, double> progress; // done, total, MB/s
        int maxWorkers;                      // 本次 Run 的 workers，作为动态上限的天花板

        // 自适应并发：全局动态上限（429/403 减半、下限 4；成功后每文件 +1 缓慢回升）
        // 读写一律走 Interlocked，故用普通静态字段（volatile 按 ref 传递会触发 CS0420）
        static int dynLimit = 64;
        static int active;                 // 当前活跃下载数
        static long lastThrottleTicks;     // 上次限流时间

        public DownloadPool(List<DownloadTask> tasks, Action<long, long, double> progress)
        {
            foreach (var t in tasks) queue.Enqueue(t);
            total = tasks.Count;
            this.progress = progress;
        }

        // 收到 429/403 时调用：动态并发上限减半（下限 4）
        public static void OnThrottled()
        {
            Interlocked.Exchange(ref lastThrottleTicks, DateTime.UtcNow.Ticks);
            while (true)
            {
                int cur = dynLimit;
                int next = Math.Max(4, cur / 2);
                if (Interlocked.CompareExchange(ref dynLimit, next, cur) == cur) return;
            }
        }

        // 文件成功完成后调用：距上次限流超 10 秒则上限 +1（不超过本次 workers）
        void OnFileSuccess()
        {
            if (DateTime.UtcNow.Ticks - Interlocked.Read(ref lastThrottleTicks) <= 10L * TimeSpan.TicksPerSecond) return;
            while (true)
            {
                int cur = dynLimit;
                int next = Math.Min(maxWorkers, cur + 1);
                if (next == cur) return;
                if (Interlocked.CompareExchange(ref dynLimit, next, cur) == cur) return;
            }
        }

        public List<string> Run(int workers)
        {
            maxWorkers = workers;
            var threads = new List<Thread>();
            for (int i = 0; i < workers; i++)
            {
                var th = new Thread(new ThreadStart(Worker));
                th.IsBackground = true;
                th.Start();
                threads.Add(th);
            }
            foreach (var th in threads) th.Join();
            return failures;
        }

        void Worker()
        {
            while (true)
            {
                // 活跃下载数达到动态上限时，等 100ms 再取任务（int 读本身原子，直接读即可）
                while (active >= dynLimit) Thread.Sleep(100);
                DownloadTask t;
                lock (queue)
                {
                    if (queue.Count == 0) return;
                    t = queue.Dequeue();
                }
                Interlocked.Increment(ref active);
                long r;
                try
                {
                    r = Net.DownloadFile(t, delegate(long n) { Interlocked.Add(ref bytesTotal, n); });
                }
                finally { Interlocked.Decrement(ref active); }
                if (r < 0) lock (failures) failures.Add(t.Url);
                else OnFileSuccess();
                int d = Interlocked.Increment(ref done);
                if (progress != null && (d % 10 == 0 || d == total))
                {
                    double sec = watch.Elapsed.TotalSeconds;
                    if (sec > 0.5) lastMb = bytesTotal / 1048576.0 / sec;
                    progress(d, total, lastMb);
                }
            }
        }
    }

    static class Env
    {
        // 检测系统 Java（PATH 或常见目录）
        public static bool SystemJavaPresent()
        {
            try
            {
                var psi = new ProcessStartInfo("java.exe", "-version")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                var p = Process.Start(psi);
                p.WaitForExit(5000);
                return p.ExitCode == 0;
            }
            catch { return false; }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        class MemStatus
        {
            public uint Length; public uint MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
            public MemStatus() { Length = 64; }
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(MemStatus ms);

        // 物理内存总量（MB），读不到时按 8192 兜底
        public static long TotalPhysMb()
        {
            var ms = new MemStatus();
            if (GlobalMemoryStatusEx(ms)) return (long)(ms.TotalPhys / 1048576);
            return 8192;
        }
    }
}
