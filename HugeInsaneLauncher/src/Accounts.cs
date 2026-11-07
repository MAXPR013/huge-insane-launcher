// HugeLauncher - 账号体系：账号档案存储 + Yggdrasil 外置登录客户端 + authlib-injector 管理
// 存储：HugeInsaneLauncher/accounts.json；首次运行从 Config 的用户名/历史迁移成离线账号。
// 离线账号 UUID 目前沿用历史硬编码值（全离线用户共用），等 Huge 账号（自建认证服务器）落地后统一解决。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;

namespace HugeLauncher
{
    // 账号档案：离线 / 外置(authlib-injector) / 微软(占位，未来接入)
    class Account
    {
        public string Name = "";        // 游戏内显示名（外置=服务器档案名）
        public string Type = "offline"; // offline | external | microsoft
        public string Uuid = "";        // 外置=服务器签发；离线=LegacyUuid（见上注释）
        public string Server = "";      // 外置：API 根（如 https://littleskin.cn/api/yggdrasil）
        public string ServerName = "";  // 外置：服务器显示名（LittleSkin 等）
        public string AccessToken = "";
        public string ClientToken = "";
        public long LastUsed;           // unix 秒

        // 离线模式历史遗留：所有离线用户共用的占位 UUID（GameCore 旧行为，保持兼容）
        public const string LegacyUuid = "069a79f4-44e9-4726-a5be-fca90e38aaf5";

        public string TypeLabel
        {
            get
            {
                switch (Type)
                {
                    case "external": return "外置";
                    case "microsoft": return "微软";
                    default: return "离线";
                }
            }
        }

        public string UuidShort
        {
            get
            {
                if (string.IsNullOrEmpty(Uuid)) return "";
                string u = Uuid.Replace("-", "");
                return u.Length > 10 ? u.Substring(0, 4) + "…" + u.Substring(u.Length - 4) : u;
            }
        }

        // 令牌落盘加密：DPAPI（当前用户）。旧明文文件读进来也能用，写出时自动升级成密文。
        static readonly byte[] Entropy = System.Text.Encoding.UTF8.GetBytes("HIL.acct.v1");

        public static string Protect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            try
            {
                byte[] raw = System.Text.Encoding.UTF8.GetBytes(s);
                byte[] enc = System.Security.Cryptography.ProtectedData.Protect(
                    raw, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return "dpapi:" + Convert.ToBase64String(enc);
            }
            catch { return s; }
        }

        public static string Unprotect(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (!s.StartsWith("dpapi:")) return s;   // 旧明文
            try
            {
                byte[] enc = Convert.FromBase64String(s.Substring(6));
                byte[] raw = System.Security.Cryptography.ProtectedData.Unprotect(
                    enc, Entropy, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                return System.Text.Encoding.UTF8.GetString(raw);
            }
            catch { return ""; }
        }

        public Dictionary<string, object> ToDict()
        {
            var d = new Dictionary<string, object>();
            d["name"] = Name;
            d["type"] = Type;
            d["uuid"] = Uuid;
            d["server"] = Server;
            d["serverName"] = ServerName;
            d["accessToken"] = Protect(AccessToken);
            d["clientToken"] = Protect(ClientToken);
            d["lastUsed"] = LastUsed;
            return d;
        }

        public static Account FromDict(Dictionary<string, object> d)
        {
            var a = new Account();
            a.Name = Json.Str(d, "name") ?? "";
            a.Type = Json.Str(d, "type") ?? "offline";
            a.Uuid = Json.Str(d, "uuid") ?? "";
            a.Server = Json.Str(d, "server") ?? "";
            a.ServerName = Json.Str(d, "serverName") ?? "";
            a.AccessToken = Unprotect(Json.Str(d, "accessToken"));
            a.ClientToken = Unprotect(Json.Str(d, "clientToken"));
            a.LastUsed = Json.Long(d, "lastUsed");
            return a;
        }
    }

    // 账号库：加载/保存/选中/增删；变更时触发 Changed（UI 线程外也可能触发，订阅方自备 Dispatcher）
    static class AccountStore
    {
        static List<Account> accounts;
        static string selectedKey;   // "offline:MAX" / "external:e3f2…"（名字会重名，外置用 uuid）
        static JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static event Action Changed;

        static string FilePath { get { return Path.Combine(Paths.LauncherDir, "accounts.json"); } }

        static string KeyOf(Account a)
        {
            return a.Type == "external" ? "external:" + a.Uuid : a.Type + ":" + a.Name;
        }

        public static List<Account> All()
        {
            Load();
            return new List<Account>(accounts);
        }

        public static Account Selected()
        {
            Load();
            foreach (var a in accounts) if (KeyOf(a) == selectedKey) return a;
            if (accounts.Count > 0) { selectedKey = KeyOf(accounts[0]); return accounts[0]; }
            return null;
        }

        public static void Select(Account a)
        {
            if (a == null) return;
            selectedKey = KeyOf(a);
            a.LastUsed = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            Save();
            Fire();
        }

