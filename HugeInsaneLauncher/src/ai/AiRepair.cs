// HugeLauncher.Ai - 崩溃错误分类器与修复动作（机读优先，不信 AI 猜）
// 分类器：正则命中即定类；分类不了的交给 AI 读崩溃摘要从动作库里选。
// 旗舰动作 fix_mod_conflict：本地 jar → Modrinth sha1 反查项目 → 枚举候选版本取交集 → 下载替换。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HugeLauncher.Ai
{
    static class AiRepair
    {
        public enum CrashKind { Unknown, ModConflict, OutOfMemory, JavaVersion, MissingDependency, CorruptJar }

        // 最近一次游戏崩溃现场（MainWindow.WatchGame 在 CrashReport.Analyze 之后写入）
        public static string LastVersionId, LastInstanceName, LastInstanceDir;
        public static CrashReport.CrashResult LastResult;

        public static void SetLastCrash(string versionId, string instanceName, string instDir, CrashReport.CrashResult result)
        {
            LastVersionId = versionId;
            LastInstanceName = instanceName;
            LastInstanceDir = instDir;
            LastResult = result;
        }

        // ---------- 分类器（机读优先） ----------

        static readonly Regex RxConflict = new Regex(@"requires .* between|incompatible with|mixin apply failed", RegexOptions.IgnoreCase);
        static readonly Regex RxOom = new Regex(@"outofmemoryerror: java heap space", RegexOptions.IgnoreCase);
        static readonly Regex RxJava = new Regex(@"unsupported class file version|a jni error", RegexOptions.IgnoreCase);
        static readonly Regex RxMissing = new Regex(@"requires .*fabric-api|missing dependency|could not find required mod|missing or unsupported mandatory", RegexOptions.IgnoreCase);
        static readonly Regex RxCorrupt = new Regex(@"zip END header not found|invalid CEN header|error reading zip file", RegexOptions.IgnoreCase);

        public static CrashKind Classify(string corpus)
        {
            if (string.IsNullOrEmpty(corpus)) return CrashKind.Unknown;
            if (RxCorrupt.IsMatch(corpus)) return CrashKind.CorruptJar;
            if (RxOom.IsMatch(corpus)) return CrashKind.OutOfMemory;
            if (RxJava.IsMatch(corpus)) return CrashKind.JavaVersion;
            if (RxMissing.IsMatch(corpus)) return CrashKind.MissingDependency;
            if (RxConflict.IsMatch(corpus)) return CrashKind.ModConflict;
            return CrashKind.Unknown;
        }

        // 收集崩溃语料：最近的崩溃报告尾部 + latest.log 尾部
        static string LoadCorpus()
        {
            if (string.IsNullOrEmpty(LastInstanceDir)) return null;
            var sb = new StringBuilder();
            try
            {
                string crashDir = Path.Combine(LastInstanceDir, "crash-reports");
                if (Directory.Exists(crashDir))
                {
                    string best = null;
                    DateTime bestTime = DateTime.MinValue;
                    foreach (var f in Directory.GetFiles(crashDir, "crash-*.txt"))
                    {
                        var t = File.GetLastWriteTime(f);
                        if (t > bestTime) { bestTime = t; best = f; }
                    }
                    string s = CrashReport.ReadTail(best, 256 * 1024);
                    if (s != null) sb.Append(s);
                }
                string latest = CrashReport.ReadTail(Path.Combine(LastInstanceDir, "logs", "latest.log"), 256 * 1024);
                if (latest != null) sb.Append('\n').Append(latest);
            }
            catch { }
            return sb.Length == 0 ? null : sb.ToString();
        }

        // ---------- 崩溃主动修复入口（气泡「好」→ 这里） ----------

        public static void StartInteractiveFix()
        {
            var th = new Thread(new ThreadStart(delegate
            {
                try
                {
                    string corpus = LoadCorpus();
                    CrashKind kind = Classify(corpus);
                    List<ScriptStep> script = null;
                    string result = null;
                    switch (kind)
                    {
                        case CrashKind.OutOfMemory:
                            script = new List<ScriptStep>
                            {
                                ScriptStep.Say("看起来是内存不够用了，我来帮你把内存加 1G。"),
                                ScriptStep.DoSwing(),
                                ScriptStep.Run(delegate { result = FixMemory(); return result; }),
                                ScriptStep.SayFunc(delegate { return result ?? "处理完了。"; }),
                            };
                            break;
                        case CrashKind.JavaVersion:
                            // Java 版本错：动作库里没有稳妥的自动修复，只解释
                            script = new List<ScriptStep>
                            {
                                ScriptStep.Say("这次是 Java 版本不兼容：游戏或 Mod 需要的 Java 与当前运行时不同。"
                                    + "最省事的办法：到「实例管理」里删掉这个版本重新下载，HIL 会自动配上合适的 Java。"),
                            };
                            break;
                        case CrashKind.MissingDependency:
                            script = new List<ScriptStep>
                            {
                                ScriptStep.Say("看起来是缺了前置 Mod，我去 Modrinth 帮你找来装上。"),
                                ScriptStep.Navigate("instance:mod", LastVersionId),
                                ScriptStep.DoSwing(),
                                ScriptStep.Run(delegate { result = InstallMissingDependency(corpus); return result; }),
                                ScriptStep.SayFunc(delegate { return result ?? "处理完了。"; }),
                            };
                            break;
                        case CrashKind.ModConflict:
                            script = new List<ScriptStep>
                            {
                                ScriptStep.Say("看起来是模组之间打架了，我去 Modrinth 找兼容版本换上。"),
                                ScriptStep.Navigate("instance:mod", LastVersionId),
                                ScriptStep.DoSwing(),
                                ScriptStep.Run(delegate { result = FixModConflict(); return result; }),
                                ScriptStep.SayFunc(delegate { return result ?? "处理完了。"; }),
                            };
                            break;
                        case CrashKind.CorruptJar:
                            script = new List<ScriptStep>
                            {
                                ScriptStep.Say("有一个 Mod 文件损坏了（多半是下载没下完），我帮你把坏文件清掉。"),
                                ScriptStep.Navigate("instance:mod", LastVersionId),
                                ScriptStep.DoSwing(),
                                ScriptStep.Run(delegate { result = FixCorruptJar(corpus); return result; }),
                                ScriptStep.SayFunc(delegate { return result ?? "处理完了。"; }),
                            };
                            break;
                        default:
                            // 分类不了：交给 AI 读崩溃摘要，从动作库里自己选
                            AiSteve.SendSystem(BuildCrashPrompt());
                            return;
                    }
                    SteveDirector.Play(script, null);
                }
                catch (Exception ex)
                {
                    HilLog.Write("AI 修复流程异常: " + ex.Message);
                }
            }));
            th.IsBackground = true;
            th.Start();
        }

        static string BuildCrashPrompt()
        {
            var sb = new StringBuilder();
            sb.Append("【后台消息，不用原样复述】游戏刚刚崩溃了。以下是 HIL 的崩溃分析摘要：\n");
            if (LastResult != null && LastResult.Reasons.Count > 0)
            {
                int n = Math.Min(3, LastResult.Reasons.Count);
                for (int i = 0; i < n; i++) sb.Append(LastResult.Reasons[i]).Append('\n');
            }
            else sb.Append("（本地规则没有识别出原因）\n");
            sb.Append("请用中文向玩家简要解释最可能的原因，并在合适时调用工具修复")
                .Append("（fix_memory / fix_mod_conflict / repair_game_files / install_resource）。")
                .Append("如果只是玩家硬件、正版验证或对方服务器的问题，就只解释，不要动任何东西。");
            return sb.ToString();
        }

        // ---------- fix_memory：Xmx +1024，上限物理内存 80% ----------

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        class MemStatus
        {
            public uint Length, MemoryLoad;
            public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx(MemStatus ms);

        static ulong TotalPhysMb()
        {
            try
            {
                var m = new MemStatus();
                m.Length = (uint)System.Runtime.InteropServices.Marshal.SizeOf(m);
                if (GlobalMemoryStatusEx(m)) return m.TotalPhys / 1048576;
            }
            catch { }
            return 0;
        }

        public static string FixMemory()
        {
            var cfg = AiSteve.Cfg;
            if (cfg == null) return "配置未就绪";
            int cur = cfg.MemoryMb;
            ulong phys = TotalPhysMb();
            int cap = phys > 0 ? (int)(phys * 8 / 10) : 32768;
            int target = Math.Min(cur + 1024, cap);
            if (target <= cur)
                return "内存已经分到 " + cur + " MB 了（接近本机上限 " + cap + " MB），不能再加了。"
                    + "建议移除一些吃内存的 Mod，或降低画质。";
            cfg.MemoryMb = target;
            cfg.Save();
            return "已把游戏内存从 " + cur + " MB 调到 " + target + " MB，重新启动游戏试试吧。";
        }

        // ---------- 崩溃报告里的模组依赖抽取 ----------

        // 从语料抽出"被需要的模组 id"（Forge 与 Fabric 两种行文），去重保序
        static List<string> ExtractRequiredModIds(string corpus)
        {
            var ids = new List<string>();
            if (corpus == null) return ids;
            foreach (Match m in Regex.Matches(corpus, @"Mod ID: '([^']+)', Requested by: '([^']+)'"))
                AddId(ids, m.Groups[1].Value);
            foreach (Match m in Regex.Matches(corpus,
                @"requires (?:any version of |version \S+ of )?(?:mod )?'?([a-z0-9_\-]+)'?", RegexOptions.IgnoreCase))
                AddId(ids, m.Groups[1].Value);
            return ids;
        }

        static void AddId(List<string> ids, string id)
        {
            id = (id ?? "").ToLowerInvariant();
            if (id.Length == 0 || id == "minecraft" || id == "forge" || id == "fabric" || id == "fabricloader") return;
            if (!ids.Contains(id)) ids.Add(id);
        }

        // ---------- 本地 jar → Modrinth 项目 ----------

        static string Sha1Of(string path)
        {
            using (var sha = SHA1.Create())
            using (var fs = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
        }

        // 优先 /v2/version_file（sha1 反查），失败返回 null
        static string LookupProjectByFile(string jarPath)
        {
            try
            {
                string body = "{\"hash\":\"" + Sha1Of(jarPath) + "\",\"algorithm\":\"sha1\"}";
                var req = (HttpWebRequest)WebRequest.Create(Modrinth.ApiBase + "/version_file");
                req.Method = "POST";
                req.ContentType = "application/json";
                req.UserAgent = "HugeInsaneLauncher/3.0";
                req.Timeout = 30000;
                byte[] bytes = Encoding.UTF8.GetBytes(body);
                using (var s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                using (var resp = req.GetResponse())
                using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return Json.Str(Json.Parse(r.ReadToEnd()), "project_id");
            }
            catch { return null; }
        }

        // 退回文件名启发式：按模组 id 搜 Modrinth 取第一个命中
        static string LookupProjectByName(string modId, string gameVersion)
        {
            try
            {
                var hits = Modrinth.Search("mod", modId, gameVersion);
                if (hits.Count > 0) return hits[0].ProjectId;
            }
            catch { }
            return null;
        }

        // ---------- 缺前置：Modrinth 查依赖自动下 ----------

        static string InstallMissingDependency(string corpus)
        {
            if (string.IsNullOrEmpty(LastVersionId)) return "没有崩溃现场可查，先把游戏再跑一次试试？";
            var ids = ExtractRequiredModIds(corpus);
            if (ids.Count == 0) return "日志里没指出具体缺哪个前置，建议把错误报告导出发给懂行的人看看。";
            string instName = LastInstanceName ?? LastVersionId;
            string mc = GameCore.BaseGameVersion(LastVersionId);
            string loader = Modrinth.DetectLoader(LastVersionId);
            var ok = new List<string>();
            var fail = new List<string>();
            foreach (var id in ids)
            {
                if (ok.Count + fail.Count >= 2) break;
                try
                {
                    string pid = LookupProjectByName(id, mc);
                    if (pid == null) { fail.Add(id + "（Modrinth 上没找到）"); continue; }
                    string installed = Modrinth.InstallResource("mod", pid, mc, loader, instName,
                        delegate(string s) { HilLog.Write("[H史] " + s); });
                    ok.Add(id + " → " + installed);
                }
                catch (Exception ex) { fail.Add(id + "（" + ex.Message + "）"); }
            }
            if (ok.Count > 0)
                return "已经帮你装好前置：" + string.Join("、", ok.ToArray())
                    + (fail.Count > 0 ? "；没搞定的：" + string.Join("、", fail.ToArray()) : "")
                    + "。重新启动游戏试试。";
            return "前置没能自动装上：" + string.Join("、", fail.ToArray()) + "。可以让我用 search_resources 再找找。";
        }

        // ---------- 旗舰：fix_mod_conflict ----------

        public static string FixModConflict()
        {
            if (string.IsNullOrEmpty(LastInstanceDir) || string.IsNullOrEmpty(LastVersionId))
                return "最近没有记录到游戏崩溃，没有可修复的现场。";
            string corpus = LoadCorpus();
            var ids = ExtractRequiredModIds(corpus);
            if (ids.Count == 0)
                return "没能从崩溃报告里抽出打架的模组。建议导出错误报告，发给 Mod 作者求助。";

            string modsDir = Path.Combine(LastInstanceDir, "mods");
            if (!Directory.Exists(modsDir)) return "这个实例还没有 mods 文件夹。";
            string mc = GameCore.BaseGameVersion(LastVersionId);
            string loader = Modrinth.DetectLoader(LastVersionId);

            var fixedMods = new List<string>();
            var failed = new List<string>();
            foreach (var id in ids)
            {
                if (fixedMods.Count + failed.Count >= 2) break;   // 一次最多动两个，保守优先
                try
                {
                    // 1. 找本地 jar（文件名含模组 id）
                    string localJar = null;
                    foreach (var f in Directory.GetFiles(modsDir, "*.jar"))
                    {
                        if (Path.GetFileName(f).ToLowerInvariant().Contains(id)) { localJar = f; break; }
                    }

                    // 2. 本地 jar → Modrinth 项目（sha1 反查优先，失败退回文件名启发式）
                    string pid = localJar != null ? LookupProjectByFile(localJar) : null;
                    if (pid == null) pid = LookupProjectByName(id, mc);
                    if (pid == null) { failed.Add(id + "（Modrinth 上没找到对应项目）"); continue; }

                    // 3. 枚举该项目全部候选版本，取满足 游戏版本+加载器 的最新一个
                    //    （崩溃语料里的版本区间写法五花八门，区间匹配退化为"最新兼容版"——
                    //     对"模组A 要求 模组B 某区间"的冲突，把 B 换成当前游戏版本的最新构建即可解开绝大多数情况）
                    Modrinth.MrVersion pick = null;
                    foreach (var v in Modrinth.GetVersionList(pid))
                    {
                        if (!string.IsNullOrEmpty(mc) && v.GameVersions.Count > 0 && !v.GameVersions.Contains(mc)) continue;
                        if (loader != "vanilla" && v.Loaders.Count > 0 && !v.Loaders.Contains(loader)) continue;
                        if (v.File == null) continue;
                        pick = v;
                        break;
                    }
                    if (pick == null) { failed.Add(id + "（没有适配 " + mc + " 的版本）"); continue; }

                    // 4. 下载替换（走下载引擎 sha1 校验），成功后删掉旧 jar
                    string dest = Path.Combine(modsDir, pick.File.Filename);
                    if (Net.DownloadFile(new DownloadTask(pick.File.Url, dest, pick.File.Sha1, pick.File.Size), null) < 0)
                    {
                        failed.Add(id + "（下载失败）");
                        continue;
                    }
                    if (localJar != null && !string.Equals(localJar, dest, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(localJar); } catch { }
                    }
                    fixedMods.Add(id + " → " + (pick.Name ?? pick.VersionNumber));
                }
                catch (Exception ex) { failed.Add(id + "（" + ex.Message + "）"); }
            }

            if (fixedMods.Count > 0)
                return "已经把冲突模组换成兼容版本：" + string.Join("、", fixedMods.ToArray())
                    + (failed.Count > 0 ? "；没搞定的：" + string.Join("、", failed.ToArray()) : "")
                    + "。重新启动游戏试试。";
            return "自动替换没成功：" + string.Join("、", failed.ToArray())
                + "。建议到 实例管理 → 详情 → Mod 里手动更新或移除它们。";
        }

        // ---------- jar 损坏：从语料定位坏文件，删除本实例 mods 目录下的它 ----------

        static string FixCorruptJar(string corpus)
        {
            if (string.IsNullOrEmpty(corpus)) return "没有找到崩溃语料，定位不了损坏的文件。";
            var bad = new List<string>();
            foreach (Match m in Regex.Matches(corpus, @"Error analyzing \[([^\]]+\.jar)\]", RegexOptions.IgnoreCase))
            {
                string p = m.Groups[1].Value;
                if (!bad.Contains(p)) bad.Add(p);
            }
            if (bad.Count == 0)
                return "日志里没指出具体是哪个文件损坏，建议到 Mod 管理里检查最近下载的 Mod。";
            if (string.IsNullOrEmpty(LastInstanceDir)) return "没有崩溃现场可查，先把游戏再跑一次试试？";
            string modsRoot = Path.GetFullPath(Path.Combine(LastInstanceDir, "mods"));
            var deleted = new List<string>();
            var failed = new List<string>();
            foreach (var p in bad)
            {
                string full;
                try { full = Path.GetFullPath(p); }
                catch { failed.Add(Path.GetFileName(p)); continue; }
                // 防误删：只动本实例 mods 目录下的 .jar
                if (!full.StartsWith(modsRoot, StringComparison.OrdinalIgnoreCase)
                    || !full.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                {
                    failed.Add(Path.GetFileName(p) + "（不在 mods 目录，没敢动）");
                    continue;
                }
                try
                {
                    if (File.Exists(full)) { File.Delete(full); deleted.Add(Path.GetFileName(full)); }
                    else deleted.Add(Path.GetFileName(full) + "（文件已经不在了）");
                }
                catch { failed.Add(Path.GetFileName(p)); }
            }
            if (deleted.Count > 0)
                return "已删除损坏的 Mod 文件：" + string.Join("、", deleted.ToArray())
                    + "。重新启动游戏试试；如果之后提示缺前置，再叫我帮你装回来。"
                    + (failed.Count > 0 ? "没处理掉的：" + string.Join("、", failed.ToArray()) : "");
            return "找到了损坏文件但没能删掉：" + string.Join("、", failed.ToArray())
                + "。你可以在 实例管理 → Mod 里手动删除。";
        }
    }
}
