// HugeLauncher - Modrinth 下载源：搜索/版本匹配/安装/整合包
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HugeLauncher
{
    class HglException : Exception
    {
        public HglException(string msg) : base(msg) { }
    }

    static class Modrinth
    {
        public const string OfficialApiBase = "https://api.modrinth.com/v2";

        // 反代基址（空/null=官方直连）。由 Config.ModrinthMirror 在启动时注入。
        // 约定：{mirror}/v2/* → api.modrinth.com/v2/*，{mirror}/cdn/* → cdn.modrinth.com/*
        public static string MirrorOverride = null;

        public static string ApiBase
        {
            get
            {
                if (string.IsNullOrEmpty(MirrorOverride)) return OfficialApiBase;
                return MirrorOverride.TrimEnd('/') + "/v2";
            }
        }

        // 文件下载 URL 走反代（cdn.modrinth.com → {mirror}/cdn/）；未配置镜像原样返回
        public static string MirrorFileUrl(string url)
        {
            if (string.IsNullOrEmpty(MirrorOverride) || string.IsNullOrEmpty(url)) return url;
            const string cdn = "https://cdn.modrinth.com/";
            if (url.StartsWith(cdn)) return MirrorOverride.TrimEnd('/') + "/cdn/" + url.Substring(cdn.Length);
            return url;
        }

        public class SearchHit
        {
            public string ProjectId, Slug, Title, Author, Description, IconUrl;
            public long Downloads;
        }

        public class MrFile { public string Url, Filename, Sha1; public long Size; }

        // project_type: mod / resourcepack / shader / modpack
        public static List<SearchHit> Search(string projectType, string query, string gameVersion)
        {
            string facets = "[[\"project_type:" + projectType + "\"]";
            if (!string.IsNullOrEmpty(gameVersion)) facets += ",[\"versions:" + gameVersion + "\"]";
            facets += "]";
            string url = ApiBase + "/search?limit=30&query=" + Uri.EscapeDataString(query ?? "")
                + "&facets=" + Uri.EscapeDataString(facets);
            var resp = Json.Parse(Net.GetString(url));
            var list = new List<SearchHit>();
            foreach (var o in Json.Arr(resp["hits"]))
            {
                var h = Json.Obj(o);
                list.Add(new SearchHit
                {
                    ProjectId = Json.Str(h, "project_id"),
                    Slug = Json.Str(h, "slug"),
                    Title = Json.Str(h, "title"),
                    Author = Json.Str(h, "author"),
                    Description = Json.Str(h, "description"),
                    IconUrl = Json.Str(h, "icon_url"),
                    Downloads = Json.Long(h, "downloads"),
                });
            }
            return list;
        }

        // 拉取项目全部版本
        static System.Collections.ArrayList GetProjectVersions(string projectId)
        {
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            return ser.Deserialize<System.Collections.ArrayList>(Net.GetString(ApiBase + "/project/" + projectId + "/version"));
        }

        static List<string> StrList(object arrObj)
        {
            var l = new List<string>();
            var arr = Json.Arr(arrObj);
            if (arr != null) foreach (var o in arr) l.Add(o.ToString());
            return l;
        }

        // 版本匹配：精确（游戏版本+加载器）> 相邻（仅游戏版本不同）。无可用返回 null
        public static Dictionary<string, object> MatchVersion(string projectId, string gameVersion, string loader, out bool adjacent)
        {
            adjacent = false;
            Dictionary<string, object> best = null, fallback = null;
            DateTime bestDate = DateTime.MinValue, fbDate = DateTime.MinValue;
            foreach (var o in GetProjectVersions(projectId))
            {
                var v = Json.Obj(o);
                var gvs = StrList(v["game_versions"]);
                var lds = StrList(v["loaders"]);
                DateTime dt;
                if (!DateTime.TryParse(Json.Str(v, "date_published"), out dt)) dt = DateTime.MinValue;
                bool gameOk = string.IsNullOrEmpty(gameVersion) || gvs.Contains(gameVersion);
                bool loaderOk = string.IsNullOrEmpty(loader) || loader == "vanilla" || lds.Count == 0 || lds.Contains(loader);
                if (gameOk && loaderOk)
                {
                    if (dt > bestDate) { bestDate = dt; best = v; }
                }
                else if (loaderOk && dt > fbDate) { fbDate = dt; fallback = v; }
            }
            if (best != null) return best;
            adjacent = true;
            return fallback;
        }

        // ---------- 版本列表（详情页分组用） ----------

        public class MrVersion
        {
            public string Id, ProjectId, Name, VersionNumber, VersionType; // VersionType: release / beta / alpha
            public List<string> GameVersions = new List<string>();
            public List<string> Loaders = new List<string>();
            public List<string> RequiredDeps = new List<string>(); // 前置依赖的 project_id
            public long Downloads;
            public string DatePublished;
            public MrFile File; // 主文件
        }

        // 拉取项目全部文件版本，结构化返回（新的在前）
        public static List<MrVersion> GetVersionList(string projectId)
        {
            var list = new List<MrVersion>();
            foreach (var o in GetProjectVersions(projectId))
            {
                var v = Json.Obj(o);
                var mv = new MrVersion();
                mv.Id = Json.Str(v, "id");
                mv.ProjectId = Json.Str(v, "project_id");
                mv.Name = Json.Str(v, "name");
                mv.VersionNumber = Json.Str(v, "version_number");
                mv.VersionType = Json.Str(v, "version_type");
                mv.GameVersions = StrList(v["game_versions"]);
                mv.Loaders = StrList(v["loaders"]);
                mv.Downloads = Json.Long(v, "downloads");
                mv.DatePublished = Json.Str(v, "date_published");
                var deps = Json.Arr(v["dependencies"]);
                if (deps != null)
                    foreach (var d in deps)
                    {
                        var dd = Json.Obj(d);
                        if (Json.Str(dd, "dependency_type") != "required") continue;
                        string pid = Json.Str(dd, "project_id");
                        if (!string.IsNullOrEmpty(pid)) mv.RequiredDeps.Add(pid);
                    }
                mv.File = PrimaryFile(v);
                list.Add(mv);
            }
            list.Sort(delegate(MrVersion a, MrVersion b)
            {
                return string.CompareOrdinal(b.DatePublished ?? "", a.DatePublished ?? "");
            });
            return list;
        }

        // 批量取工程信息（前置依赖卡片用；项目接口无 author 字段）
        public static List<SearchHit> GetProjects(List<string> ids)
        {
            var list = new List<SearchHit>();
            if (ids == null || ids.Count == 0) return list;
            var sb = new StringBuilder("[");
            for (int i = 0; i < ids.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"').Append(ids[i]).Append('"');
            }
            sb.Append(']');
            var ser = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var arr = ser.Deserialize<System.Collections.ArrayList>(
                Net.GetString(ApiBase + "/projects?ids=" + Uri.EscapeDataString(sb.ToString())));
            foreach (var o in arr)
            {
                var h = Json.Obj(o);
                list.Add(new SearchHit
                {
                    ProjectId = Json.Str(h, "id"),
                    Slug = Json.Str(h, "slug"),
                    Title = Json.Str(h, "title"),
                    Description = Json.Str(h, "description"),
                    IconUrl = Json.Str(h, "icon_url"),
                    Downloads = Json.Long(h, "downloads"),
                });
            }
            return list;
        }

        static MrFile PrimaryFile(Dictionary<string, object> version)
        {
            MrFile first = null;
            foreach (var o in Json.Arr(version["files"]))
            {
                var f = Json.Obj(o);
                var mf = new MrFile
                {
                    Url = Json.Str(f, "url"),
                    Filename = Json.Str(f, "filename"),
                    Size = Json.Long(f, "size"),
                    Sha1 = Json.Str(Json.Obj(f["hashes"]), "sha1"),
                };
                if (first == null) first = mf;
                if (Json.Bool(f, "primary", false)) return mf;
            }
            return first;
        }

        // 从版本 id 推断加载器（neoforge 必须先于 forge 判断）
        public static string DetectLoader(string versionId)
        {
            // 先按名字匹配；匹配不到（如整合包实例名）沿 inheritsFrom 链逐跳看祖先 id
            string cur = versionId;
            for (int hop = 0; hop < 8 && !string.IsNullOrEmpty(cur); hop++)
            {
                string s = cur.ToLowerInvariant();
                if (s.Contains("neoforge")) return "neoforge";
                if (s.Contains("fabric")) return "fabric";
                if (s.Contains("forge")) return "forge";
                string parent = null;
                try
                {
                    string p = Path.Combine(Paths.VersionDir(cur), cur + ".json");
                    if (File.Exists(p))
                        parent = Json.Str(Json.Parse(File.ReadAllText(p, Encoding.UTF8)), "inheritsFrom");
                }
                catch { }
                if (string.IsNullOrEmpty(parent) || parent == cur) break;
                cur = parent;
            }
            return "vanilla";
        }

        static string SubDirForType(string projectType)
        {
            switch (projectType)
            {
                case "mod": return "mods";
                case "resourcepack": return "resourcepacks";
                case "shader": return "shaderpacks";
                default: return "mods";
            }
        }

        // 依赖补装失败汇总：顶层安装开始时清空，失败逐个记入；UI 安装完读取并明示，不再静默跳过
        public static readonly List<string> FailedDeps = new List<string>();
        static object DepSync = new object();

        // 安装 mod/资源包/光影。实例名 = 版本 id（modpack 单独处理）。
        // 返回安装的版本名称；无匹配抛 HglException("HIL 找不到可用的游戏版本")
        public static string InstallResource(string projectType, string projectId, string gameVersion, string loader,
            string instanceName, Action<string> log)
        {
            lock (DepSync) FailedDeps.Clear();
            return InstallResourceInner(projectType, projectId, gameVersion, loader, instanceName, log);
        }

        static string InstallResourceInner(string projectType, string projectId, string gameVersion, string loader,
            string instanceName, Action<string> log)
        {
            bool adjacent;
            var v = MatchVersion(projectId, gameVersion, loader, out adjacent);
            if (v == null) throw new HglException("HIL 找不到可用的游戏版本");
            if (adjacent)
                log("警告：没有完全适配的版本，将安装最近版本 " + Json.Str(v, "name"));

            var mf = PrimaryFile(v);
            if (mf == null) throw new Exception("该版本没有可下载的文件");
            string dest = Path.Combine(Paths.InstanceDir(instanceName), SubDirForType(projectType), mf.Filename);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            log("下载 " + mf.Filename + " ...");
            if (Net.DownloadFile(new DownloadTask(MirrorFileUrl(mf.Url), dest, mf.Sha1, mf.Size), null) < 0)
                throw new Exception("下载失败: " + mf.Filename);

            // 依赖（仅 mod）
            if (projectType == "mod")
                InstallDependencies(v, gameVersion, loader, instanceName, log, 0);
            return Json.Str(v, "name");
        }

        static void InstallDependencies(Dictionary<string, object> version, string gameVersion, string loader,
            string instanceName, Action<string> log, int depth)
        {
            if (depth >= 3) return;
            var deps = Json.Arr(version["dependencies"]);
            if (deps == null) return;
            foreach (var o in deps)
            {
                var d = Json.Obj(o);
                if (Json.Str(d, "dependency_type") != "required") continue;
                string depProject = Json.Str(d, "project_id");
                if (string.IsNullOrEmpty(depProject)) continue;
                try
                {
                    log("安装依赖: " + depProject);
                    InstallResourceInner("mod", depProject, gameVersion, loader, instanceName, log);
                }
                catch (Exception ex)
                {
                    lock (DepSync) if (!FailedDeps.Contains(depProject)) FailedDeps.Add(depProject);
                    log("依赖安装失败（跳过）: " + ex.Message);
                }
            }
        }

        // 按指定文件版本安装（详情页版本行点击）。gameVersion/loader 仅用于依赖补装匹配。
        // 返回安装名；文件未标注适配目标实例时在日志里警告但不阻止。
        public static string InstallResourceFile(string projectType, MrVersion v, string gameVersion, string loader,
            string instanceName, Action<string> log)
        {
            if (v == null || v.File == null) throw new Exception("该版本没有可下载的文件");
            lock (DepSync) FailedDeps.Clear();
            if (projectType == "mod")
            {
                if (!string.IsNullOrEmpty(gameVersion) && v.GameVersions.Count > 0 && !v.GameVersions.Contains(gameVersion))
                    log("警告：该文件未标注适配游戏版本 " + gameVersion);
                if (!string.IsNullOrEmpty(loader) && loader != "vanilla" && v.Loaders.Count > 0 && !v.Loaders.Contains(loader))
                    log("警告：该文件未标注适配加载器 " + loader);
            }
            var mf = v.File;
            string dest = Path.Combine(Paths.InstanceDir(instanceName), SubDirForType(projectType), mf.Filename);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            log("下载 " + mf.Filename + " ...");
            if (Net.DownloadFile(new DownloadTask(MirrorFileUrl(mf.Url), dest, mf.Sha1, mf.Size), null) < 0)
                throw new Exception("下载失败: " + mf.Filename);

            // 依赖自动补装（仅 mod，沿用递归匹配逻辑）
            if (projectType == "mod" && v.RequiredDeps.Count > 0)
                foreach (var dep in v.RequiredDeps)
                {
                    try
                    {
                        log("安装依赖: " + dep);
                        InstallResourceInner("mod", dep, gameVersion, loader, instanceName, log);
                    }
                    catch (Exception ex)
                    {
                        lock (DepSync) if (!FailedDeps.Contains(dep)) FailedDeps.Add(dep);
                        log("依赖安装失败（跳过）: " + ex.Message);
                    }
                }
            return v.Name ?? v.VersionNumber ?? mf.Filename;
        }

        // ---------- 一键完整下载（摊平：主项+前置各占一个下载任务条目） ----------

        // 顶层安装开始前清空前置失败汇总（ResolveInstallPlan/DownloadResolvedFile 路径用）
        public static void BeginInstall()
        {
            lock (DepSync) FailedDeps.Clear();
        }

        // 记录一个前置失败（解析/下载阶段都可能调）
        public static void NoteFailedDep(string projectId)
        {
            if (string.IsNullOrEmpty(projectId)) return;
            lock (DepSync) if (!FailedDeps.Contains(projectId)) FailedDeps.Add(projectId);
        }

        // 从 MrVersion 列表挑最佳：精确（游戏版本+加载器）优先，否则仅加载器匹配的兜底。
        // GetVersionList 已按发布日期降序，第一个命中的就是最新。
        static MrVersion MatchMr(List<MrVersion> vers, string gv, string ld)
        {
            MrVersion fallback = null;
            foreach (var v in vers)
            {
                bool gameOk = string.IsNullOrEmpty(gv) || v.GameVersions.Contains(gv);
                bool loaderOk = string.IsNullOrEmpty(ld) || ld == "vanilla" || v.Loaders.Count == 0 || v.Loaders.Contains(ld);
                if (gameOk && loaderOk) return v;
                if (loaderOk && fallback == null) fallback = v;
            }
            return fallback;
        }

        // 摊平安装计划：返回列表 [0]=主项，[1..]=全部前置（BFS 去重，依赖深度≤3）。
        // 选不出适配版本的前置记 FailedDeps 并跳过，不阻断主项。非 mod 直接只含主项。
        public static List<MrVersion> ResolveInstallPlan(string projectType, MrVersion main, string gameVersion, string loader)
        {
            var plan = new List<MrVersion>();
            if (main == null) return plan;
            plan.Add(main);
            if (projectType != "mod") return plan;

            var seen = new HashSet<string>();
            var depth = new Dictionary<string, int>();
            var queue = new List<string>();
            foreach (var d0 in main.RequiredDeps)
                if (!depth.ContainsKey(d0)) { depth[d0] = 1; queue.Add(d0); }

            for (int qi = 0; qi < queue.Count; qi++)
            {
                string dep = queue[qi];
                if (string.IsNullOrEmpty(dep) || seen.Contains(dep)) continue;
                seen.Add(dep);
                if (seen.Count > 24) break;   // 防御：依赖图爆炸上限
                try
                {
                    var pick = MatchMr(GetVersionList(dep), gameVersion, loader);
                    if (pick == null || pick.File == null) { NoteFailedDep(dep); continue; }
                    if (string.IsNullOrEmpty(pick.ProjectId)) pick.ProjectId = dep;
                    plan.Add(pick);
                    int nd = depth[dep] + 1;
                    if (nd <= 3)
                        foreach (var sub in pick.RequiredDeps)
                            if (!depth.ContainsKey(sub)) { depth[sub] = nd; queue.Add(sub); }
                }
                catch (Exception)
                {
                    NoteFailedDep(dep);   // 网络/解析异常：跳过该前置，主项不受影响
                }
            }
            return plan;
        }

        // 下载一个已解析好的文件版本到目标实例（不递归装前置——前置已由计划摊平各自成任务）。
        public static void DownloadResolvedFile(string projectType, MrVersion v, string instanceName)
        {
            if (v == null || v.File == null) throw new Exception("该版本没有可下载的文件");
            var mf = v.File;
            string dest = Path.Combine(Paths.InstanceDir(instanceName), SubDirForType(projectType), mf.Filename);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (Net.DownloadFile(new DownloadTask(MirrorFileUrl(mf.Url), dest, mf.Sha1, mf.Size), null) < 0)
                throw new Exception("下载失败: " + mf.Filename);
        }

        // ---------- 整合包 ----------

        // 安装整合包，创建新实例。返回实例名。
        public static string InstallModpack(string projectId, string packName, Action<StepEvent> report)
        {
            report(StepEvent.Start(0, "解析整合包信息"));
            bool adjacent;
            var v = MatchVersion(projectId, null, null, out adjacent);
            if (v == null) throw new HglException("HIL 找不到可用的游戏版本");
            var mf = PrimaryFile(v);
            if (mf == null) throw new Exception("整合包没有可下载的文件");
            report(StepEvent.Done(0, true, null));
            return InstallModpackCore(mf, packName, report);
        }

        // 按指定文件安装整合包（详情页版本行点击）。返回实例名。
        public static string InstallModpackFile(MrVersion v, string packName, Action<StepEvent> report)
        {
            if (v == null || v.File == null) throw new Exception("整合包没有可下载的文件");
            return InstallModpackCore(v.File, packName, report);
        }

        static string InstallModpackCore(MrFile mf, string packName, Action<StepEvent> report)
        {
            report(StepEvent.Start(1, "下载整合包 " + mf.Filename));
            string tmp = Path.Combine(Paths.McDir, "temp");
            Directory.CreateDirectory(tmp);
            string packFile = Path.Combine(tmp, mf.Filename);
            if (Net.DownloadFile(new DownloadTask(MirrorFileUrl(mf.Url), packFile, mf.Sha1, mf.Size), null) < 0)
                throw new Exception("整合包下载失败");
            report(StepEvent.Done(1, true, null));

            report(StepEvent.Start(2, "解析整合包清单"));
            Dictionary<string, object> index = null;
            using (var zip = ZipFile.OpenRead(packFile))
            {
                var entry = zip.GetEntry("modrinth.index.json");
                if (entry == null) throw new Exception("不是有效的 Modrinth 整合包");
                using (var s = new StreamReader(entry.Open(), Encoding.UTF8))
                    index = Json.Parse(s.ReadToEnd());
            }
            string safeName = string.IsNullOrEmpty(packName) ? Json.Str(index, "name") : packName;
            foreach (var c in Path.GetInvalidFileNameChars()) safeName = safeName.Replace(c, '_');
            // 重名沿用 # 副本约定；旧式实例目录（无版本 json）不算占用，直接复用合并
            string finalName = GameCore.NextAvailableId(safeName);
            report(StepEvent.Done(2, true, null));

            // 依赖的基础环境：缺什么自动装什么（原版 + 加载器），装完拿到可启动的基础版本 id
            object depsObj;
            var deps = index.TryGetValue("dependencies", out depsObj) ? Json.Obj(depsObj) : null;
            string mcVer = Json.Str(deps, "minecraft");
            if (mcVer == null) throw new Exception("整合包清单没有声明游戏版本");
            if (Json.Str(deps, "quilt-loader") != null)
                throw new HglException("该整合包使用 Quilt 加载器，HIL 暂不支持");
            report(StepEvent.Start(3, "准备基础环境（MC " + mcVer + "）"));
            string baseId = EnsureBaseEnv(mcVer,
                Json.Str(deps, "fabric-loader"), Json.Str(deps, "forge"), Json.Str(deps, "neoforge"), report);
            report(StepEvent.Done(3, true, null));

            report(StepEvent.Start(4, "下载整合包内容"));
            string instDir = Paths.InstanceDir(finalName);
            Directory.CreateDirectory(instDir);
            var files = Json.Arr(index["files"]);
            var tasks = new List<DownloadTask>();
            foreach (var o in files)
            {
                var f = Json.Obj(o);
                var dls = Json.Arr(f["downloads"]);
                if (dls == null || dls.Count == 0) continue;
                string rel = Json.Str(f, "path").Replace('/', Path.DirectorySeparatorChar);
                tasks.Add(new DownloadTask(MirrorFileUrl(dls[0].ToString()), Path.Combine(instDir, rel),
                    Json.Str(Json.Obj(f["hashes"]), "sha1"), Json.Long(f, "fileSize")));
            }
            var fails = new DownloadPool(tasks, delegate(long d, long t, double m) { report(StepEvent.Prog(4, d, t, m)); }).Run(16);
            if (fails.Count > 0) { report(StepEvent.Done(4, false, fails[0])); throw new Exception("有 " + fails.Count + " 个文件下载失败"); }
            report(StepEvent.Done(4, true, null));

            report(StepEvent.Start(5, "应用覆盖文件"));
            using (var zip = ZipFile.OpenRead(packFile))
            {
                foreach (var entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith("overrides/")) continue;
                    string rel = entry.FullName.Substring("overrides/".Length);
                    if (rel.Length == 0) continue;
                    string dest = Path.Combine(instDir, rel.Replace('/', Path.DirectorySeparatorChar));
                    if (entry.FullName.EndsWith("/")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, true);
                }
            }
            try { File.Delete(packFile); } catch { }
            report(StepEvent.Done(5, true, null));

            // 注册为可启动版本：versions/<包名>/<包名>.json 以 inheritsFrom 继承基础版本，
            // 版本列表/版本选择/版本设置即刻认它；游戏目录 = instances/<包名>（Launch 默认行为）
            report(StepEvent.Start(6, "注册版本 " + finalName));
            var vj = new Dictionary<string, object>();
            vj["id"] = finalName;
            vj["inheritsFrom"] = baseId;
            vj["type"] = "release";
            string vdir = Paths.VersionDir(finalName);
            Directory.CreateDirectory(vdir);
            File.WriteAllText(Path.Combine(vdir, finalName + ".json"),
                new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(vj),
                new UTF8Encoding(false));
            // 兜底校验依赖库（基础环境是复用旧装时尤其重要；已完整则秒过）
            GameCore.EnsureLibraries(finalName, delegate(long d, long t2, double m) { report(StepEvent.Prog(6, d, t2, m)); });
            report(StepEvent.Done(6, true, null));
            report(StepEvent.LogLine("整合包安装完成: " + finalName + "（基于 " + baseId + "，已加入版本列表，可直接启动）"));
            return finalName;
        }

        // 解析整合包依赖的基础版本：已装直接用；缺则自动安装（原版 + 加载器），返回可启动的基础版本 id
        static string EnsureBaseEnv(string mcVer, string fabricV, string forgeV, string neoV, Action<StepEvent> report)
        {
            // 候选 id 覆盖新旧命名格式（fabric 旧格式 fabric-loader-<lv>-<mc>；forge 老安装器 forge-<fv>）
            string[] candidates;
            if (fabricV != null) candidates = new[] { mcVer + "-fabric-" + fabricV, "fabric-loader-" + fabricV + "-" + mcVer };
            else if (forgeV != null) candidates = new[] { mcVer + "-forge-" + forgeV, "forge-" + mcVer + "-" + forgeV, "forge-" + forgeV };
            else if (neoV != null) candidates = new[] { mcVer + "-neoforge-" + neoV, "neoforge-" + neoV };
            else candidates = new[] { mcVer };
            foreach (var c in candidates)
                if (GameCore.IsVersionReady(c)) { report(StepEvent.LogLine("基础环境已就绪: " + c)); return c; }

            GameCore.VersionEntry ve = null;
            foreach (var e in GameCore.GetVersionList())
                if (e.Id == mcVer) { ve = e; break; }
            if (ve == null) throw new HglException("HIL 找不到可用的游戏版本 " + mcVer);

            // 子安装的步骤事件摊平成日志行，进度挂到第 3 步，保持整合包任务的步骤列表清爽
            Action<StepEvent> sub = delegate(StepEvent e)
            {
                if (e.EventKind == StepEvent.Kind.Log) report(StepEvent.LogLine(e.Message));
                else if (e.EventKind == StepEvent.Kind.StepStart) report(StepEvent.LogLine("→ " + e.StepName));
                else if (e.EventKind == StepEvent.Kind.StepProgress) report(StepEvent.Prog(3, e.Current, e.Total, e.MbPerSec));
            };
            if (fabricV != null)
            {
                GameCore.EnsureVanilla(ve, sub);
                return Loaders.InstallFabric(mcVer, fabricV, sub);
            }
            if (forgeV != null)
            {
                GameCore.EnsureVanilla(ve, sub);
                // Modrinth 清单的 forge 依赖不带 MC 前缀（如 47.2.0），maven 坐标需要 <mc>-<fv>
                return Loaders.InstallForge(mcVer, forgeV.StartsWith(mcVer + "-") ? forgeV : mcVer + "-" + forgeV, sub);
            }
            if (neoV != null)
            {
                GameCore.EnsureVanilla(ve, sub);
                return Loaders.InstallNeoForge(mcVer, neoV, sub);
            }
            GameCore.EnsureVanilla(ve, sub);
            return mcVer;
        }

        // 读取实例的启动版本（整合包实例）
        public static string GetInstanceLaunchId(string instanceName)
        {
            try
            {
                string p = Path.Combine(Paths.InstanceDir(instanceName), "hil-instance.txt");
                if (File.Exists(p)) return File.ReadAllText(p, Encoding.UTF8).Trim();
            }
            catch { }
            return null;
        }
    }
}
