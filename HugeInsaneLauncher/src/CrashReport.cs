// HugeLauncher - 错误报告：启动器日志落地（滚动保留 5 个）/ 游戏崩溃分析（规则表）/ 报告导出（打码）
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace HugeLauncher
{
    // ---------- 启动器日志 ----------
    static class HilLog
    {
        static object sync = new object();
        static bool inited;
        static string curFile;

        public static string LogDir { get { return Path.Combine(Paths.LauncherDir, "logs"); } }
        public static string CurrentFile { get { Init(); return curFile; } }

        // 滚动：hil.4 删，3→4，2→3，1→2，hil.log→hil.1.log（幂等）
        public static void Init()
        {
            lock (sync)
            {
                if (inited) return;
                inited = true;
                try
                {
                    Directory.CreateDirectory(LogDir);
                    string f4 = Path.Combine(LogDir, "hil.4.log");
                    if (File.Exists(f4)) File.Delete(f4);
                    for (int i = 3; i >= 1; i--)
                    {
                        string src = Path.Combine(LogDir, "hil." + i + ".log");
                        if (File.Exists(src)) File.Move(src, Path.Combine(LogDir, "hil." + (i + 1) + ".log"));
                    }
                    string cur = Path.Combine(LogDir, "hil.log");
                    if (File.Exists(cur)) File.Move(cur, Path.Combine(LogDir, "hil.1.log"));
                    curFile = cur;
                }
                catch { }
            }
        }

        public static void Write(string msg)
        {
            lock (sync)
            {
                try
                {
                    Init();
                    File.AppendAllText(curFile,
                        "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + msg + "\r\n", new UTF8Encoding(false));
                }
                catch { }
            }
        }

        public static void WriteEnvironment()
        {
            Write("====== Huge Insane Launcher 启动 ======");
            Write(CrashReport.EnvironmentInfo());
        }
    }

    // ---------- 崩溃分析与报告导出 ----------
    static class CrashReport
    {
        public class CrashResult
        {
            public List<string> Reasons = new List<string>(); // 每条已含「原因 + 解决建议」
            public string Excerpt;                            // 日志关键片段（已打码）
            public string DirectFile;                         // 建议直接查看的文件（崩溃报告/hs_err/latest.log）
        }

        // ---------- 环境信息 ----------

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        class MemStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(MemStatus ms);

        public static string EnvironmentInfo()
        {
            var sb = new StringBuilder();
            sb.Append("系统: ").Append(Environment.OSVersion.VersionString)
                .Append(Environment.Is64BitOperatingSystem ? " (64 位)" : " (32 位)").Append("\r\n");
            try
            {
                var ms = new MemStatus();
                ms.Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(ms);
                if (GlobalMemoryStatusEx(ms))
                    sb.Append("内存: 可用 ").Append(ms.AvailPhys / 1048576).Append(" MB / 共 ")
                        .Append(ms.TotalPhys / 1048576).Append(" MB\r\n");
            }
            catch { }
            sb.Append(".NET: ").Append(Environment.Version).Append("\r\n");
            sb.Append("启动器路径: ").Append(Mask(Paths.Root)).Append("\r\n");
            sb.Append("MC 目录: ").Append(Mask(Paths.McDir)).Append("\r\n");
            return sb.ToString().TrimEnd();
        }

        // ---------- 打码 ----------

        static readonly Regex RxUserName = new Regex(@"[A-Za-z]:\\Users\\[^\\\/]+", RegexOptions.Compiled);
        public static string Mask(string s)
        {
            if (s == null) return null;
            return RxUserName.Replace(s, delegate(Match m)
            {
                return m.Value.Substring(0, 3) + "Users\\***";
            });
        }

        // ---------- 文件读取 ----------

        // 读文件尾部（最多 maxBytes），不存在返回 null
        public static string ReadTail(string path, int maxBytes)
        {
            try
            {
                if (!File.Exists(path)) return null;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, fs.Length - maxBytes);
                    fs.Seek(start, SeekOrigin.Begin);
                    using (var r = new StreamReader(fs, Encoding.UTF8, true, 4096, true))
                        return r.ReadToEnd();
                }
            }
            catch { return null; }
        }

        static string NewestFile(string dir, string pattern, TimeSpan maxAge)
        {
            try
            {
                if (!Directory.Exists(dir)) return null;
                string best = null;
                DateTime bestTime = DateTime.MinValue;
                foreach (var f in Directory.GetFiles(dir, pattern))
                {
                    var t = File.GetLastWriteTime(f);
                    if (DateTime.Now - t > maxAge) continue;
                    if (t > bestTime) { bestTime = t; best = f; }
                }
                return best;
            }
            catch { return null; }
        }

        // ---------- 崩溃分析 ----------

        class Rule
        {
            public string[] Any;   // 任一关键词命中（语料整体 Contains，忽略大小写）
            public string Reason;  // 原因+建议文案
            public bool HsErrOnly; // 仅匹配 JVM 原生崩溃日志
        }

        static readonly Rule[] Rules = new Rule[]
        {
            new Rule { Any = new[] { "OutOfMemoryError", "Could not reserve enough space", "Out of Memory Error", "insufficient memory" },
                Reason = "内存不足：游戏分配的运行内存不够。\r\n解决：版本设置 → 把内存调到 4096M 以上再启动；Mod 很多时建议 6144M。" },
            new Rule { Any = new[] { "Missing or unsupported mandatory dependencies", "Incompatible mods found!", "could not find required mod" },
                Reason = "缺少前置 Mod 或 Mod 之间不兼容（详见下方提取信息）。\r\n解决：按提示去 下载 → Mod 搜索安装缺失的前置；或更新/移除冲突的 Mod。" },
            new Rule { Any = new[] { "zip END header not found", "invalid CEN header", "error reading zip file" },
                Reason = "Mod 文件损坏（下载不完整或文件被破坏，不是真的 Mod 冲突）。\r\n解决：删除损坏的 Mod 文件后重新下载；H史可以帮你自动处理。" },
            new Rule { Any = new[] { "DuplicateModsFoundException", "Duplicate mods found" },
                Reason = "同一个 Mod 重复安装了。\r\n解决：版本设置 → Mod 管理，删除重复的那一份。" },
            new Rule { Any = new[] { "Mixin apply failed", "MixinApplyError", "Critical injection failure", "MixinTargetError" },
                Reason = "某个 Mod 的 Mixin 注入失败：Mod 之间或 Mod 与游戏版本不兼容。\r\n解决：确认所有 Mod 都支持当前游戏版本与加载器；逐个禁用最近装的 Mod 定位问题。" },
            new Rule { Any = new[] { "The driver does not appear to support OpenGL", "Couldn't set pixel format", "GLFW error 65542", "WGL: The driver does not appear", "Pixel format not accelerated" },
                Reason = "显卡驱动过旧或不支持 OpenGL。\r\n解决：到显卡官网（Intel / NVIDIA / AMD）下载安装最新驱动后重启电脑再试。" },
            new Rule { Any = new[] { "Unsupported class file major version", "UnsupportedClassVersionError", "has been compiled by a more recent version of the Java Runtime" },
                Reason = "Java 版本不兼容：游戏或 Mod 需要的 Java 与当前运行时不同。\r\n解决：删除该版本后重新下载（HIL 会自动安装匹配的 Java 运行时）。" },
            new Rule { Any = new[] { "has found a non-mod file", "Found a non-mod file", "is not a valid mod file" },
                Reason = "mods 文件夹里有不是 Mod 的文件（下载损坏或放错了东西）。\r\n解决：版本设置 → Mod 管理，检查并移除可疑文件。" },
            new Rule { Any = new[] { "needs language provider", "Cannot load class", "no constructor with 0 args" },
                Reason = "Mod 与当前加载器不匹配（比如把 Forge 版 Mod 装到了 Fabric 上）。\r\n解决：下载与当前加载器对应的 Mod 版本。" },
            new Rule { Any = new[] { "nightconfig", "Failed to load config", "ParsingException" },
                Reason = "某个 Mod 的配置文件损坏。\r\n解决：删除实例目录 config 文件夹里对应 Mod 的配置文件后重启。" },
            new Rule { Any = new[] { "Manually triggered debug crash" },
                Reason = "这是按 F3+C 手动触发的调试崩溃，不是真的故障，不用处理。" },
            new Rule { Any = new[] { "Caught exception from" },
                Reason = "某个 Mod 在加载阶段抛出异常（详见下方提取信息）。\r\n解决：更新或移除该 Mod。" },
            new Rule { Any = new[] { "EXCEPTION_ACCESS_VIOLATION" }, HsErrOnly = true,
                Reason = "JVM 原生崩溃（访问违例），最常见于显卡驱动问题。\r\n解决：优先更新显卡驱动；其次降低内存分配、移除可疑 Mod 再试。" },
        };

        // 从语料提取关键补充信息（前置名、Mod 名等），附加到对应原因后面
        static void Enrich(StringBuilder corpus, List<string> reasons)
        {
            // Forge：Mod ID: 'xxx', Requested by: 'yyy'
            foreach (Match m in Regex.Matches(corpus.ToString(), @"Mod ID: '([^']+)', Requested by: '([^']+)'"))
            {
                AddReason(reasons, "缺少前置：'" + m.Groups[2].Value + "' 需要 '" + m.Groups[1].Value + "' 才能运行。\r\n解决：下载页 → Mod 搜索 '" + m.Groups[1].Value + "' 安装到当前版本。");
            }
            // Fabric：Mod 'xxx' requires mod 'yyy'
            foreach (Match m in Regex.Matches(corpus.ToString(), @"Mod '([^']+)' requires (?:any version of )?(?:mod )?'?([a-z0-9_\-]+)'?"))
            {
                AddReason(reasons, "缺少前置：'" + m.Groups[1].Value + "' 需要 '" + m.Groups[2].Value + "' 才能运行。\r\n解决：下载页 → Mod 搜索 '" + m.Groups[2].Value + "' 安装到当前版本。");
            }
            // 老 Forge：Caught exception from xxx
            foreach (Match m in Regex.Matches(corpus.ToString(), @"Caught exception from ([a-zA-Z0-9_\-]+)"))
            {
                AddReason(reasons, "Mod '" + m.Groups[1].Value + "' 在加载时出错。\r\n解决：更新或移除该 Mod。");
            }
            // Mixin 注入失败：... in mixins.xxx.json:MixinY from mod zzz failed injection check → 点名肇事 Mod
            foreach (Match m in Regex.Matches(corpus.ToString(), @"in\s+(mixins\.[A-Za-z0-9_.\-]+\.json):[A-Za-z0-9_]+\s+from mod ([A-Za-z0-9_\-]+) failed injection check"))
            {
                AddReason(reasons, "肇事 Mod：'" + m.Groups[2].Value + "'（注入配置 " + m.Groups[1].Value + "）。\r\n解决：这通常是它与另一个 Mod（配置名里有提示，如 sodium）的版本搭配不当——把 '" + m.Groups[2].Value + "' 和对方都更新到适配当前游戏版本的构建，或先移除其中一个。");
            }
            // Java class 版本号 → 对应 Java 版本
            var jm = Regex.Match(corpus.ToString(), @"major version (\d+)");
            if (jm.Success)
            {
                int v; int.TryParse(jm.Groups[1].Value, out v);
                string jv = v >= 61 ? ("Java " + (v - 44)) : "更高版本 Java";
                AddReason(reasons, "（补充：class 文件版本 " + jm.Groups[1].Value + " ≈ 需要 " + jv + " 编译/运行。）");
            }
            // Fabric 分析坏 jar：Error analyzing [C:\...\mods\xxx.jar] → 点名损坏文件
            foreach (Match m in Regex.Matches(corpus.ToString(), @"Error analyzing \[([^\]]+\.jar)\]", RegexOptions.IgnoreCase))
            {
                AddReason(reasons, "损坏的 Mod 文件：" + Path.GetFileName(m.Groups[1].Value)
                    + "\r\n解决：删除该文件后重新下载（H史可以帮你自动处理）。");
            }
        }

        // hs_err 的 Problematic frame → 显卡品牌
        static string GpuBrandFromHsErr(string hserr)
        {
            if (hserr == null) return null;
            var m = Regex.Match(hserr, @"Problematic frame:[\s\S]{0,150}?\[([a-zA-Z0-9_\.]+\.dll)");
            if (!m.Success) return null;
            string dll = m.Groups[1].Value.ToLowerInvariant();
            if (dll.StartsWith("ig")) return "Intel";
            if (dll.StartsWith("nvoglv") || dll.StartsWith("nv")) return "NVIDIA";
            if (dll.StartsWith("atio") || dll.StartsWith("amd") || dll.StartsWith("ati")) return "AMD";
            return null;
        }

        // 堆栈反推可疑 Mod：取包名第二段，排除官方/公共库，再与 mods/*.jar 文件名比对
        static readonly HashSet<string> PackageBlacklist = new HashSet<string>
        {
            "mojang", "minecraft", "minecraftforge", "fabricmc", "fabric", "forge", "fml", "cpw",
            "lwjgl", "google", "gson", "apache", "fasterxml", "googlecode", "jcraft", "joptsimple",
            "spongepowered", "llamalad7", "oshi", "jline", "jna", "sun", "jdk", "jetbrains",
            "neoforged", "mcp", "mixinextras", "log4j", "slf4j", "typesafe", "netty", "jetbrains",
        };

        static List<string> SuspiciousMods(string corpus, string instDir)
        {
            var found = new List<string>();
            // 加载器格式堆栈：at TRANSFORMER/<modid>@<版本>/<包名>... → 直接拿到 modid
            foreach (Match m in Regex.Matches(corpus, @"^\s*at TRANSFORMER/([a-z0-9_.\-]+)@", RegexOptions.Multiline | RegexOptions.IgnoreCase))
            {
                string cand = m.Groups[1].Value.ToLowerInvariant();
                if (PackageBlacklist.Contains(cand) || found.Contains(cand)) continue;
                found.Add(cand);
                if (found.Count >= 3) break;
            }
            foreach (Match m in Regex.Matches(corpus, @"^\s*at (?:com|net|org|io)\.([a-z][a-z0-9_]+)\.", RegexOptions.Multiline))
            {
                string cand = m.Groups[1].Value;
                if (PackageBlacklist.Contains(cand) || found.Contains(cand)) continue;
                found.Add(cand);
                if (found.Count >= 3) break;
            }
            // 与 mods 目录文件名比对，命中则换成文件名（更有说服力）
            var mapped = new List<string>();
            try
            {
                string modsDir = Path.Combine(instDir, "mods");
                foreach (var cand in found)
                {
                    string file = null;
                    if (Directory.Exists(modsDir))
                        foreach (var f in Directory.GetFiles(modsDir, "*.jar"))
                        {
                            string fn = Path.GetFileName(f).ToLowerInvariant();
                            if (fn.Contains(cand)) { file = Path.GetFileName(f); break; }
                        }
                    mapped.Add(file ?? cand);
                    if (mapped.Count >= 2) break;
                }
            }
            catch { }
            return mapped;
        }

        static void AddReason(List<string> reasons, string r)
        {
            if (!reasons.Contains(r)) reasons.Add(r);
        }

        // 分析实例目录：收集近期日志 → 规则匹配 → 补充提取 → 堆栈反推
        public static CrashResult Analyze(string instDir)
        {
            var result = new CrashResult();

            // 1. 收集语料
            string crashFile = NewestFile(Path.Combine(instDir, "crash-reports"), "crash-*.txt", TimeSpan.FromMinutes(30));
            string hsErrFile = NewestFile(instDir, "hs_err_pid*.log", TimeSpan.FromMinutes(30));
            string latestFile = Path.Combine(instDir, "logs", "latest.log");
            string crashText = ReadTail(crashFile, 256 * 1024);
            string hsErrText = ReadTail(hsErrFile, 128 * 1024);
            string latestText = ReadTail(latestFile, 256 * 1024);

            result.DirectFile = crashFile ?? hsErrFile ?? (File.Exists(latestFile) ? latestFile : null);

            var corpus = new StringBuilder();
            if (crashText != null) corpus.Append(crashText);
            if (latestText != null) corpus.Append('\n').Append(latestText);
            string corpusStr = corpus.ToString();
            string corpusLower = corpusStr.ToLowerInvariant();
            string hsErrLower = hsErrText == null ? "" : hsErrText.ToLowerInvariant();

            // 2. 规则表匹配
            foreach (var rule in Rules)
            {
                string hay = rule.HsErrOnly ? hsErrLower : corpusLower;
                if (hay.Length == 0) continue;
                foreach (var kw in rule.Any)
                {
                    if (hay.Contains(kw.ToLowerInvariant()))
                    {
                        AddReason(result.Reasons, rule.Reason);
                        break;
                    }
                }
            }

            // 3. 补充信息提取（前置名、Mod 名、Java 版本号）
            Enrich(corpus, result.Reasons);

            // 4. hs_err 显卡品牌
            string brand = GpuBrandFromHsErr(hsErrText);
            if (brand != null)
                AddReason(result.Reasons, "显卡驱动崩溃（识别为 " + brand + " 显卡）。\r\n解决：到 " + brand + " 官网更新显卡驱动后重启电脑。");

            // 5. 堆栈反推可疑 Mod
            foreach (var mod in SuspiciousMods(corpusStr, instDir))
                AddReason(result.Reasons, "疑似 Mod '" + mod + "' 导致崩溃（从堆栈推断，仅供参考）。\r\n解决：版本设置 → Mod 管理，先禁用它再启动验证。");

            if (result.Reasons.Count == 0)
                result.Reasons.Add("未能识别出具体原因。\r\n建议：导出错误报告 zip，发给懂行的人或 Mod 作者求助。");

            // 6. 日志片段（尾部 25 行，打码）
            string tailSrc = crashText ?? latestText ?? hsErrText;
            if (tailSrc != null)
            {
                var lines = tailSrc.Split('\n');
                var keep = new List<string>();
                for (int i = Math.Max(0, lines.Length - 25); i < lines.Length; i++)
                {
                    string l = lines[i].TrimEnd('\r');
                    if (l.Length > 0) keep.Add(l.Length > 300 ? l.Substring(0, 300) : l);
                }
                result.Excerpt = Mask(string.Join("\r\n", keep.ToArray()));
            }
            return result;
        }

        // ---------- 异常格式化（启动器自身崩溃用） ----------

        public static string FormatException(Exception ex)
        {
            if (ex == null) return "未知错误";
            var sb = new StringBuilder();
            var seen = new HashSet<string>();
            int depth = 0;
            for (Exception cur = ex; cur != null && depth < 8; cur = cur.InnerException, depth++)
            {
                if (depth > 0) sb.Append("\r\n→ ");
                sb.Append(cur.GetType().FullName).Append(": ").Append(cur.Message);
                if (cur.StackTrace != null)
                    foreach (var line in cur.StackTrace.Split('\n'))
                    {
                        string t = line.Trim();
                        if (t.Length == 0 || !seen.Add(t)) continue;
                        sb.Append("\r\n   ").Append(t);
                    }
            }
            // 按根因类型给中文提示
            Exception root = ex;
            while (root.InnerException != null) root = root.InnerException;
            string tip = null;
            if (root is UnauthorizedAccessException) tip = "常见原因：文件被占用或权限不足，尝试以管理员身份运行。";
            else if (root is OutOfMemoryException) tip = "常见原因：系统内存不足，关闭一些程序再试。";
            else if (root is System.Net.WebException || root is System.Net.Sockets.SocketException) tip = "常见原因：网络环境不佳，检查网络或稍后再试。";
            else if (root is TypeLoadException || root is BadImageFormatException || root is MissingMethodException) tip = "常见原因：.NET Framework 运行库损坏，建议重装 .NET Framework 4.8。";
            else if (root is IOException) tip = "常见原因：磁盘读写失败，检查磁盘空间与文件占用。";
            if (tip != null) sb.Insert(0, tip + "\r\n\r\n—————— 详细信息 ——————\r\n");
            return Mask(sb.ToString());
        }

        // ---------- 导出错误报告 zip ----------

        // 打包：游戏日志/崩溃报告/hs_err/版本 json/启动器日志/启动参数，全部打码
        public static void ExportReport(string instDir, string versionId, string destZip)
        {
            if (File.Exists(destZip)) File.Delete(destZip);
            using (var zip = ZipFile.Open(destZip, ZipArchiveMode.Create))
            {
                AddFileEntry(zip, Path.Combine(instDir, "logs", "latest.log"), "游戏日志.txt");
                string crashFile = NewestFile(Path.Combine(instDir, "crash-reports"), "crash-*.txt", TimeSpan.FromDays(1));
                AddFileEntry(zip, crashFile, "崩溃报告.txt");
                string hsErrFile = NewestFile(instDir, "hs_err_pid*.log", TimeSpan.FromDays(1));
                AddFileEntry(zip, hsErrFile, "JVM崩溃日志.log");
                if (!string.IsNullOrEmpty(versionId))
                {
                    string vj = Path.Combine(Paths.VersionDir(versionId), versionId + ".json");
                    AddFileEntry(zip, vj, "版本信息.json");
                }
                AddFileEntry(zip, HilLog.CurrentFile, "启动器日志.txt");
                AddFileEntry(zip, Path.Combine(Paths.McDir, "launch.args"), "启动参数.txt");
                // 环境信息单独一份
                var env = zip.CreateEntry("环境信息.txt");
                using (var w = new StreamWriter(env.Open(), new UTF8Encoding(false)))
                    w.Write(EnvironmentInfo());
            }
        }

        static void AddFileEntry(ZipArchive zip, string srcPath, string entryName)
        {
            try
            {
                if (srcPath == null || !File.Exists(srcPath)) return;
                string text = ReadTail(srcPath, 2 * 1024 * 1024);
                if (text == null) return;
                var entry = zip.CreateEntry(entryName);
                using (var w = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                    w.Write(Mask(text));
            }
            catch { }
        }
    }
}
