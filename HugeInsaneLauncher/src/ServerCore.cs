// HugeLauncher - 一键开服：实例管理 + 服务端核心下载（Vanilla/Paper 直链下载）
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HugeLauncher
{
    class ServerInstance
    {
        public string Name = "";        // 实例名 = 目录名（= 版本名）
        public string GameVersion = "";
        public string CoreType = "vanilla"; // vanilla / paper
        public int MemoryMb = 2048;
        public int Port = 25565;
        public string CreatedAt = "";
        public string DisplayName = ""; // 服务器名称（MOTD 第一行 + HIL 实例下拉显示）
        public string Motd = "";        // 服务器介绍（MOTD 第二行）
        public bool Tunnel;             // 开启 Huge 隧道（无公网入口时朋友用房间码加入）
        public int MaxPlayers = 20;     // 玩家人数上限（默认按 内存/100 计算）

        public string Dir { get { return ServerCore.InstanceDir(Name); } }
        public string JarPath { get { return Path.Combine(Dir, "server.jar"); } }
        public bool JarReady { get { return File.Exists(JarPath); } }
    }

    static class ServerCore
    {
        public static string ServersRoot { get { return Path.Combine(Paths.McDir, "servers"); } }
        public static string InstanceDir(string name) { return Path.Combine(ServersRoot, name); }
        static string ConfigPath(string name)
        {
            // 品牌改名迁移：旧 hgl-server.json 首次读取时改名 hil-server.json
            string p = Path.Combine(InstanceDir(name), "hil-server.json");
            string old = Path.Combine(InstanceDir(name), "hgl-server.json");
            if (!File.Exists(p) && File.Exists(old))
            {
                try { File.Move(old, p); } catch { }
            }
            return p;
        }

        // 实例名列表（.minecraft/servers 下每个子目录，新创建的在前）
        public static List<string> GetInstances()
        {
            var list = new List<string>();
            try
            {
                if (Directory.Exists(ServersRoot))
                {
                    var dirs = Directory.GetDirectories(ServersRoot);
                    Array.Sort(dirs, delegate(string a, string b)
                    {
                        return Directory.GetCreationTime(b).CompareTo(Directory.GetCreationTime(a));
                    });
                    foreach (var d in dirs) list.Add(Path.GetFileName(d));
                }
            }
            catch { }
            return list;
        }

        public static bool InstanceExists(string name) { return Directory.Exists(InstanceDir(name)); }

        public static ServerInstance Load(string name)
        {
            var inst = new ServerInstance { Name = name };
            try
            {
                var cfg = Json.Parse(File.ReadAllText(ConfigPath(name), Encoding.UTF8));
                inst.GameVersion = Json.Str(cfg, "gameVersion") ?? name;
                inst.CoreType = (Json.Str(cfg, "coreType") ?? "vanilla").ToLowerInvariant();
                inst.MemoryMb = (int)Json.Long(cfg, "memoryMb");
                if (inst.MemoryMb <= 0) inst.MemoryMb = 2048;
                inst.Port = (int)Json.Long(cfg, "port");
                if (inst.Port <= 0) inst.Port = 25565;
                inst.CreatedAt = Json.Str(cfg, "createdAt") ?? "";
                inst.DisplayName = Json.Str(cfg, "displayName") ?? "";
                inst.Motd = Json.Str(cfg, "motd") ?? "";
                inst.MaxPlayers = (int)Json.Long(cfg, "maxPlayers");
                if (inst.MaxPlayers <= 0) inst.MaxPlayers = 20;
                inst.Tunnel = Json.Long(cfg, "tunnel") == 1;
            }
            catch { inst.GameVersion = name; }
            return inst;
        }

        public static void Save(ServerInstance inst)
        {
            Directory.CreateDirectory(inst.Dir);
            var d = new Dictionary<string, object>();
            d["gameVersion"] = inst.GameVersion;
            d["coreType"] = inst.CoreType;
            d["memoryMb"] = inst.MemoryMb;
            d["port"] = inst.Port;
            d["createdAt"] = inst.CreatedAt;
            d["displayName"] = inst.DisplayName;
            d["motd"] = inst.Motd;
            d["maxPlayers"] = inst.MaxPlayers;
            d["tunnel"] = inst.Tunnel ? 1 : 0;
            string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(d);
            File.WriteAllText(ConfigPath(inst.Name), json, new UTF8Encoding(false));
        }

        // 检测端口是否空闲（可绑定）
        public static bool IsPortFree(int port)
        {
            try
            {
                var l = new TcpListener(IPAddress.Any, port);
                l.Start();
                l.Stop();
                return true;
            }
            catch { return false; }
        }

        // 从指定端口起找第一个空闲端口
        public static int FindFreePort(int from)
        {
            for (int p = from; p < from + 100; p++)
                if (IsPortFree(p)) return p;
            return from;
        }

        // 写实例基础文件：eula.txt 不覆盖；server.properties 不存在则创建（离线模式 + 端口 + 人数上限 + MOTD），
        // 已存在则只同步 HIL 管理的三个键（替换不追加），玩家其余配置不动
        public static void ScaffoldInstance(ServerInstance inst)
        {
            Directory.CreateDirectory(inst.Dir);
            string eula = Path.Combine(inst.Dir, "eula.txt");
            if (!File.Exists(eula))
                File.WriteAllText(eula, "eula=true\n", new UTF8Encoding(false));
            string props = Path.Combine(inst.Dir, "server.properties");
            if (!File.Exists(props))
            {
                string text = "server-port=" + inst.Port + "\n" +
                              "online-mode=false\n" +
                              "max-players=" + inst.MaxPlayers + "\n";
                string m0 = BuildMotd(inst);
                if (m0 != null) text += "motd=" + m0 + "\n";
                File.WriteAllText(props, text, new UTF8Encoding(false));
                return;
            }
            var lines = new List<string>(File.ReadAllLines(props));
            SyncProp(lines, "server-port", inst.Port.ToString());
            SyncProp(lines, "max-players", inst.MaxPlayers.ToString());
            string m = BuildMotd(inst);
            if (m != null) SyncProp(lines, "motd", m);
            File.WriteAllLines(props, lines.ToArray(), new UTF8Encoding(false));
        }

        // 同步 properties 中的某一行（存在则替换，不存在则追加）
        static void SyncProp(List<string> lines, string key, string value)
        {
            bool found = false;
            for (int i = 0; i < lines.Count; i++)
                if (lines[i].StartsWith(key + "=")) { lines[i] = key + "=" + value; found = true; }
            if (!found) lines.Add(key + "=" + value);
        }

        // 由名称+介绍拼 MOTD（名称第一行、介绍第二行）；两者皆空返回 null（保持原 motd 不动）
        static string BuildMotd(ServerInstance inst)
        {
            string a = (inst.DisplayName ?? "").Trim();
            string b = (inst.Motd ?? "").Trim();
            if (a.Length == 0 && b.Length == 0) return null;
            if (a.Length == 0) return EscapeMotd(b);
            if (b.Length == 0) return EscapeMotd(a);
            return EscapeMotd(a) + "\\n" + EscapeMotd(b);
        }

        // java.util.Properties 按 Latin-1 读取：非 ASCII 转 \uXXXX，并转义 \ = : # !（与 vanilla 自己的写法一致）
        public static string EscapeMotd(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '=': sb.Append("\\="); break;
                    case ':': sb.Append("\\:"); break;
                    case '#': sb.Append("\\#"); break;
                    case '!': sb.Append("\\!"); break;
                    default:
                        if (c > 127) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        // 本机主网卡 IPv4（UDP 探测路由出口最可靠；失败退回网卡枚举，兜底 127.0.0.1）
        public static string GetLocalIPv4()
        {
            try
            {
                using (var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
                {
                    s.Connect("223.5.5.5", 53); // UDP 不真正发包，只为让系统选出出口网卡
                    var ep = s.LocalEndPoint as IPEndPoint;
                    if (ep != null && !IPAddress.IsLoopback(ep.Address)) return ep.Address.ToString();
                }
            }
            catch { }
            try
            {
                foreach (var ip in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
            }
            catch { }
            return "127.0.0.1";
        }

        // 本机全球单播 IPv6（仅 2000::/3，排除 Teredo 2001:0::/32 与 6to4 2002::/16；无可用地址返回 null）
        public static string GetGlobalIPv6()
        {
            try
            {
                foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        var ip = ua.Address;
                        if (ip.AddressFamily != AddressFamily.InterNetworkV6 || ip.IsIPv6LinkLocal) continue;
                        var b = ip.GetAddressBytes();
                        if ((b[0] & 0xE0) != 0x20) continue;                                     // 只认 2000::/3
                        if (b[0] == 0x20 && b[1] == 0x01 && b[2] == 0x00 && b[3] == 0x00) continue; // Teredo
                        if (b[0] == 0x20 && b[1] == 0x02) continue;                              // 6to4
                        return ip.ToString();
                    }
                }
            }
            catch { }
            return null;
        }

        // 确保服务端核心 server.jar 存在（vanilla 走官方清单，paper 走 PaperMC API）
        public static void EnsureServerJar(ServerInstance inst, Action<StepEvent> report)
        {
            if (inst.JarReady) return;
            string mcVer = GameCore.BaseGameVersion(inst.GameVersion);
            string url, sha1 = null;
            long size = 0;
            if (inst.CoreType == "paper")
            {
                report(StepEvent.Start(0, "查询 Paper 核心 " + mcVer));
                // Fill v3 API（旧 api.papermc.io/v2 已 410 下线）：
                // GET versions/<mc>/builds/latest → downloads["server:default"] 给直链/sha256/size
                string json;
                try
                {
                    json = Net.GetString("https://fill.papermc.io/v3/projects/paper/versions/" + mcVer + "/builds/latest");
                }
                catch (WebException we)
                {
                    var hr = we.Response as HttpWebResponse;
                    if (hr != null && hr.StatusCode == HttpStatusCode.NotFound)
                        throw new Exception("HIL 找不到可用的游戏版本：Paper 没有 " + mcVer + " 的构建");
                    throw;
                }
                var build = Json.Parse(json);
                var dl = Json.Obj(Json.Obj(build["downloads"])["server:default"]);
                if (dl == null)
                    throw new Exception("HIL 找不到可用的游戏版本：Paper 没有 " + mcVer + " 的构建");
                url = Json.Str(dl, "url");
                size = Json.Long(dl, "size");
                // v3 只给 sha256，而下载校验器走 sha1——哈希留空，大小校验兜底（与旧 v2 行为一致）
            }
            else
            {
                report(StepEvent.Start(0, "查询官方服务端 " + mcVer));
                GameCore.VersionEntry found = null;
                foreach (var ve in GameCore.GetVersionList())
                    if (ve.Id == mcVer) { found = ve; break; }
                if (found == null)
                    throw new Exception("HIL 找不到可用的游戏版本：官方清单里没有 " + mcVer);
                var v = Json.Parse(Net.GetString(found.Url));
                var server = Json.Obj(Json.Obj(v["downloads"])["server"]);
                if (server == null)
                    throw new Exception("HIL 找不到可用的游戏版本：" + mcVer + " 没有官方服务端");
                url = Json.Str(server, "url");
                sha1 = Json.Str(server, "sha1");
                size = Json.Long(server, "size");
            }
            report(StepEvent.Done(0, true, null));

            report(StepEvent.Start(1, "下载服务端核心"));
            Directory.CreateDirectory(inst.Dir);
            string tmp = inst.JarPath + ".download";
            var tasks = new List<DownloadTask> { new DownloadTask(url, tmp, sha1, size) };
            var fails = new DownloadPool(tasks, delegate(long d, long t, double mbps)
            {
                report(StepEvent.Prog(1, d, t, mbps));
            }).Run(4);
            if (fails.Count > 0)
            {
                report(StepEvent.Done(1, false, fails[0]));
                throw new Exception("服务端核心下载失败: " + fails[0]);
            }
            if (File.Exists(inst.JarPath)) File.Delete(inst.JarPath);
            File.Move(tmp, inst.JarPath);
            report(StepEvent.Done(1, true, null));
        }
    }
}
