// HILCheck - Huge Insane Launcher 全面自检工具（终端版）
// 双击运行：逐项检查网络服务与本机环境，末尾汇总；有失败项时退出码 = 失败数。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Web.Script.Serialization;

static class HILCheck
{
    const string UA = "HugeInsaneLauncher/26.8.13 HILCheck";
    const string ExpectedVersion = "26.8.13";
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    static int fails = 0, warns = 0;

    static void Ok(string name, string detail) { Report(" OK ", name, detail, ConsoleColor.Green); }
    static void Warn(string name, string detail) { warns++; Report("警告", name, detail, ConsoleColor.Yellow); }
    static void Fail(string name, string detail) { fails++; Report("失败", name, detail, ConsoleColor.Red); }
    static void Report(string tag, string name, string detail, ConsoleColor c)
    {
        Console.ForegroundColor = c;
        Console.Write("[" + tag + "]");
        Console.ResetColor();
        Console.WriteLine(" " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail));
    }

    // ---------- 网络小助手 ----------
    static string HttpGet(string url, out int status, long rangeEnd)
    {
        status = 0;
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.UserAgent = UA;
        req.Timeout = 15000; req.ReadWriteTimeout = 15000;
        req.AllowAutoRedirect = true;
        if (rangeEnd > 0) req.AddRange(0, rangeEnd);
        try
        {
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                status = (int)resp.StatusCode;
                using (var rs = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    rs.CopyTo(ms, 65536);
                    if (ms.Length > 512 * 1024) ms.SetLength(512 * 1024);
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
        }
        catch (WebException we)
        {
            // 非 2xx 也属于"服务活着"：把状态码带回去交给调用方判断
            var resp = we.Response as HttpWebResponse;
            if (resp == null) throw;
            status = (int)resp.StatusCode;
            using (var rs = resp.GetResponseStream())
            {
                if (rs == null) return "";
                using (var ms = new MemoryStream()) { rs.CopyTo(ms); return Encoding.UTF8.GetString(ms.ToArray()); }
            }
        }
    }

    static string HttpPostJson(string url, string json, out int status)
    {
        status = 0;
        var req = (HttpWebRequest)WebRequest.Create(url);
        req.Method = "POST";
        req.UserAgent = UA;
        req.ContentType = "application/json";
        req.Timeout = 30000; req.ReadWriteTimeout = 30000;
        byte[] body = Encoding.UTF8.GetBytes(json);
        req.ContentLength = body.Length;
        using (var rs = req.GetRequestStream()) rs.Write(body, 0, body.Length);
        using (var resp = (HttpWebResponse)req.GetResponse())
        {
            status = (int)resp.StatusCode;
            using (var s = resp.GetResponseStream())
            using (var ms = new MemoryStream()) { s.CopyTo(ms); return Encoding.UTF8.GetString(ms.ToArray()); }
        }
    }

    // 简易 WebSocket 握手：只验证能升级成功（101），随后直接关闭
    static string WsHandshake(string host, string path)
    {
        using (var tcp = new TcpClient())
        {
            tcp.ReceiveTimeout = 15000; tcp.SendTimeout = 15000;
            tcp.Connect(host, 443);
            using (var ssl = new SslStream(tcp.GetStream(), false))
            {
                ssl.AuthenticateAsClient(host, null, SslProtocols.Tls12, false);
                string key = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
                string reqStr =
                    "GET " + path + " HTTP/1.1\r\n" +
                    "Host: " + host + "\r\n" +
                    "Upgrade: websocket\r\n" +
                    "Connection: Upgrade\r\n" +
                    "Sec-WebSocket-Key: " + key + "\r\n" +
                    "Sec-WebSocket-Version: 13\r\n" +
                    "User-Agent: " + UA + "\r\n\r\n";
                byte[] data = Encoding.ASCII.GetBytes(reqStr);
                ssl.Write(data, 0, data.Length);
                var sb = new StringBuilder();
                var buf = new byte[1024];
                int total = 0;
                while (total < 4096)
                {
                    int n = ssl.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    sb.Append(Encoding.ASCII.GetString(buf, 0, n));
                    total += n;
                    if (sb.ToString().IndexOf("\r\n\r\n") >= 0) break;
                }
                string first = sb.ToString().Split('\n')[0].Trim();
                return first; // 形如 "HTTP/1.1 101 Switching Protocols"
            }
        }
    }

    // 探活类：任何 HTTP 响应都算服务在线（CDN 根路径返回 404 属正常）
    static void AliveCheck(string name, string url)
    {
        try
        {
            int st; HttpGet(url, out st, 0);
            Ok(name, "可达（HTTP " + st + "）");
        }
        catch (Exception ex) { Fail(name, ex.Message); }
    }

    static void NetCheck(string name, string url, string expect)
    {
        try
        {
            int st; string body = HttpGet(url, out st, 0);
            if (st >= 200 && st < 300) Ok(name, expect);
            else Fail(name, "HTTP " + st);
        }
        catch (Exception ex) { Fail(name, ex.Message); }
    }

    // ---------- 版本目录体检 ----------
    static string FindRoot()
    {
        string dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 4 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, ".minecraft"))) return dir;
            dir = Path.GetDirectoryName(dir.TrimEnd('\\'));
        }
        return null;
    }

    static Dictionary<string, object> ReadJsonObj(string path)
    {
        return Json.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
    }

    // 沿 inheritsFrom 链收集所有 libraries 条目（最多 8 跳）
    static int CheckLibraries(string versionsDir, string librariesDir, string id, out int missing)
    {
        missing = 0;
        int total = 0;
        string cur = id;
        for (int hop = 0; hop < 8 && cur != null; hop++)
        {
            string jp = Path.Combine(versionsDir, cur, cur + ".json");
            if (!File.Exists(jp)) break;
            Dictionary<string, object> j;
            try { j = ReadJsonObj(jp); } catch { break; }
            object libs;
            if (j.TryGetValue("libraries", out libs))
            {
                foreach (var o in (System.Collections.ArrayList)libs)
                {
                    var lib = o as Dictionary<string, object>;
                    if (lib == null || !lib.ContainsKey("name")) continue;
                    if (lib.ContainsKey("natives") || lib.ContainsKey("rules")) continue; // 平台相关条目跳过
                    string[] p = ((string)lib["name"]).Split(':');
                    if (p.Length < 3) continue;
                    total++;
                    string rel = p[0].Replace('.', '/') + "/" + p[1] + "/" + p[2] + "/" +
                                 p[1] + "-" + p[2] + (p.Length > 3 ? "-" + p[3] : "") + ".jar";
                    if (!File.Exists(Path.Combine(librariesDir, rel))) missing++;
                }
            }
            object parent;
            cur = j.TryGetValue("inheritsFrom", out parent) ? parent as string : null;
        }
        return total;
    }

    static int Main()
    {
        Console.OutputEncoding = Encoding.UTF8;
        try { ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11; } catch { }
        Console.WriteLine("===== HIL 自检 (HILCheck) =====");
        Console.WriteLine();

        // ===== A. 网络服务 =====
        Console.WriteLine("--- A. 网络服务 ---");
        try
        {
            int st; string txt = HttpGet("https://hugeinsane.pages.dev/version.txt?t=" + DateTime.Now.Ticks, out st, 0).Trim();
            if (st == 200 && txt == ExpectedVersion) Ok("官网版本号", txt);
            else if (st == 200) Fail("官网版本号", "云端=" + txt + "，本地期望=" + ExpectedVersion + "（两端不一致，会误报更新）");
            else Fail("官网版本号", "HTTP " + st);
        }
        catch (Exception ex) { Fail("官网版本号", ex.Message); }

        try
        {
            int st; HttpGet("https://hugeinsane.pages.dev/HugeInsaneLauncher.exe?t=" + DateTime.Now.Ticks, out st, 1024);
            if (st == 200 || st == 206) Ok("官网启动器下载", "可下载（HTTP " + st + "）");
            else Fail("官网启动器下载", "HTTP " + st);
        }
        catch (Exception ex) { Fail("官网启动器下载", ex.Message); }

        try
        {
            int st;
            string resp = HttpPostJson("https://hugeinsane.pages.dev/api/hshi",
                "{\"model\":\"glm-4-flash\",\"messages\":[{\"role\":\"user\",\"content\":\"ping\"}],\"tools\":[]}", out st);
            if (st >= 200 && st < 300) Ok("H史 AI 中转", "HTTP " + st);
            else Fail("H史 AI 中转", "HTTP " + st + "：" + (resp.Length > 80 ? resp.Substring(0, 80) : resp));
        }
        catch (Exception ex) { Fail("H史 AI 中转", ex.Message); }

        try
        {
            int st; HttpGet("https://hugeinsane.pages.dev/api/relay?room=HILCHK&role=host", out st, 0);
            if (st == 426) Ok("Huge 隧道（身份校验）", "UA 通过，等待 WebSocket（HTTP 426）");
            else if (st == 403) Fail("Huge 隧道（身份校验）", "403 forbidden —— 云端 worker 是旧版，需重新部署 hgl-relay");
            else Warn("Huge 隧道（身份校验）", "HTTP " + st + "（期望 426）");
        }
        catch (Exception ex) { Fail("Huge 隧道（身份校验）", ex.Message); }

        try
        {
            string line = WsHandshake("hugeinsane.pages.dev", "/api/relay?room=HILCHK&role=host");
            if (line.IndexOf("101") >= 0) Ok("Huge 隧道（WS 握手）", line);
            else Fail("Huge 隧道（WS 握手）", line);
        }
        catch (Exception ex) { Fail("Huge 隧道（WS 握手）", ex.Message); }

        NetCheck("Mojang 版本清单", "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json", "可获取");
        AliveCheck("Mojang 资源 CDN", "https://piston-data.mojang.com/");
        NetCheck("BMCLAPI 镜像", "https://bmclapi2.bangbang93.com/mc/game/version_manifest_v2.json", "可获取");
        NetCheck("Fabric Meta", "https://meta.fabricmc.net/v2/versions/game", "可获取");
        NetCheck("Forge Maven", "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", "可获取");
        NetCheck("NeoForge Maven", "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", "可获取");
        NetCheck("Modrinth API", "https://api.modrinth.com/v2/search?limit=1", "可搜索");
        NetCheck("Paper 开服核心", "https://fill.papermc.io/v3/projects/paper", "可获取");

        // ===== B. 本机环境 =====
        Console.WriteLine();
        Console.WriteLine("--- B. 本机环境 ---");

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("java", "-version")
            { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            var p = System.Diagnostics.Process.Start(psi);
            string first = (p.StandardError.ReadLine() ?? "").Trim();
            p.WaitForExit(8000);
            if (p.ExitCode == 0 && first.Length > 0) Ok("Java", first);
            else Warn("Java", "PATH 上未找到可用 java（HIL 可自动下载运行时，不影响游戏）");
        }
        catch { Warn("Java", "PATH 上未安装（HIL 可自动下载运行时，不影响游戏）"); }

        string root = FindRoot();
        if (root == null) { Fail("目录定位", "未找到 .minecraft 目录（请把 HILCheck 放到启动器附近运行）"); }
        else
        {
            string mcDir = Path.Combine(root, ".minecraft");
            string versionsDir = Path.Combine(mcDir, "versions");
            string librariesDir = Path.Combine(mcDir, "libraries");
            Ok("目录定位", root);

            if (!Directory.Exists(versionsDir)) Fail("版本目录", versionsDir + " 不存在");
            else
            {
                string[] dirs = Directory.GetDirectories(versionsDir);
                int good = 0, bad = 0, totalLibs = 0, missLibs = 0;
                foreach (var dir in dirs)
                {
                    string id = Path.GetFileName(dir);
                    string jp = Path.Combine(dir, id + ".json");
                    if (!File.Exists(jp)) { bad++; Fail("版本 " + id, "json 缺失（下载失败的残骸，可删）"); continue; }
                    Dictionary<string, object> j;
                    try { j = ReadJsonObj(jp); }
                    catch { bad++; Fail("版本 " + id, "json 损坏无法解析"); continue; }
                    object idv; object parent;
                    bool hasParent = j.TryGetValue("inheritsFrom", out parent) && parent != null && (parent as string) != null;
                    if (j.TryGetValue("id", out idv) && (string)idv != id)
                        Warn("版本 " + id, "json 内 id=" + idv + " 与文件夹名不一致");
                    if (hasParent)
                    {
                        string par = (string)parent;
                        if (!File.Exists(Path.Combine(versionsDir, par, par + ".json")))
                        { bad++; Fail("版本 " + id, "依赖的父版本 " + par + " 未安装（这就是'版本未安装'报错的原因）"); continue; }
                    }
                    else
                    {
                        if (!File.Exists(Path.Combine(dir, id + ".jar")))
                        { bad++; Fail("版本 " + id, "游戏 jar 缺失"); continue; }
                    }
                    int miss; totalLibs += CheckLibraries(versionsDir, librariesDir, id, out miss);
                    missLibs += miss;
                    good++;
                    Ok("版本 " + id, hasParent ? "加载器版本（父版本 " + parent + "）" : "原版/整合版" + (miss > 0 ? "，缺库 " + miss : ""));
                }
                if (bad == 0) Ok("版本扫描汇总", "共 " + dirs.Length + " 个版本全部健康");
                else Fail("版本扫描汇总", dirs.Length + " 个版本中 " + bad + " 个有问题");
                if (missLibs == 0) Ok("库文件完整性", "校验 " + totalLibs + " 个库文件，无缺失");
                else Fail("库文件完整性", "校验 " + totalLibs + " 个库文件，缺失 " + missLibs + " 个（启动可能崩溃，重下该版本可修复）");
            }

            // 配置文件一致性
            string cfgPath = Path.Combine(root, "HugeInsaneLauncher", "config.ini");
            if (!File.Exists(cfgPath)) Warn("配置文件", "config.ini 不存在（首次运行启动器后会生成）");
            else
            {
                var kv = new Dictionary<string, string>();
                foreach (var line in File.ReadAllLines(cfgPath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
                string vid;
                if (kv.TryGetValue("VersionId", out vid) && vid.Length > 0)
                {
                    string vp = Path.Combine(versionsDir, vid, vid + ".json");
                    if (File.Exists(vp)) Ok("当前选中版本", vid);
                    else Warn("当前选中版本", vid + " 未安装（可能是整合包实例名）");
                }
                else Warn("当前选中版本", "config.ini 里未设置 VersionId");
                string hist;
                if (kv.TryGetValue("UserHistory", out hist) && hist.Length > 0)
                    Ok("用户名历史", hist.Split(',').Length + " 个");
            }
        }

        // ===== 汇总 =====
        Console.WriteLine();
        Console.WriteLine("===== 自检完成：失败 " + fails + " 项，警告 " + warns + " 项 =====");
        if (fails == 0) { Console.ForegroundColor = ConsoleColor.Green; Console.WriteLine("结论：所有关键功能可用，可以发布。"); }
        else { Console.ForegroundColor = ConsoleColor.Red; Console.WriteLine("结论：有 " + fails + " 项失败，请先看上面的 [失败] 条目。"); }
        Console.ResetColor();
        if (Environment.UserInteractive && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.Write("按任意键退出...");
            try { Console.ReadKey(true); } catch { }
        }
        return fails;
    }
}
