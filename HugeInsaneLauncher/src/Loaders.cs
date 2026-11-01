// HugeLauncher - 加载器：Fabric / NeoForge / Forge 版本列表与安装
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

namespace HugeLauncher
{
    static class Loaders
    {
        public class LoaderVersion { public string Version, Label; }

        // ---------- Fabric ----------

        public static List<LoaderVersion> GetFabricLoaderList(string mcVersion)
        {
            var list = new List<LoaderVersion>();
            string text = GameCore.GetJson("https://meta.fabricmc.net/v2/versions/loader/" + mcVersion);
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var arr = ser.Deserialize<System.Collections.ArrayList>(text);
            foreach (var o in arr)
            {
                var entry = Json.Obj(o);
                var loader = Json.Obj(entry["loader"]);
                string ver = Json.Str(loader, "version");
                bool stable = Json.Bool(loader, "stable", false);
                if (ver != null)
                    list.Add(new LoaderVersion { Version = ver, Label = ver + (stable ? " (稳定)" : "") });
            }
            return list;
        }

        // 返回安装后的版本 id
        public static string InstallFabric(string mcVersion, string loaderVersion, Action<StepEvent> report)
        {
            report(StepEvent.Start(4, "安装 Fabric 加载器 " + loaderVersion));
            string profileUrl = "https://meta.fabricmc.net/v2/versions/loader/" + mcVersion + "/" + loaderVersion + "/profile/json";
            var profile = Json.Parse(GameCore.GetJson(profileUrl));
            // 统一命名为 <mc>-fabric-<加载器版本>：先拼好完整名再查占用，
            // 不存在"暂存名带进最终名"的路径；同名版本已完整安装（json 存在）时
            // NextAvailableId 重定向到 # 副本后缀（残骸目录无 json 不算占用，直接覆盖）
            string fullName = mcVersion + "-fabric-" + loaderVersion;
            string id = GameCore.NextAvailableId(fullName);
            if (id != fullName)
                HilLog.Write("安装 Fabric：" + fullName + " 已存在，本次落为副本 " + id);
            profile["id"] = id;
            string vdir = Paths.VersionDir(id);
            Directory.CreateDirectory(vdir);
            File.WriteAllText(Path.Combine(vdir, id + ".json"),
                new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(profile),
                new UTF8Encoding(false));
            report(StepEvent.Prog(4, 1, 2, 0));
            GameCore.EnsureLibraries(id, delegate(long d, long t, double m) { report(StepEvent.Prog(4, d, t, m)); });
            report(StepEvent.Prog(4, 2, 2, 0));
            report(StepEvent.Done(4, true, null));
            return id;
        }

        // ---------- 通用：Maven 元数据 ----------

        static List<string> MavenVersions(string metadataUrl)
        {
            var list = new List<string>();
            var doc = new XmlDocument();
            doc.LoadXml(GameCore.GetJson(metadataUrl));
            foreach (XmlNode n in doc.SelectNodes("//metadata/versioning/versions/version"))
                list.Add(n.InnerText.Trim());
            return list;
        }

        // ---------- NeoForge ----------