        // 添加离线账号；同名已存在则直接选中并返回旧的
        public static Account AddOffline(string name)
        {
            Load();
            name = (name ?? "").Trim();
            if (name.Length == 0) return null;
            foreach (var a in accounts)
                if (a.Type == "offline" && a.Name == name) { Select(a); return a; }
            var acc = new Account();
            acc.Name = name;
            acc.Type = "offline";
            acc.Uuid = Account.LegacyUuid;   // 沿用旧行为；Huge 账号落地后统一修
            accounts.Insert(0, acc);
            Select(acc);
            return acc;
        }

        // 外置登录成功或令牌刷新后入册（同 uuid 覆盖旧记录）
        public static Account AddExternal(Account acc)
        {
            Load();
            for (int i = accounts.Count - 1; i >= 0; i--)
                if (accounts[i].Type == "external" && accounts[i].Uuid == acc.Uuid)
                    accounts.RemoveAt(i);
            accounts.Insert(0, acc);
            Select(acc);
            return acc;
        }

        public static void Remove(Account a)
        {
            Load();
            string k = KeyOf(a);
            accounts.RemoveAll(delegate(Account x) { return KeyOf(x) == k; });
            if (selectedKey == k) selectedKey = accounts.Count > 0 ? KeyOf(accounts[0]) : null;
            Save();
            Fire();
        }

        public static void Save()
        {
            if (accounts == null) return;
            try
            {
                Directory.CreateDirectory(Paths.LauncherDir);
                var list = new List<Dictionary<string, object>>();
                foreach (var a in accounts) list.Add(a.ToDict());
                var root = new Dictionary<string, object>();
                root["selected"] = selectedKey ?? "";
                root["accounts"] = list;
                File.WriteAllText(FilePath, ser.Serialize(root), new UTF8Encoding(false));
            }
            catch { }
        }

        static void Fire()
        {
            var h = Changed;
            if (h != null) h();
        }

        // 首次运行：从 Config 的用户名/历史迁移（老用户无感）
        public static void Init(Config cfg)
        {
            Load();
            if (accounts.Count > 0 || File.Exists(FilePath)) return;
            var names = new List<string>();
            if (!string.IsNullOrEmpty(cfg.Username)) names.Add(cfg.Username);
            foreach (var n in cfg.UserHistory) if (!names.Contains(n)) names.Add(n);
            foreach (var n in names)
            {
                var acc = new Account();
                acc.Name = n;
                acc.Type = "offline";
                acc.Uuid = Account.LegacyUuid;
                accounts.Add(acc);
            }
            if (accounts.Count > 0) selectedKey = KeyOf(accounts[0]);
            Save();
        }

        static void Load()
        {
            if (accounts != null) return;
            accounts = new List<Account>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var root = Json.Parse(File.ReadAllText(FilePath, Encoding.UTF8));
                selectedKey = Json.Str(root, "selected");
                var arr = Json.Arr(root.ContainsKey("accounts") ? root["accounts"] : null);
                if (arr == null) return;
                foreach (var o in arr)
                {
                    var d = Json.Obj(o);
                    if (d == null) continue;
                    var a = Account.FromDict(d);
                    if (a.Name.Length > 0) accounts.Add(a);
                }
            }
            catch { }
        }
    }

    // authlib-injector 管理：jar 存 HugeInsaneLauncher/authlib-injector.jar，首次外置登录启动时下载
    static class AuthlibInjector
    {
        public static string JarPath { get { return Path.Combine(Paths.LauncherDir, "authlib-injector.jar"); } }
        public static bool Ready { get { return File.Exists(JarPath); } }

        // 同步确保就绪（启动流程里调用）；失败抛异常
        public static void Ensure(Action<string> log)
        {
            if (Ready) return;
            // bmclapi 镜像优先（国内快），官方兜底；latest.json 给真实下载地址
            string[] metaUrls =
            {
                "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/artifact/latest.json",
                "https://authlib-injector.yushi.moe/artifact/latest.json",
            };
            Exception last = null;
            foreach (var mu in metaUrls)
            {
                try
                {
                    if (log != null) log("正在下载 authlib-injector（外置登录补丁，一次性）...");
                    var meta = Json.Parse(Net.GetString(mu));
                    string url = Json.Str(meta, "download_url");
                    if (string.IsNullOrEmpty(url)) throw new Exception("镜像未返回下载地址");
                    Net.DownloadFile(new DownloadTask(url, JarPath, null, 0), null);
                    if (Ready) return;
                    throw new Exception("下载后文件缺失");
                }
                catch (Exception ex) { last = ex; }
            }
            throw new Exception("authlib-injector 下载失败：" + (last == null ? "未知错误" : last.Message));
        }
    }

    // Yggdrasil 认证客户端：authenticate / refresh / validate + 服务器元数据
    static class Yggdrasil
    {
        static JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static string NormalizeServer(string server)
        {
            server = (server ?? "").Trim().TrimEnd('/');
            if (server.Length == 0) throw new Exception("请填写认证服务器地址");
            if (!server.StartsWith("http://") && !server.StartsWith("https://"))
                server = "https://" + server;
            return server;
        }

