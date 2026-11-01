// HugeLauncher - 本地 Mod 扫描：列表/图标/启用禁用/删除/安装
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace HugeLauncher
{
    class LocalMod
    {
        public string FilePath;      // 完整路径（含 .disabled 后缀）
        public string FileName;      // 文件名
        public string DisplayName;   // 展示名（解析失败则为文件名）
        public string Version = "";
        public string Description = "";
        public bool Enabled;         // .jar = true, .jar.disabled = false
        public string IconPath;      // 缓存的图标 png 路径，可能为 null

        public string ModsDir { get { return Path.GetDirectoryName(FilePath); } }
    }

    static class LocalMods
    {
        // 扫描实例的 mods 目录
        public static List<LocalMod> Scan(string instanceName)
        {
            var list = new List<LocalMod>();
            string dir = Path.Combine(Paths.InstanceDir(instanceName), "mods");
            if (!Directory.Exists(dir)) return list;
            foreach (var f in Directory.GetFiles(dir))
            {
                string name = Path.GetFileName(f);
                bool enabled = name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
                bool disabled = name.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase);
                if (!enabled && !disabled) continue;
                var m = new LocalMod { FilePath = f, FileName = name, Enabled = enabled, DisplayName = name };
                TryParse(f, m);
                list.Add(m);
            }
            list.Sort(delegate(LocalMod a, LocalMod b) { return string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase); });
            return list;
        }

        static void TryParse(string jarPath, LocalMod m)
        {
            try
            {
                using (var zip = ZipFile.OpenRead(jarPath))
                {
                    // Fabric: fabric.mod.json
                    var e = zip.GetEntry("fabric.mod.json");
                    if (e != null)
                    {
                        using (var s = new StreamReader(e.Open(), Encoding.UTF8))
                        {
                            var j = Json.Parse(s.ReadToEnd());
                            m.DisplayName = Json.Str(j, "name") ?? m.DisplayName;
                            m.Version = Json.Str(j, "version") ?? "";
                            m.Description = Json.Str(j, "description") ?? "";
                            string icon = Json.Str(j, "icon");
                            if (icon != null) ExtractIcon(zip, icon, m);
                        }
                        return;
                    }
                    // NeoForge: META-INF/neoforge.mods.toml; Forge: META-INF/mods.toml
                    e = zip.GetEntry("META-INF/neoforge.mods.toml");
                    if (e == null) e = zip.GetEntry("META-INF/mods.toml");
                    if (e != null)
                    {
                        string toml;
                        using (var s = new StreamReader(e.Open(), Encoding.UTF8)) toml = s.ReadToEnd();
                        ParseModsToml(toml, m);
                        string logo = TomlValue(toml, "logoFile");
                        if (logo != null) ExtractIcon(zip, logo, m);
                        return;
                    }
                }
            }
            catch { }
        }

        // 极简 TOML 取值：抓 [[mods]] 段内的 key="value"
        static string TomlValue(string toml, string key)
        {
            string needle = key + "=";
            foreach (var raw in toml.Split('\n'))
            {
                var line = raw.Trim();
                if (line.StartsWith("#")) continue;
                int i = line.IndexOf(needle, StringComparison.Ordinal);
                if (i < 0) continue;
                string v = line.Substring(i + needle.Length).Trim();
                if (v.StartsWith("\"") && v.EndsWith("\"") && v.Length >= 2)
                    return v.Substring(1, v.Length - 2);
            }
            return null;
        }

        static void ParseModsToml(string toml, LocalMod m)
        {
            // 取 [[mods]] 之后的第一个 displayName / version
            int modsIdx = toml.IndexOf("[[mods]]", StringComparison.Ordinal);
            string scope = modsIdx >= 0 ? toml.Substring(modsIdx) : toml;
            string name = TomlValue(scope, "displayName");
            if (name != null) m.DisplayName = name;
            string ver = TomlValue(scope, "version");
            if (ver != null && !ver.StartsWith("${")) m.Version = ver;
            string desc = TomlValue(scope, "description");
            if (desc != null) m.Description = desc;
        }

        static void ExtractIcon(ZipArchive zip, string iconPath, LocalMod m)
        {
            try
            {
                var e = zip.GetEntry(iconPath.Replace('\\', '/'));
                if (e == null) return;
                string cacheDir = Path.Combine(Paths.LauncherDir, "cache", "modicons");
                Directory.CreateDirectory(cacheDir);
                string dest = Path.Combine(cacheDir,
                    Math.Abs(m.FileName.GetHashCode()).ToString("x") + ".png");
                using (var s = e.Open())
                using (var fs = new FileStream(dest, FileMode.Create))
                    s.CopyTo(fs);
                m.IconPath = dest;
            }
            catch { }
        }

        public static void SetEnabled(LocalMod m, bool enabled)
        {
            if (m.Enabled == enabled) return;
            string newPath = enabled
                ? m.FilePath.Substring(0, m.FilePath.Length - ".disabled".Length)
                : m.FilePath + ".disabled";
            File.Move(m.FilePath, newPath);
            m.FilePath = newPath;
            m.FileName = Path.GetFileName(newPath);
            m.Enabled = enabled;
        }

        public static void Delete(LocalMod m)
        {
            File.Delete(m.FilePath);
            if (m.IconPath != null) { try { File.Delete(m.IconPath); } catch { } }
        }

        // 从文件安装（复制到 mods 目录），返回安装的 Mod 数
        public static int InstallFromFiles(string instanceName, string[] files)
        {
            string dir = Path.Combine(Paths.InstanceDir(instanceName), "mods");
            Directory.CreateDirectory(dir);
            int n = 0;
            foreach (var f in files)
            {
                string name = Path.GetFileName(f);
                if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)) continue;
                File.Copy(f, Path.Combine(dir, name), true);
                n++;
            }
            return n;
        }
    }
}