        public static List<LoaderVersion> GetNeoForgeVersions(string mcVersion)
        {
            var all = MavenVersions("https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml");
            // MC 26.2 → neoforge 26.2.x；MC 1.21.1 → neoforge 21.1.x
            string prefix = mcVersion.StartsWith("1.") ? mcVersion.Substring(2) : mcVersion;
            var list = new List<LoaderVersion>();
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].StartsWith(prefix + "."))
                    list.Add(new LoaderVersion { Version = all[i], Label = all[i] });
            return list;
        }

        // ---------- Forge ----------

        public static List<LoaderVersion> GetForgeVersions(string mcVersion)
        {
            var all = MavenVersions("https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml");
            string prefix = mcVersion + "-";
            var list = new List<LoaderVersion>();
            for (int i = all.Count - 1; i >= 0; i--)
                if (all[i].StartsWith(prefix))
                    list.Add(new LoaderVersion { Version = all[i], Label = all[i].Substring(prefix.Length) });
            return list;
        }

        // ---------- 安装器方式（Forge / NeoForge 共用） ----------

        static string RunInstaller(string installerUrl, string installerFile, string label, string brandRepo, Action<StepEvent> report)
        {
            report(StepEvent.Start(4, "下载 " + label + " 安装器与依赖库"));
            string tmp = Path.Combine(Paths.McDir, "temp");
            Directory.CreateDirectory(tmp);
            string jar = Path.Combine(tmp, installerFile);
            var t = new DownloadTask(installerUrl, jar, null, 0);
            GameCore.PreferMirror(t);   // 安装器几 MB：BMCLAPI 主源（国内 CDN 单流足够），官方兜底
            if (Net.DownloadFile(t, null) < 0)
            {
                report(StepEvent.Done(4, false, "安装器下载失败"));
                throw new Exception(label + " 安装器下载失败");
            }

            // 关键：安装器是独立 Java 进程，它自己的下载器裸连海外源，不经过 HIL 引擎/镜像。
            // 先拆 install_profile.json 用 HIL 引擎（带 BMCLAPI 镜像）预拉所有库，
            // 安装器运行时校验通过即跳过下载，从源头绕开裸连被重置的问题
            PreDownloadInstallerLibraries(jar, brandRepo, report);
            report(StepEvent.Done(4, true, null));

            // Forge/NeoForge 安装器要求存在 launcher_profiles.json
            string lp = Path.Combine(Paths.McDir, "launcher_profiles.json");
            if (!File.Exists(lp))
                File.WriteAllText(lp, "{\"profiles\":{\"HugeInsaneLauncher\":{\"name\":\"HugeInsaneLauncher\"}},\"version\":3}", new UTF8Encoding(false));

            report(StepEvent.Start(5, "运行 " + label + " 安装器（耗时较长）"));
            report(StepEvent.Prog(5, 0, 0, 0));
            var psi = new ProcessStartInfo
            {
                FileName = Paths.AnyJavaExe,
                Arguments = "-Djava.net.preferIPv4Stack=true -jar \"" + jar + "\" --installClient \"" + Paths.McDir + "\"",
                WorkingDirectory = tmp,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var p = Process.Start(psi);
            string outLog = "";
            var tOut = p.StandardOutput.ReadToEndAsync();
            var tErr = p.StandardError.ReadToEndAsync();
            p.WaitForExit();
            outLog = tOut.Result + tErr.Result;
            File.WriteAllText(Path.Combine(Paths.LauncherDir, "installer.log"), outLog, new UTF8Encoding(false));
            try { File.Delete(jar); } catch { }
            if (p.ExitCode != 0)
            {
                report(StepEvent.Done(5, false, "安装器退出码 " + p.ExitCode));
                throw new Exception(label + " 安装器执行失败（退出码 " + p.ExitCode + "），详见 HugeInsaneLauncher/installer.log");
            }
            report(StepEvent.Done(5, true, null));
            return outLog;
        }

        // 拆安装器 jar 里的 install_profile.json，用 HIL 引擎预下载全部库到 libraries 目录。
        // brandRepo：该品牌默认 maven 仓库（清单缺 url 时用）；net.minecraft 坐标走 Mojang 库仓库。
        static void PreDownloadInstallerLibraries(string jarPath, string brandRepo, Action<StepEvent> report)
        {
            Dictionary<string, object> profile;
            try
            {
                using (var zip = System.IO.Compression.ZipFile.OpenRead(jarPath))
                {
                    var entry = zip.GetEntry("install_profile.json");
                    if (entry == null) return;
                    using (var s = new StreamReader(entry.Open(), Encoding.UTF8))
                        profile = Json.Parse(s.ReadToEnd());
                }
            }
            catch { return; }

            var tasks = new List<DownloadTask>();
            var seen = new HashSet<string>();
            object tmp;

            // libraries 清单（Forge/NeoForge 安装器通用格式，LibToTask 两种格式都认）
            var libs = profile.TryGetValue("libraries", out tmp) ? Json.Arr(tmp) : null;
            if (libs != null)
                foreach (var o in libs)
                {
                    var lt = GameCore.LibToTask(Json.Obj(o), brandRepo);
                    if (lt != null && !string.IsNullOrEmpty(lt.Url) && seen.Add(lt.Dest)) tasks.Add(lt);
                }

            // 注意：data 段里的 [net.minecraft:client:<mc>-<时间戳>:mappings/slim/extra...] 不是仓库 artifact，
            // 任何 maven 源都 404（那是安装器本地生成物的命名约定；mappings 由安装器自己从 Mojang 版本
            // 清单的 client_mappings 拉取）。曾经试图预下载它们导致老版 Forge（1.20.1 及更早）安装必炸，已移除。

            if (tasks.Count == 0) return;
            var fails = new DownloadPool(tasks, delegate(long d, long t2, double m)
            {
                report(StepEvent.Prog(4, d, t2, m));
            }).Run(24);
            if (fails.Count > 0)
            {
                report(StepEvent.Done(4, false, fails[0]));
                throw new Exception("安装器依赖库预下载失败（" + fails.Count + " 个）：" + fails[0]);
            }
        }

        public static string InstallForge(string mcVersion, string forgeVersion, Action<StepEvent> report)
        {
            // 重复安装：安装器会覆盖同名文件夹，先把已有的完整安装移去 # 后缀保住
            string label = forgeVersion.StartsWith(mcVersion + "-") ? forgeVersion.Substring(mcVersion.Length + 1) : forgeVersion;
            PreserveIfExists("forge-" + forgeVersion);
            PreserveIfExists(mcVersion + "-forge-" + label);
            string url = "https://maven.minecraftforge.net/net/minecraftforge/forge/" + forgeVersion + "/forge-" + forgeVersion + "-installer.jar";
            RunInstaller(url, "forge-installer.jar", "Forge", "https://maven.minecraftforge.net/", report);
            string id = "forge-" + forgeVersion;
            // 新版 forge 安装器生成的版本 id 可能带 forge 前缀，按实际目录查找
            string found = FindInstalledLoaderDir("forge");
            string realId = found ?? id;
            // 统一命名为 <mc>-forge-<版本>：老格式安装产物的改名归一
            string canonical = mcVersion + "-forge-" + label;
            if (realId != canonical && GameCore.IsVersionReady(realId))
            {
                string to = GameCore.NextAvailableId(canonical);
                GameCore.RenameVersion(realId, to);
                realId = to;
            }
            report(StepEvent.Start(6, "补全依赖库"));
            GameCore.EnsureLibraries(realId, delegate(long d, long t2, double m) { report(StepEvent.Prog(6, d, t2, m)); });
            report(StepEvent.Done(6, true, null));
            return realId;
        }

        public static string InstallNeoForge(string mcVersion, string neoVersion, Action<StepEvent> report)
        {
            // 重复安装：先把已有的完整安装移去 # 后缀保住（安装器会覆盖同名文件夹）
            PreserveIfExists("neoforge-" + neoVersion);
            PreserveIfExists(mcVersion + "-neoforge-" + neoVersion);
            string url = "https://maven.neoforged.net/releases/net/neoforged/neoforge/" + neoVersion + "/neoforge-" + neoVersion + "-installer.jar";
            RunInstaller(url, "neoforge-installer.jar", "NeoForge", "https://maven.neoforged.net/releases/", report);
            string found = FindInstalledLoaderDir("neoforge");
            string realId = found ?? ("neoforge-" + neoVersion);
            // 统一命名为 <mc>-neoforge-<版本>：安装器产出是 neoforge-<版本>，改名归一
            string canonical = mcVersion + "-neoforge-" + neoVersion;
            if (realId != canonical && GameCore.IsVersionReady(realId))
            {
                string to = GameCore.NextAvailableId(canonical);
                GameCore.RenameVersion(realId, to);
                realId = to;
            }
            report(StepEvent.Start(6, "补全依赖库"));
            GameCore.EnsureLibraries(realId, delegate(long d, long t2, double m) { report(StepEvent.Prog(6, d, t2, m)); });
            report(StepEvent.Done(6, true, null));
            return realId;
        }

        // 若该版本已被完整安装，移去 # 后缀位置（为安装器腾出原文件夹）
        static void PreserveIfExists(string id)
        {
            if (!GameCore.IsVersionReady(id)) return;
            GameCore.RenameVersion(id, GameCore.NextAvailableId(id));
        }

        // 在 versions 目录找最近生成的加载器版本
        static string FindInstalledLoaderDir(string keyword)        {
            string best = null;
            DateTime bestTime = DateTime.MinValue;
            try
            {
                foreach (var dir in Directory.GetDirectories(Paths.VersionsDir))
                {
                    string id = Path.GetFileName(dir).ToLowerInvariant();
                    if (!id.Contains(keyword)) continue;
                    var t = Directory.GetLastWriteTime(dir);
                    if (t > bestTime) { bestTime = t; best = Path.GetFileName(dir); }
                }
            }
            catch { }
            return best;
        }
    }
}