        // GET 服务器根 → authlib-injector 元数据里的 serverName（拿不到就用域名）
        public static string FetchServerName(string serverRoot)
        {
            try
            {
                var meta = Json.Parse(Net.GetString(serverRoot));
                var m = Json.Obj(meta.ContainsKey("meta") ? meta["meta"] : null);
                string n = m != null ? Json.Str(m, "serverName") : null;
                if (!string.IsNullOrEmpty(n)) return n;
            }
            catch { }
            try { return new Uri(serverRoot).Host; } catch { return serverRoot; }
        }

        // 登录：POST {root}/authserver/authenticate；成功返回填充好的 Account（未入册）
        public static Account Authenticate(string serverRoot, string email, string password)
        {
            serverRoot = NormalizeServer(serverRoot);
            string serverName = FetchServerName(serverRoot);
            var req = new Dictionary<string, object>();
            req["username"] = email;
            req["password"] = password;
            req["clientToken"] = Guid.NewGuid().ToString("N");
            req["requestUser"] = true;
            var agent = new Dictionary<string, object>();
            agent["name"] = "Minecraft";
            agent["version"] = 1;
            req["agent"] = agent;

            var resp = Post(serverRoot + "/authserver/authenticate", req);
            var acc = new Account();
            acc.Type = "external";
            acc.Server = serverRoot;
            acc.ServerName = serverName;
            acc.AccessToken = Json.Str(resp, "accessToken") ?? "";
            acc.ClientToken = Json.Str(resp, "clientToken") ?? "";
            var prof = Json.Obj(resp.ContainsKey("selectedProfile") ? resp["selectedProfile"] : null);
            if (prof == null)
            {
                var avail = Json.Arr(resp.ContainsKey("availableProfiles") ? resp["availableProfiles"] : null);
                if (avail != null && avail.Count > 0) prof = Json.Obj(avail[0]);
            }
            if (prof == null) throw new Exception("该账号下没有可用的游戏角色");
            acc.Name = Json.Str(prof, "name") ?? "";
            acc.Uuid = Json.Str(prof, "id") ?? "";
            if (acc.AccessToken.Length == 0 || acc.Uuid.Length == 0) throw new Exception("认证服务器返回数据不完整");
            return acc;
        }

        // 启动前刷新令牌（accessToken 会过期）；失败返回 false 让 UI 引导重登
        public static bool Refresh(Account acc)
        {
            try
            {
                var req = new Dictionary<string, object>();
                req["accessToken"] = acc.AccessToken;
                req["clientToken"] = acc.ClientToken;
                req["requestUser"] = true;
                var resp = Post(acc.Server + "/authserver/refresh", req);
                string tok = Json.Str(resp, "accessToken");
                if (string.IsNullOrEmpty(tok)) return false;
                acc.AccessToken = tok;
                var prof = Json.Obj(resp.ContainsKey("selectedProfile") ? resp["selectedProfile"] : null);
                if (prof != null)
                {
                    acc.Name = Json.Str(prof, "name") ?? acc.Name;
                    acc.Uuid = Json.Str(prof, "id") ?? acc.Uuid;
                }
                AccountStore.Save();
                return true;
            }
            catch { return false; }
        }

        static Dictionary<string, object> Post(string url, Dictionary<string, object> body)
        {
            HttpWebResponse resp = null;
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "POST";
                req.ContentType = "application/json; charset=utf-8";
                req.Timeout = 30000;
                req.UserAgent = "HugeInsaneLauncher/3.0";
                byte[] payload = Encoding.UTF8.GetBytes(ser.Serialize(body));
                req.ContentLength = payload.Length;
                using (var s = req.GetRequestStream()) s.Write(payload, 0, payload.Length);
                resp = (HttpWebResponse)req.GetResponse();
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    return Json.Parse(sr.ReadToEnd());
            }
            catch (WebException we)
            {
                // 认证失败时服务器返回 4xx + JSON {error, errorMessage}，提炼成人话
                string msg = we.Message;
                var er = we.Response as HttpWebResponse;
                if (er != null)
                {
                    try
                    {
                        using (var sr = new StreamReader(er.GetResponseStream(), Encoding.UTF8))
                        {
                            var ej = Json.Parse(sr.ReadToEnd());
                            string m = Json.Str(ej, "errorMessage");
                            if (!string.IsNullOrEmpty(m)) msg = m;
                        }
                    }
                    catch { }
                    // 403 = 服务器核对后拒绝凭证。外置登录最经典的踩坑是用角色名当用户名，
                    // 附一句引导，免得用户对着「用户名或密码错误」干瞪眼
                    if (er.StatusCode == HttpStatusCode.Forbidden)
                        msg += "（提示：外置登录的用户名一般是注册邮箱，不是游戏角色名；新注册账号请先到邮箱完成验证）";
                }
                throw new Exception(msg);
            }
            finally
            {
                if (resp != null) resp.Dispose();
            }
        }
    }
}
