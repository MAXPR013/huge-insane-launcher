// HugeLauncher - 目录识别与迁移：扫描多启动器的 .minecraft，按内容探测版本，支持整包搬进 HIL
// 原则：识别只读不写；迁移默认复制（原文件不动），重名自动加后缀。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HugeLauncher
{
    class ScannedVersion
    {
        public string Root = "";          // 所属根目录
        public string Source = "";        // 来源标签（HIL / 官方启动器 / PCL / HMCL / Prism …）
        public string Id = "";            // 版本 id（优先 json 里的 id，其次文件夹名）
        public string Dir = "";           // versions/<folder>
        public string JsonPath;
        public string JarPath;
        public string BaseId = "";        // inheritsFrom
        public string Loader = "";        // forge / neoforge / fabric / quilt / 空
        public bool HasJson;
        public bool HasJar;
        public bool Broken;          // 解析失败
        public string Note = "";

        public string Display
        {
            get
            {
                string s = Id;
                if (!string.IsNullOrEmpty(Loader)) s += "  · " + Loader;
                return s;
            }
        }
    }

    static class LauncherScan
    {
        // 候选根目录：已知启动器的常见布局（不扫全盘，用户可手动指定）
        public static List<string[]> CandidateRoots()
        {
            var list = new List<string[]>();
            Add(list, Paths.McDir, "HIL");
            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Add(list, Path.Combine(roaming, ".minecraft"), "官方启动器");
            Add(list, Path.Combine(local, ".minecraft"), "官方启动器");
            Add(list, Path.Combine(roaming, "PrismLauncher"), "Prism");
            Add(list, Path.Combine(roaming, "MultiMC"), "MultiMC");
            Add(list, Path.Combine(roaming, "com.modrinth.theseus"), "Modrinth");
            // 常见盘符一层（只到 <盘>:\ 和 <盘>:\Games 这种浅层）
            string[] drives = { "C:", "D:", "E:", "F:" };
            for (int i = 0; i < drives.Length; i++)
            {
                Add(list, drives[i] + "\\.minecraft", "盘符");
                Add(list, drives[i] + "\\Games\\.minecraft", "盘符");
                Add(list, drives[i] + "\\MC\\.minecraft", "盘符");
                Add(list, drives[i] + "\\Minecraft\\.minecraft", "盘符");
            }
            return list;
        }

        static void Add(List<string[]> list, string path, string source)
        {
            if (string.IsNullOrEmpty(path)) return;
            try { if (!Directory.Exists(path)) return; } catch { return; }
            for (int i = 0; i < list.Count; i++)
                if (string.Equals(list[i][0], path, StringComparison.OrdinalIgnoreCase)) return;
            list.Add(new string[] { path, source });
        }

        // 扫描一个根目录：普通 versions/ + HMCL 版本隔离（versions/<v>/.minecraft/versions）
        public static List<ScannedVersion> ScanRoot(string root, string source)
        {
            var result = new List<ScannedVersion>();
            ScanVersionsDir(Path.Combine(root, "versions"), root, source, result);
            // Prism/MultiMC：instances/<名字>/.minecraft/versions
            try
            {
                string inst = Path.Combine(root, "instances");
                if (Directory.Exists(inst))
                {
                    foreach (string d in Directory.GetDirectories(inst))
                    {
                        string mc = Path.Combine(d, ".minecraft");
                        if (Directory.Exists(mc)) ScanVersionsDir(Path.Combine(mc, "versions"), mc, source, result);
                    }
                }
            }
            catch { }
            return result;
        }

        static void ScanVersionsDir(string versionsDir, string root, string source, List<ScannedVersion> outList)
        {
            try
            {
                if (!Directory.Exists(versionsDir)) return;
                foreach (string dir in Directory.GetDirectories(versionsDir))
                {
                    // HMCL 版本隔离：versions/<v>/.minecraft
                    string nested = Path.Combine(dir, ".minecraft");
                    if (Directory.Exists(nested))
                    {
                        ScanVersionsDir(Path.Combine(nested, "versions"), nested, source, outList);
                        continue;
                    }
                    outList.Add(Describe(dir, root, source));
                }
            }
            catch { }
        }

        static ScannedVersion Describe(string dir, string root, string source)
        {
            var v = new ScannedVersion();
            v.Root = root;
            v.Source = source;
            v.Dir = dir;
            string folder = Path.GetFileName(dir);
            v.Id = folder;

            // 1) 任意 *.json
            try
            {
                string[] jsons = Directory.GetFiles(dir, "*.json");
                string best = null;
                for (int i = 0; i < jsons.Length; i++)
                {
                    string fn = Path.GetFileNameWithoutExtension(jsons[i]);
                    if (string.Equals(fn, folder, StringComparison.OrdinalIgnoreCase)) { best = jsons[i]; break; }
                    if (best == null) best = jsons[i];
                }
                if (best != null)
                {
                    v.JsonPath = best;
                    v.HasJson = true;
                    var d = Json.Parse(File.ReadAllText(best));
                    string id = Json.Str(d, "id");
                    if (!string.IsNullOrEmpty(id)) v.Id = id;
                    v.BaseId = Json.Str(d, "inheritsFrom");
                    string type = Json.Str(d, "type");
                    if (!string.IsNullOrEmpty(type)) v.Note = type;
                }
            }
            catch { v.Broken = true; }

            // 2) client jar：<folder>.jar / <id>.jar / 任意 .jar
            string[] jars = new string[0];
            try { jars = Directory.GetFiles(dir, "*.jar"); } catch { }
            for (int i = 0; i < jars.Length; i++)
            {
                string fn = Path.GetFileNameWithoutExtension(jars[i]);
                if (string.Equals(fn, folder, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(fn, v.Id, StringComparison.OrdinalIgnoreCase))
                {
                    v.JarPath = jars[i];
                    break;
                }
            }
            if (v.JarPath == null && jars.Length > 0) v.JarPath = jars[0];
            v.HasJar = v.JarPath != null;
            if (!v.HasJar) v.Note = "缺少 client.jar";

            // 3) 加载器识别
            string lower = (v.Id + " " + folder).ToLowerInvariant();
            if (lower.Contains("neoforge")) v.Loader = "NeoForge";
            else if (lower.Contains("forge")) v.Loader = "Forge";
            else if (lower.Contains("quilt")) v.Loader = "Quilt";
            else if (lower.Contains("fabric")) v.Loader = "Fabric";
            return v;
        }

        // ---------- 迁移 ----------

        // 目标版本名：重名就加后缀，绝不覆盖
        public static string UniqueId(string wanted)
        {
            string baseId = wanted;
            for (int i = 0; i < 1000; i++)
            {
                string id = i == 0 ? baseId : baseId + " (" + i + ")";
                if (!Directory.Exists(Path.Combine(Paths.VersionsDir, id))) return id;
            }
            return baseId + " (" + Guid.NewGuid().ToString("N").Substring(0, 4) + ")";
        }

        // 复制一个版本目录（跳过临时文件），返回目标目录
        public static string CopyVersion(ScannedVersion v, string newId, Action<string> log)
        {
            string target = Path.Combine(Paths.VersionsDir, newId);
            CopyDir(v.Dir, target, log);
            // json 里的 id 字段改名，否则启动器按 json 认不出这个目录
            try
            {
                string folderJson = Path.Combine(v.Dir, Path.GetFileName(v.Dir) + ".json");
                string srcJson = File.Exists(folderJson) ? folderJson : v.JsonPath;
                if (!string.IsNullOrEmpty(srcJson) && File.Exists(srcJson))
                {
                    string dstJson = Path.Combine(target, newId + ".json");
                    string text = File.ReadAllText(srcJson);
                    var d = Json.Parse(text);
                    if (d != null)
                    {
                        d["id"] = newId;
                        text = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d);
                    }
                    File.WriteAllText(dstJson, text);
                    // 旧名字的 json 删掉，避免同目录两份
                    foreach (string f in Directory.GetFiles(target, "*.json"))
                        if (!string.Equals(Path.GetFileNameWithoutExtension(f), newId, StringComparison.OrdinalIgnoreCase))
                            try { File.Delete(f); } catch { }
                }
            }
            catch (Exception ex) { if (log != null) log("改 id 失败：" + ex.Message); }
            // jar 也改名成 <id>.jar，并清掉旧名字那份
            try
            {
                if (!string.IsNullOrEmpty(v.JarPath) && File.Exists(v.JarPath))
                {
                    string dstJar = Path.Combine(target, newId + ".jar");
                    if (!File.Exists(dstJar)) File.Copy(v.JarPath, dstJar, true);
                    foreach (string f in Directory.GetFiles(target, "*.jar"))
                        if (!string.Equals(Path.GetFileNameWithoutExtension(f), newId, StringComparison.OrdinalIgnoreCase))
                            try { File.Delete(f); } catch { }
                }
            }
            catch { }
            return target;
        }

        static void CopyDir(string from, string to, Action<string> log)
        {
            Directory.CreateDirectory(to);
            foreach (string f in Directory.GetFiles(from))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".tmp" || ext == ".part") continue;
                try { File.Copy(f, Path.Combine(to, Path.GetFileName(f)), true); }
                catch (Exception ex) { if (log != null) log("跳过 " + Path.GetFileName(f) + "：" + ex.Message); }
            }
            foreach (string d in Directory.GetDirectories(from))
            {
                string name = Path.GetFileName(d);
                if (string.Equals(name, ".minecraft", StringComparison.OrdinalIgnoreCase)) continue;
                CopyDir(d, Path.Combine(to, name), log);
            }
        }

        // 复制一个版本引用到的库文件（含父版本），失败不致命
        public static int CopyLibraries(ScannedVersion v, Action<string> log)
        {
            int n = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string cur = v.JsonPath;
            for (int depth = 0; depth < 5 && !string.IsNullOrEmpty(cur); depth++)
            {
                n += CopyLibrariesOfJson(cur, v.Root, seen, log);
                string baseId = null;
                try
                {
                    var d = Json.Parse(File.ReadAllText(cur));
                    baseId = Json.Str(d, "inheritsFrom");
                }
                catch { }
                if (string.IsNullOrEmpty(baseId)) break;
                string next = Path.Combine(v.Root, "versions", baseId, baseId + ".json");
                cur = File.Exists(next) ? next : null;
            }
            return n;
        }

        static int CopyLibrariesOfJson(string jsonPath, string root, HashSet<string> seen, Action<string> log)
        {
            int n = 0;
            try
            {
                var d = Json.Parse(File.ReadAllText(jsonPath));
                if (d == null || !d.ContainsKey("libraries")) return 0;
                var arr = Json.Arr(d["libraries"]);
                if (arr == null) return 0;
                foreach (object o in arr)
                {
                    var lib = Json.Obj(o);
                    if (lib == null || !lib.ContainsKey("downloads")) continue;
                    var dl = Json.Obj(lib["downloads"]);
                    if (dl == null || !dl.ContainsKey("artifact")) continue;
                    var art = Json.Obj(dl["artifact"]);
                    if (art == null) continue;
                    string rel = Json.Str(art, "path");
                    if (string.IsNullOrEmpty(rel)) continue;
                    if (!seen.Add(rel)) continue;
                    string src = Path.Combine(root, "libraries", rel.Replace('/', '\\'));
                    if (!File.Exists(src)) continue;
                    string dst = Path.Combine(Paths.LibrariesDir, rel.Replace('/', '\\'));
                    if (File.Exists(dst)) continue;
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(dst));
                        File.Copy(src, dst, true);
                        n++;
                    }
                    catch (Exception ex) { if (log != null) log("库文件跳过：" + ex.Message); }
                }
            }
            catch { }
            return n;
        }
    }
}
