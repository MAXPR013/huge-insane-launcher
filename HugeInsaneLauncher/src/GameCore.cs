// HugeLauncher - 游戏核心：版本清单/下载/版本链解析/参数构建/启动
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace HugeLauncher
{
    static class GameCore
    {
        public const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
        public const string RuntimeListUrl = "https://launchermeta.mojang.com/v1/products/java-runtime/2ec0cc96c44e5a76b9c8b7c39df7210883d12871/all.json";
        public const string AssetBase = "https://resources.download.minecraft.net/";

        public class VersionEntry { public string Id, Type, Url, ReleaseTime; }

        // ---------- BMCLAPI 镜像（国内提速，官方源兜底；两端点均实测支持 Range 206） ----------

        static string Swap(string url, string from, string to)
        {
            return url.StartsWith(from) ? to + url.Substring(from.Length) : null;
        }

        // 官方 URL → BMCLAPI 镜像 URL（无镜像返回 null）
        public static string BmclOf(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            const string B = "https://bmclapi2.bangbang93.com/";
            return Swap(url, "https://resources.download.minecraft.net/", B + "assets/")
                ?? Swap(url, "https://libraries.minecraft.net/", B + "maven/")
                ?? Swap(url, "https://meta.fabricmc.net/", B + "fabric-meta/")
                ?? Swap(url, "https://maven.fabricmc.net/", B + "maven/")
                ?? Swap(url, "https://maven.minecraftforge.net/", B + "maven/")
                ?? Swap(url, "https://maven.neoforged.net/releases/", B + "maven/")
                ?? Swap(url, "https://piston-data.mojang.com/", B)
                ?? Swap(url, "https://piston-meta.mojang.com/", B)
                ?? Swap(url, "https://launchermeta.mojang.com/", B)
                ?? Swap(url, "https://launcher.mojang.com/", B);
        }

        // 小文件任务改主源为 BMCLAPI，官方原 URL 降为兜底镜像（引擎自动换源/分摊）
        public static void PreferMirror(DownloadTask t)
        {
            if (t == null || string.IsNullOrEmpty(t.Url)) return;
            string bm = BmclOf(t.Url);
            if (bm == null) return;
            t.Mirror = t.Url;
            t.Url = bm;
        }

        // JSON 元数据：镜像优先，失败回退官方
        public static string GetJson(string url)
        {
            string bm = BmclOf(url);
            if (bm != null)
            {
                try { return Net.GetString(bm); } catch { }
            }
            return Net.GetString(url);
        }

        // 大文件优先出队，防止最后一个大文件拖尾
        static void BigFirst(List<DownloadTask> tasks)
        {
            tasks.Sort(delegate(DownloadTask a, DownloadTask b) { return b.Size.CompareTo(a.Size); });
        }

        public static List<VersionEntry> GetVersionList()
        {
            var list = new List<VersionEntry>();
            var m = Json.Parse(GetJson(VersionManifestUrl));
            foreach (var o in Json.Arr(m["versions"]))
            {
                var v = Json.Obj(o);
                list.Add(new VersionEntry
                {
                    Id = Json.Str(v, "id"),
                    Type = Json.Str(v, "type"),
                    Url = Json.Str(v, "url"),
                    ReleaseTime = Json.Str(v, "releaseTime")
                });
            }
            return list;
        }

        // 已安装版本（versions/<id>/<id>.json 存在即可，含加载器版本）
        public static List<string> GetInstalledVersions()
        {
            var list = new List<string>();
            try
            {
                foreach (var dir in Directory.GetDirectories(Paths.VersionsDir))
                {
                    string id = Path.GetFileName(dir);
                    if (File.Exists(Path.Combine(dir, id + ".json"))) list.Add(id);
                }
            }
            catch { }
            list.Sort();
            return list;
        }

        public static bool IsVersionReady(string id)
        {
            return File.Exists(Path.Combine(Paths.VersionDir(id), id + ".json"));
        }

        // ---------- 重复安装（# 后缀） ----------

        // 计算不冲突的版本 id：baseId 已被完整安装占用时依次试 baseId#、baseId##...
        // 注意：文件夹存在但 json 缺失（上次下载失败的残骸）不算占用，直接覆盖修复
        public static string NextAvailableId(string baseId)
        {
            string id = baseId;
            while (IsVersionReady(id)) id += "#";
            return id;
        }

        // 重命名已安装版本：移动文件夹、重命名 json/jar、改写 json 里的 id 字段
        public static void RenameVersion(string oldId, string newId)
        {
            string oldDir = Paths.VersionDir(oldId);
            string newDir = Paths.VersionDir(newId);
            Directory.Move(oldDir, newDir);
            string newJson = Path.Combine(newDir, newId + ".json");
            string oldJsonMoved = Path.Combine(newDir, oldId + ".json");
            if (File.Exists(oldJsonMoved))
            {
                File.Move(oldJsonMoved, newJson);
                try
                {
                    var j = Json.Parse(File.ReadAllText(newJson, Encoding.UTF8));
                    j["id"] = newId;
                    File.WriteAllText(newJson,
                        new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(j),
                        new UTF8Encoding(false));
                }
                catch { }
            }
            string oldJar = Path.Combine(newDir, oldId + ".jar");
            if (File.Exists(oldJar)) File.Move(oldJar, Path.Combine(newDir, newId + ".jar"));
        }

        // 该版本对应的原版 MC 版本号：派生版本读 json 的 inheritsFrom；否则自身 id（去掉 # 副本后缀）
        public static string BaseGameVersion(string id)
        {
            if (string.IsNullOrEmpty(id)) return id;
            try
            {
                // 沿 inheritsFrom 链走到底：整合包实例 → 加载器档案 → 真正的 MC 版本（防环上限 8 跳）
                string cur = id;
                for (int hop = 0; hop < 8; hop++)
                {
                    string p = Path.Combine(Paths.VersionDir(cur), cur + ".json");
                    if (!File.Exists(p)) break;
                    string parent = Json.Str(Json.Parse(File.ReadAllText(p, Encoding.UTF8)), "inheritsFrom");
                    if (string.IsNullOrEmpty(parent) || parent == cur) break;
                    cur = parent;
                }
                if (cur != id) return cur;
            }
            catch { }
            return id.TrimEnd('#');
        }

        // ---------- 旧格式加载器版本迁移 ----------

        // 把旧格式加载器版本统一改名为 <mc>-<loader>-<加载器版本>：
        //   fabric-loader-<lv>-<mc> → <mc>-fabric-<lv>
        //   neoforge-<nv>           → <mc>-neoforge-<nv>（mc 取 json 的 inheritsFrom）
        //   forge-<fv>              → <mc>-forge-<fv>（fv 自带 mc 前缀时去掉）
        // 连带处理：# 副本后缀保留、改名前备份 json 到 cache/migration-backup、
        // instances/ 同名目录改名、hil-instance.txt 启动 id 同步更新。
        // 返回 旧id → 新id 映射（调用方用它更新 Config.VersionId）。
        public static Dictionary<string, string> MigrateLoaderVersionIds()
        {
            var map = new Dictionary<string, string>();
            string[] dirs;
            try { dirs = Directory.GetDirectories(Paths.VersionsDir); }
            catch { return map; }

            foreach (var dir in dirs)
            {
                string oldId = Path.GetFileName(dir);
                // # 副本后缀：剥掉参与解析，新名末尾补回
                string baseOld = oldId.TrimEnd('#');
                string hashes = oldId.Substring(baseOld.Length);

                string loader = null, loaderVer = null, mc = null;
                if (baseOld.StartsWith("fabric-loader-"))
                {
                    string rest = baseOld.Substring("fabric-loader-".Length);
                    int dash = rest.IndexOf('-');
                    if (dash <= 0) continue;
                    loader = "fabric";
                    loaderVer = rest.Substring(0, dash);
                    mc = rest.Substring(dash + 1);
                }
                else if (baseOld.StartsWith("neoforge-"))
                {
                    loader = "neoforge";
                    loaderVer = baseOld.Substring("neoforge-".Length);
                }
                else if (baseOld.StartsWith("forge-"))
                {
                    loader = "forge";
                    loaderVer = baseOld.Substring("forge-".Length);
                }
                else continue;

                // mc 以 json 的 inheritsFrom 为准（neoforge/forge 版本号推不出 mc）
                try
                {
                    string jp = Path.Combine(dir, oldId + ".json");
                    if (File.Exists(jp))
                    {
                        string parent = Json.Str(Json.Parse(File.ReadAllText(jp, Encoding.UTF8)), "inheritsFrom");
                        if (!string.IsNullOrEmpty(parent)) mc = parent;
                    }
                }
                catch { }
                if (string.IsNullOrEmpty(mc) || string.IsNullOrEmpty(loaderVer)) continue;
                if (loader == "forge" && loaderVer.StartsWith(mc + "-"))
                    loaderVer = loaderVer.Substring(mc.Length + 1);

                string newId = mc + "-" + loader + "-" + loaderVer + hashes;
                if (newId == oldId) continue;
                newId = NextAvailableId(newId);

                // 备份 json 后再改名
                try
                {
                    string backupDir = Path.Combine(Paths.LauncherDir, "cache", "migration-backup");
                    Directory.CreateDirectory(backupDir);
                    string srcJson = Path.Combine(dir, oldId + ".json");
                    if (File.Exists(srcJson)) File.Copy(srcJson, Path.Combine(backupDir, oldId + ".json"), true);
                }
                catch { }

                try
                {
                    RenameVersion(oldId, newId);
                    map[oldId] = newId;
                }
                catch { }
            }

            if (map.Count == 0) return map;

            // instances/<旧id> 目录改名（各版本独立的 mods 等数据）
            foreach (var kv in map)
            {
                try
                {
                    string oldInst = Paths.InstanceDir(kv.Key);
                    string newInst = Paths.InstanceDir(kv.Value);
                    if (Directory.Exists(oldInst) && !Directory.Exists(newInst))
                        Directory.Move(oldInst, newInst);
                }
                catch { }
            }
            // 整合包实例记录的启动版本 id
            try
            {
                foreach (var instDir in Directory.GetDirectories(Paths.InstancesDir))
                {
                    string f = Path.Combine(instDir, "hil-instance.txt");
                    if (!File.Exists(f)) continue;
                    string launchId = File.ReadAllText(f, Encoding.UTF8).Trim();
                    string mapped;
                    if (map.TryGetValue(launchId, out mapped))
                        File.WriteAllText(f, mapped, new UTF8Encoding(false));
                }
            }
            catch { }
            return map;
        }

        // ---------- 规则 ----------

        public static bool RuleAllowed(Dictionary<string, object> lib)
        {
            bool allowed = true;
            object rulesObj;
            if (!lib.TryGetValue("rules", out rulesObj) || rulesObj == null) return true;
            foreach (var o in Json.Arr(rulesObj))
            {
                var rule = Json.Obj(o);
                bool allow = Json.Str(rule, "action") == "allow";
                object osObj, featObj;
                if (rule.TryGetValue("features", out featObj) && featObj != null) { allowed = false; continue; } // 我们不启用任何 feature
                var os = rule.TryGetValue("os", out osObj) ? Json.Obj(osObj) : null;
                if (os == null) allowed = allow;
                else
                {
                    string name = Json.Str(os, "name");
                    allowed = (name == "windows") ? allow : !allow;
                }
            }
            return allowed;
        }

        // ---------- 下载 ----------

        // 下载原版文件（不含加载器安装）。返回版本 json 文本所在路径。
        // 用户主动下载原版：重复版本自动重定向到 # 后缀文件夹，返回实际版本 id
        public static string DownloadVanilla(VersionEntry ve, Action<StepEvent> report)
        {
            string id = NextAvailableId(ve.Id);
            DownloadVanillaTo(ve, id, report);
            return id;
        }

        // 作为依赖补装原版：已就绪则跳过，未就绪原地修复（不做 # 重定向）
        public static void EnsureVanilla(VersionEntry ve, Action<StepEvent> report)
        {
            if (IsVersionReady(ve.Id)) return;
            DownloadVanillaTo(ve, ve.Id, report);
        }

        static void DownloadVanillaTo(VersionEntry ve, string id, Action<StepEvent> report)
        {
            string vdir = Paths.VersionDir(id);
            Directory.CreateDirectory(vdir);

            report(StepEvent.Start(0, "获取版本信息 " + ve.Id));
            string vjsonPath = Path.Combine(vdir, id + ".json");
            string vjsonText = GetJson(ve.Url);
            if (id != ve.Id)
            {
                // 副本：json 里的 id 字段同步改成新 id
                var vj = Json.Parse(vjsonText);
                vj["id"] = id;
                vjsonText = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(vj);
            }
            File.WriteAllText(vjsonPath, vjsonText, new UTF8Encoding(false));
            var v = Json.Parse(File.ReadAllText(vjsonPath, Encoding.UTF8));
            report(StepEvent.Done(0, true, null));

            // 客户端 + 库（步骤1）与资产（步骤2）并行：库是大文件吃带宽，资产是几千个小文件吃连接数，
            // 两类互补，同时跑互不争抢；两池共享全局动态并发上限（DownloadPool.dynLimit）
            report(StepEvent.Start(1, "下载客户端与依赖库"));
            var tasks = new List<DownloadTask>();
            var client = Json.Obj(Json.Obj(v["downloads"])["client"]);
            var clientTask = new DownloadTask(Json.Str(client, "url"), Path.Combine(vdir, id + ".jar"),
                Json.Str(client, "sha1"), Json.Long(client, "size"));
            clientTask.Mirror = BmclOf(clientTask.Url);   // 大文件：官方分块提速，BMCLAPI 兜底
            tasks.Add(clientTask);
            foreach (var o in Json.Arr(v["libraries"]))
            {
                var lib = Json.Obj(o);
                if (!RuleAllowed(lib)) continue;
                var t = LibToTask(lib);
                if (t != null && !string.IsNullOrEmpty(t.Url)) tasks.Add(t);
            }
            BigFirst(tasks);   // 大文件先拉，防止拖尾

            // 资产清单先拉（一个小 JSON），随后资产池在后台线程与库池并行开跑
            report(StepEvent.Start(2, "下载游戏资产"));
            var ai = Json.Obj(v["assetIndex"]);
            string aiPath = Path.Combine(Paths.AssetsDir, "indexes", Json.Str(ai, "id") + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(aiPath));
            File.WriteAllText(aiPath, GetJson(Json.Str(ai, "url")), new UTF8Encoding(false));
            var objects = Json.Obj(Json.Parse(File.ReadAllText(aiPath, Encoding.UTF8))["objects"]);
            var atasks = new List<DownloadTask>();
            foreach (var kv in objects)
            {
                var meta = Json.Obj(kv.Value);
                string hash = Json.Str(meta, "hash");
                string sub = hash.Substring(0, 2);
                var at = new DownloadTask(AssetBase + sub + "/" + hash,
                    Path.Combine(Paths.AssetsDir, "objects", sub, hash), null, Json.Long(meta, "size"));
                PreferMirror(at);   // 资产全是小文件：BMCLAPI 主源，官方兜底
                atasks.Add(at);
            }
            List<string> assetFails = null;
            var assetThread = new Thread(new ThreadStart(delegate
            {
                assetFails = new DownloadPool(atasks, delegate(long d, long t2, double mbps)
                {
                    report(StepEvent.Prog(2, d, t2, mbps));
                }).Run(48);
            }));
            assetThread.IsBackground = true;
            assetThread.Start();

            var fails = new DownloadPool(tasks, delegate(long d, long t, double mbps)
            {
                report(StepEvent.Prog(1, d, t, mbps));
            }).Run(24);
            assetThread.Join();
            if (fails.Count > 0) { report(StepEvent.Done(1, false, fails[0])); throw new Exception("有 " + fails.Count + " 个库文件下载失败: " + fails[0]); }
            report(StepEvent.Done(1, true, null));
            if (assetFails != null && assetFails.Count > 0) { report(StepEvent.Done(2, false, assetFails[0])); throw new Exception("有 " + assetFails.Count + " 个资产下载失败: " + assetFails[0]); }
            report(StepEvent.Done(2, true, null));

            // 运行时
            report(StepEvent.Start(3, "下载 Java 运行时"));
            DownloadRuntime(Json.Str(Json.Obj(v["javaVersion"]), "component"), delegate(long d, long t, double mbps)
            {
                report(StepEvent.Prog(3, d, t, mbps));
            });
            report(StepEvent.Done(3, true, null));
        }

        public static void DownloadRuntime(string component, Action<long, long, double> progress)
        {
            // 按组件分目录安装，跳过条件也按组件判（老版本要 gamma=17，新版本要更高，互不挤占）
            string targetDir = Paths.RuntimeDirFor(component);
            // 完整性判断（不是光看 javaw.exe）：半截目录会缺 lib/jvm.cfg，必须补下
            if (RuntimeHealthy(Path.Combine(targetDir, "bin", "javaw.exe"))) return;
            var all = Json.Parse(GetJson(RuntimeListUrl));
            var plat = Json.Obj(all["windows-x64"]);
            var entries = Json.Arr(plat[component]);
            if (entries == null || entries.Count == 0) entries = Json.Arr(plat["java-runtime-epsilon"]);
            var manifestUrl = Json.Str(Json.Obj(Json.Obj(entries[0])["manifest"]), "url");
            var manifest = Json.Parse(GetJson(manifestUrl));
            var files = Json.Obj(manifest["files"]);
            var rtasks = new List<DownloadTask>();
            foreach (var kv in files)
            {
                var info = Json.Obj(kv.Value);
                if (Json.Str(info, "type") != "file") continue;
                var raw = Json.Obj(Json.Obj(info["downloads"])["raw"]);
                if (raw == null) continue;
                var rt = new DownloadTask(Json.Str(raw, "url"),
                    Path.Combine(targetDir, kv.Key.Replace('/', Path.DirectorySeparatorChar)),
                    Json.Str(raw, "sha1"), Json.Long(raw, "size"));
                PreferMirror(rt);
                rtasks.Add(rt);
            }
            BigFirst(rtasks);
            var fails = new DownloadPool(rtasks, progress).Run(24);
            if (fails.Count > 0) throw new Exception("有 " + fails.Count + " 个运行时文件下载失败: " + fails[0]);
        }

        // ---------- 版本链解析 ----------

        public class ResolvedVersion
        {
            public string Id;                    // 启动用的版本 id（最末级）
            public string RootId;                // 原版 id（客户端 jar 所在）
            public string MainClass;
            public string AssetIndexId;
            public string JavaComponent;
            public List<DownloadTask> Libraries = new List<DownloadTask>();
            public List<string> JvmArgs = new List<string>();
            public List<string> GameArgs = new List<string>();
            public string LegacyGameArgs;        // minecraftArguments
        }

        // 库条目 → 下载任务。支持标准 downloads.artifact 格式和 Fabric/Forge 的 maven name+url 格式
        public static DownloadTask LibToTask(Dictionary<string, object> lib)
        {
            return LibToTask(lib, "https://libraries.minecraft.net/");
        }

        // defaultBase：maven name 格式缺 url 时的默认仓库（安装器预下载按其品牌仓库传入）
        public static DownloadTask LibToTask(Dictionary<string, object> lib, string defaultBase)
        {
            object dlObj;
            if (lib.TryGetValue("downloads", out dlObj) && dlObj != null)
            {
                object artObj;
                var dl = Json.Obj(dlObj);
                if (dl != null && dl.TryGetValue("artifact", out artObj) && artObj != null)
                {
                    var art = Json.Obj(artObj);
                    string rel = Json.Str(art, "path");
                    string url = Json.Str(art, "url");
                    if (string.IsNullOrEmpty(rel)) return null;
                    // url 为空 = 安装器本地生成的补丁 jar，无需下载但必须进 classpath
                    var task = new DownloadTask(url ?? "", Path.Combine(Paths.LibrariesDir, rel.Replace('/', Path.DirectorySeparatorChar)),
                        Json.Str(art, "sha1"), Json.Long(art, "size"));
                    PreferMirror(task);
                    return task;
                }
            }
            string name = Json.Str(lib, "name");
            if (name == null) return null;
            string mpath = MavenPath(name);
            if (mpath == null) return null;
            string baseUrl = Json.Str(lib, "url");
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = defaultBase;
            if (!baseUrl.EndsWith("/")) baseUrl += "/";
            var mt = new DownloadTask(baseUrl + mpath, Path.Combine(Paths.LibrariesDir, mpath.Replace('/', Path.DirectorySeparatorChar)), null, 0);
            PreferMirror(mt);
            return mt;
        }

        public static string MavenPath(string name)
        {
            return MavenPathExt(name, null);
        }

        // maven 坐标 → 仓库相对路径；ext 覆盖扩展名（如 @txt 的 mappings）
        public static string MavenPathExt(string name, string ext)
        {
            var parts = name.Split(':');
            if (parts.Length < 3) return null;
            string g = parts[0].Replace('.', '/');
            string file = parts[1] + "-" + parts[2] + (parts.Length > 3 ? "-" + parts[3] : "")
                + "." + (string.IsNullOrEmpty(ext) ? "jar" : ext);
            return g + "/" + parts[1] + "/" + parts[2] + "/" + file;
        }

        public static ResolvedVersion Resolve(string id)
        {
            var chain = new List<Dictionary<string, object>>();
            string cur = id;
            while (true)
            {
                string p = Path.Combine(Paths.VersionDir(cur), cur + ".json");
                if (!File.Exists(p)) throw new Exception("版本 " + cur + " 未安装");
                var j = Json.Parse(File.ReadAllText(p, Encoding.UTF8));
                chain.Insert(0, j);
                string parent = Json.Str(j, "inheritsFrom");
                if (string.IsNullOrEmpty(parent)) break;
                cur = parent;
            }

            var r = new ResolvedVersion { Id = id };
            var libTasks = new List<DownloadTask>();
            var seen = new HashSet<string>();
            bool modern = false;

            foreach (var j in chain)
            {
                string cid = Json.Str(j, "id");
                if (r.RootId == null) r.RootId = cid;
                string mc = Json.Str(j, "mainClass");
                if (mc != null) r.MainClass = mc;
                object tmpObj;
                var ai = j.TryGetValue("assetIndex", out tmpObj) ? Json.Obj(tmpObj) : null;
                if (ai != null && Json.Str(ai, "id") != null) r.AssetIndexId = Json.Str(ai, "id");
                var jv = j.TryGetValue("javaVersion", out tmpObj) ? Json.Obj(tmpObj) : null;
                if (jv != null && Json.Str(jv, "component") != null) r.JavaComponent = Json.Str(jv, "component");

                var libs = j.TryGetValue("libraries", out tmpObj) ? Json.Arr(tmpObj) : null;
                if (libs != null)
                foreach (var o in libs)
                {
                    var lib = Json.Obj(o);
                    if (!RuleAllowed(lib)) continue;
                    var t = LibToTask(lib);
                    if (t == null) continue;
                    if (seen.Add(t.Dest)) libTasks.Add(t);
                }

                var args = j.TryGetValue("arguments", out tmpObj) ? Json.Obj(tmpObj) : null;
                if (args != null)
                {
                    modern = true;
                    object ao;
                    if (args.TryGetValue("jvm", out ao)) CollectArgs(ao, r.JvmArgs);
                    if (args.TryGetValue("game", out ao)) CollectArgs(ao, r.GameArgs);
                }
                string legacy = Json.Str(j, "minecraftArguments");
                if (legacy != null) r.LegacyGameArgs = legacy;
            }

            if (!modern && r.LegacyGameArgs == null)
                r.LegacyGameArgs = "--username ${auth_player_name} --version ${version_name} --gameDir ${game_directory} --assetsDir ${assets_root} --assetIndex ${assets_index_name} --uuid ${auth_uuid} --accessToken ${auth_access_token} --userType ${user_type} --versionType ${version_type}";

            r.Libraries = libTasks;
            return r;
        }

        // 补全某版本缺失的库文件（安装加载器后调用）
        public static void EnsureLibraries(string id, Action<long, long, double> progress)
        {
            var rv = Resolve(id);
            var missing = new List<DownloadTask>();
            foreach (var t in rv.Libraries)
                if (!File.Exists(t.Dest))
                {
                    if (string.IsNullOrEmpty(t.Url)) continue; // 本地生成产物，无下载源
                    missing.Add(t);
                }
            if (missing.Count == 0) return;
            var fails = new DownloadPool(missing, progress).Run(16);
            if (fails.Count > 0) throw new Exception("有 " + fails.Count + " 个库文件下载失败: " + fails[0]);
        }

        static void CollectArgs(object arrObj, List<string> output)
        {
            var arr = Json.Arr(arrObj);
            if (arr == null) return;
            foreach (var o in arr)
            {
                string s = o as string;
                if (s != null) { output.Add(s); continue; }
                var d = Json.Obj(o);
                if (d == null) continue;
                if (!RuleAllowed(d)) continue;
                var val = d["value"];
                string vs = val as string;
                if (vs != null) output.Add(vs);
                else foreach (var x in Json.Arr(val)) output.Add(x.ToString());
            }
        }

        // ---------- 启动 ----------

        static string Sub(string s, Dictionary<string, string> map)
        {
            foreach (var kv in map) s = s.Replace(kv.Key, kv.Value);
            return s;
        }

        // GC 参数：delta 运行时（Java 21）上分代 ZGC，老运行时上社区调优 G1 套（flags.sh 风格，
        // 去掉 AlwaysPreTouch 免得拖慢启动）；GcBoost 关闭时只留裸 G1（与游戏默认一致）
        static List<string> GcArgs(Config cfg, ResolvedVersion rv)
        {
            var args = new List<string>();
            if (cfg == null || !cfg.GcBoost)
            {
                args.Add("-XX:+UseG1GC");
                return args;
            }
            bool java21 = rv.JavaComponent != null && rv.JavaComponent.IndexOf("delta") >= 0;
            if (java21)
            {
                args.Add("-XX:+UseZGC");
                args.Add("-XX:+ZGenerational");
                args.Add("-XX:+DisableExplicitGC");
                return args;
            }
            args.Add("-XX:+UnlockExperimentalVMOptions");
            args.Add("-XX:+UseG1GC");
            args.Add("-XX:+ParallelRefProcEnabled");
            args.Add("-XX:MaxGCPauseMillis=200");
            args.Add("-XX:+DisableExplicitGC");
            args.Add("-XX:G1NewSizePercent=30");
            args.Add("-XX:G1MaxNewSizePercent=40");
            args.Add("-XX:G1HeapRegionSize=8M");
            args.Add("-XX:G1ReservePercent=20");
            args.Add("-XX:G1HeapWastePercent=5");
            args.Add("-XX:G1MixedGCCountTarget=4");
            args.Add("-XX:InitiatingHeapOccupancyPercent=15");
            args.Add("-XX:G1MixedGCLiveThresholdPercent=90");
            args.Add("-XX:G1RSetUpdatingPauseTimePercent=5");
            args.Add("-XX:SurvivorRatio=32");
            args.Add("-XX:MaxTenuringThreshold=1");
            return args;
        }

        // 运行时完整性：光 javaw.exe 在不算好（半截下载会缺 lib/jvm.cfg，启动秒退）
        static bool RuntimeHealthy(string javawPath)
        {
            if (string.IsNullOrEmpty(javawPath) || !File.Exists(javawPath)) return false;
            string libCfg = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(javawPath)), "lib", "jvm.cfg");
            return File.Exists(libCfg);
        }

        // 按版本声明的 javaVersion 组件选运行时（1.20.1→gamma=Java17，新版本→更高），
        // 缺了现场自动补装；版本没声明才退回旧的单一 jre 目录。
        static string PickJavaw(ResolvedVersion rv, Action<string> log)
        {
            if (!string.IsNullOrEmpty(rv.JavaComponent))
            {
                string p = Paths.JavawExeFor(rv.JavaComponent);
                if (RuntimeHealthy(p)) return p;
                if (File.Exists(p) && log != null)
                    log("检测到 Java 组件 " + rv.JavaComponent + " 不完整（缺 lib/jvm.cfg），正在重新下载修复...");
                else if (log != null)
                    log("该版本需要 Java 运行时组件 " + rv.JavaComponent + "，正在自动下载（一次性）...");
                DownloadRuntime(rv.JavaComponent, delegate(long d, long t, double m) { });
                if (RuntimeHealthy(p)) return p;
                throw new Exception("Java 运行时组件 " + rv.JavaComponent + " 下载失败");
            }
            if (RuntimeHealthy(Paths.JavawExe)) return Paths.JavawExe;
            throw new Exception("Java 运行时未安装，请先下载版本");
        }

        // account = null 时退回纯离线（沿用历史硬编码 UUID）；外置账号则注入 authlib-injector
        public static Process Launch(string id, string username, Config cfg, Action<string> log, string instanceName = null, Account account = null)
        {
            var rv = Resolve(id);
            string javaw = PickJavaw(rv, log);
            bool ext = account != null && account.Type == "external";
            string playerName = account != null && account.Name.Length > 0 ? account.Name : username;
            string uuid = account != null && account.Uuid.Length > 0 ? account.Uuid : Account.LegacyUuid;
            string accessToken = ext && account.AccessToken.Length > 0 ? account.AccessToken : "0";

            // 实例目录
            string instDir = Paths.InstanceDir(instanceName ?? id);
            Directory.CreateDirectory(instDir);
            foreach (var sub in new string[] { "mods", "resourcepacks", "shaderpacks", "saves" })
                Directory.CreateDirectory(Path.Combine(instDir, sub));
            EnsureOptionsTxt(instDir, rv.RootId);
            ApplyWindowSize(instDir);   // 游戏窗口尺寸对齐启动器主窗（1040x600）

            // classpath
            var jars = new List<string>();
            foreach (var t in rv.Libraries) if (File.Exists(t.Dest)) jars.Add(Paths.Fwd(t.Dest));
            string rootJar = Path.Combine(Paths.VersionDir(rv.RootId), rv.RootId + ".jar");
            // Forge/NeoForge 1.17+（BootstrapLauncher）走 JPMS 模块层，客户端 jar 由 FML 以
            // "minecraft" 模块载入；若再把原版 jar 塞进 classpath，它会被当成自动模块
            // （按文件名如 _1._20._1），与 minecraft 模块重复导出同名包 →
            // ResolutionException 秒退。原版/Fabric 不走模块层，照旧要带客户端 jar。
            bool modLauncher = rv.MainClass != null && rv.MainClass.IndexOf("BootstrapLauncher") >= 0;
            if (File.Exists(rootJar) && !modLauncher) jars.Add(Paths.Fwd(rootJar));
            string classpath = string.Join(";", jars.ToArray());

            string nativesDir = Path.Combine(Paths.McDir, "natives");
            Directory.CreateDirectory(nativesDir);

            var map = new Dictionary<string, string>
            {
                {"${auth_player_name}", playerName},
                {"${auth_uuid}", uuid},
                {"${auth_access_token}", accessToken},
                {"${auth_xuid}", "0"},
                {"${clientid}", "0"},
                {"${user_type}", ext ? "mojang" : "legacy"},
                {"${user_properties}", "{}"},
                {"${version_name}", id},
                {"${game_directory}", Paths.Fwd(instDir)},
                {"${assets_root}", Paths.Fwd(Paths.AssetsDir)},
                {"${assets_index_name}", rv.AssetIndexId ?? ""},
                {"${version_type}", "HugeInsaneLauncher"},
                {"${natives_directory}", Paths.Fwd(nativesDir)},
                {"${launcher_name}", "HugeInsaneLauncher"},
                {"${launcher_version}", "3.0"},
                {"${library_directory}", Paths.Fwd(Paths.LibrariesDir)},
                {"${classpath_separator}", ";"},
                {"${classpath}", classpath},
            };

            var finalArgs = new List<string> { "-Xmx" + cfg.MemoryMb + "M" };
            finalArgs.AddRange(GcArgs(cfg, rv));
            finalArgs.Add("-Dminecraft.launcher.brand=HugeInsaneLauncher");
            // LAN 破墙：放行离线玩家加入本机开放的局域网世界。
            // patch 的是「本机作为服务端被连接」的验证路径（authlib hasJoinedServer），
            // 玩家进别人服务器的客户端侧流程（joinServer）完全不受影响；释放失败则照常启动。
            string lanAgent = LanAgent.Ensure();
            if (lanAgent != null) finalArgs.Add("-javaagent:" + Paths.Fwd(lanAgent));
            // 外置登录：authlib-injector 把认证地址拦到自定义 Yggdrasil 服务器（皮肤/联机档案都走它）
            if (ext)
            {
                AuthlibInjector.Ensure(log);
                finalArgs.Add("-javaagent:" + Paths.Fwd(AuthlibInjector.JarPath) + "=" + account.Server);
                finalArgs.Add("-Dauthlibinjector.side=client");
            }
            foreach (var a in rv.JvmArgs) finalArgs.Add(Sub(a, map));
            finalArgs.Add(rv.MainClass);
            if (rv.GameArgs.Count > 0)
                foreach (var a in rv.GameArgs) finalArgs.Add(Sub(a, map));
            else
                foreach (var a in rv.LegacyGameArgs.Split(' ')) finalArgs.Add(Sub(a, map));

            string argFile = Path.Combine(Paths.McDir, "launch.args");
            var sb = new StringBuilder();
            foreach (var a in finalArgs)
                sb.AppendLine(a.IndexOf(' ') >= 0 ? "\"" + a + "\"" : a);
            // @argfile 的读取编码 = JVM 平台默认编码：Java 18+（JEP 400，delta=21/epsilon=25）固定 UTF-8；
            // Java 17 及更早（gamma/alpha/beta/legacy）用系统 ANSI（中文 Windows 是 GBK）。
            // 写错编码会让中文路径（如整合包同名版本）变乱码 → joptsimple 报目录不存在秒退
            bool ansiArgFile = rv.JavaComponent != null &&
                rv.JavaComponent.IndexOf("delta") < 0 && rv.JavaComponent.IndexOf("epsilon") < 0;
            File.WriteAllText(argFile, sb.ToString(), ansiArgFile ? Encoding.Default : new UTF8Encoding(false));

            log("启动 " + id + " (玩家: " + playerName + ", 内存: " + cfg.MemoryMb + "M)");
            // 一次性提示：Defender 实时扫描是 Windows 上启动变慢的最大元凶之一
            if (!cfg.DefenderTipShown)
            {
                cfg.DefenderTipShown = true;
                log("小提示：把 .minecraft 目录加进 Windows 安全中心的「排除项」，启动和加载会明显更快（只需设置一次）");
            }
            var psi = new ProcessStartInfo
            {
                FileName = javaw,
                Arguments = "@\"" + argFile + "\"",
                WorkingDirectory = instDir,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = Process.Start(psi);

            try
            {
                switch (cfg.Priority)
                {
                    case "高": proc.PriorityClass = ProcessPriorityClass.High; break;
                    case "高于正常": proc.PriorityClass = ProcessPriorityClass.AboveNormal; break;
                }
            }
            catch { }

            cfg.PushUserHistory(playerName);
            cfg.Save();
            return proc;
        }

        // 首次启动预写中文配置。options.txt 的 version 字段是游戏的数据版本，
        // 不匹配会被游戏整文件重置，所以从客户端 jar 里的 version.json 读真实值
        static void EnsureOptionsTxt(string instDir, string rootId)
        {
            string opt = Path.Combine(instDir, "options.txt");
            if (File.Exists(opt)) return;
            string dataVersion = null;
            try
            {
                string jar = Path.Combine(Paths.VersionDir(rootId), rootId + ".jar");
                using (var zip = System.IO.Compression.ZipFile.OpenRead(jar))
                {
                    var entry = zip.GetEntry("version.json");
                    if (entry != null)
                        using (var s = new StreamReader(entry.Open(), Encoding.UTF8))
                        {
                            var vj = Json.Parse(s.ReadToEnd());
                            long dv = Json.Long(vj, "world_version");
                            if (dv > 0) dataVersion = dv.ToString();
                        }
                }
            }
            catch { }
            var lines = new List<string>
            {
                "lang:zh_cn",
                "fov:0.5",
                "guiScale:0",
                "maxFps:120",
                "fullscreen:false",
            };
            if (dataVersion != null) lines.Add("version:" + dataVersion);
            File.WriteAllLines(opt, lines.ToArray(), new UTF8Encoding(false));
        }

        // 性能优先画面预设：合并进已有 options.txt（version 等其余键原样保留；
        // 老版本认 graphicsMode，1.21.5+ 认 graphicsPreset，互不认识的键会被忽略，两套都写）
        public static void ApplyPerformanceOptions(string instDir, string rootId)
        {
            EnsureOptionsTxt(instDir, rootId);   // 不存在先按原逻辑补建（含正确 version 字段）
            string opt = Path.Combine(instDir, "options.txt");
            var perf = new Dictionary<string, string>
            {
                { "graphicsMode", "0" },
                { "graphicsPreset", "\"fast\"" },
                { "renderDistance", "8" },
                { "simulationDistance", "6" },
                { "particles", "2" },
                { "enableVsync", "false" },
                { "entityShadows", "false" },
                { "renderClouds", "\"false\"" },
                { "mipmapLevels", "2" },
                { "biomeBlendRadius", "0" },
                { "ao", "false" },
                { "entityDistanceScaling", "0.75" },
                { "maxFps", "260" },
            };
            var lines = File.Exists(opt) ? new List<string>(File.ReadAllLines(opt)) : new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c <= 0) continue;
                string key = lines[i].Substring(0, c);
                string nv;
                if (perf.TryGetValue(key, out nv)) { lines[i] = key + ":" + nv; perf.Remove(key); }
            }
            foreach (var kv in perf) lines.Add(kv.Key + ":" + kv.Value);
            File.WriteAllLines(opt, lines.ToArray(), new UTF8Encoding(false));
        }

        // 游戏窗口尺寸与启动器主窗一致（1040x600）：overrideWidth/Height 合并进 options.txt，
        // 其余键原样保留（合并手法同 ApplyPerformanceOptions）
        public static void ApplyWindowSize(string instDir)
        {
            string opt = Path.Combine(instDir, "options.txt");
            var size = new Dictionary<string, string>
            {
                { "overrideWidth", "1040" },
                { "overrideHeight", "600" },
            };
            var lines = File.Exists(opt) ? new List<string>(File.ReadAllLines(opt)) : new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c <= 0) continue;
                string key = lines[i].Substring(0, c);
                string nv;
                if (size.TryGetValue(key, out nv)) { lines[i] = key + ":" + nv; size.Remove(key); }
            }
            foreach (var kv in size) lines.Add(kv.Key + ":" + kv.Value);
            File.WriteAllLines(opt, lines.ToArray(), new UTF8Encoding(false));
        }
    }

    // 运行中游戏注册表：实例管理页「运行中游戏」标签的数据源。
    // Add/Remove 由启动/守护线程调用，Changed 在调用方线程触发（订阅方自备 Dispatcher 切回 UI）。
    static class RunningGames
    {
        public class Entry
        {
            public string InstanceName;   // 实例目录名（instances/<name>）
            public string VersionId;      // 实际启动的版本 id
            public string PlayerName;
            public Process Proc;
            public DateTime StartTime;
            public bool KilledByUser;   // 用户从实例管理页主动关闭：守护线程据此跳过崩溃判定

            public bool Exited
            {
                get { try { return Proc == null || Proc.HasExited; } catch { return true; } }
            }

            public string RunText
            {
                get
                {
                    var ts = DateTime.Now - StartTime;
                    if (ts.TotalSeconds < 60) return "刚刚启动";
                    if (ts.TotalHours < 1) return "已运行 " + (int)ts.TotalMinutes + " 分钟";
                    return "已运行 " + (int)ts.TotalHours + " 小时 " + ts.Minutes + " 分";
                }
            }
        }

        static List<Entry> entries = new List<Entry>();
        public static event Action Changed;

        public static List<Entry> All()
        {
            lock (entries)
            {
                entries.RemoveAll(delegate(Entry e) { return e.Exited; });
                return new List<Entry>(entries);
            }
        }

        public static int Count { get { return All().Count; } }

        public static void Add(Entry e)
        {
            if (e == null) return;
            lock (entries) { entries.Add(e); }
            Fire();
        }

        public static void Remove(Entry e)
        {
            if (e == null) return;
            bool found;
            lock (entries) { found = entries.Remove(e); }
            if (found) Fire();
        }

        public static void Kill(Entry e)
        {
            try
            {
                if (e != null && e.Proc != null && !e.Proc.HasExited)
                {
                    e.KilledByUser = true;   // 先打标再杀：WatchGame 见到标记按正常退出处理
                    e.Proc.Kill();
                }
            }
            catch { }
            // 进程退出的收尾由守护线程（WatchGame）负责 → Remove + 崩溃判定
        }

        static void Fire()
        {
            var h = Changed;
            if (h != null) h();
        }
    }
}
