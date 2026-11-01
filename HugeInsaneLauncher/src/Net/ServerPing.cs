// HugeLauncher - 联机大厅支撑：Minecraft 服务器状态查询引擎（Server List Ping 协议）
// 流程：DNS SRV 解析（BCL 不支持 SRV，手写 UDP DNS 客户端）→ TCP 握手 → 收状态 JSON → 解析。
// 线程模型：PingAsync 绝不阻塞调用方；线程池执行 + SemaphoreSlim 限 8 并发；
// 回调经捕获的 SynchronizationContext 送回调用线程（UI 线程发起即回 UI 线程）。
// 会话级缓存：同一地址 30 秒内直接回上次结果。所有网络异常吞掉转为 Ok=false，不让坏服务器炸掉列表。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace HugeLauncher
{
    // 一次服务器状态查询的结果
    class ServerPingResult
    {
        public bool Ok;
        public string VersionName;   // version.name（如 "Paper 1.21.1"）
        public int Online;           // players.online
        public int Max;              // players.max
        public string Motd;          // description 摊平并剥掉颜色码后的纯文本
        public long LatencyMs;       // 从发起 TCP 连接到收完 JSON 的毫秒数
        public Image Favicon;        // 服务器图标（无则 null）
        public byte[] FaviconPng;    // 图标原始 PNG 字节（WPF 侧转 BitmapImage 用）
        public string Error;         // 失败原因（Ok=false 时）
    }

    static class ServerPing
    {
        const int DefaultPort = 25565;
        const int IoTimeoutMs = 4000;    // TCP 连接/收发超时
        const int DnsTimeoutMs = 3000;   // SRV 查询超时
        const int CacheTtlMs = 30000;    // 会话缓存有效期
        const int MaxConcurrent = 8;     // 同时 ping 上限

        // 剥离 MC 颜色/格式码（§0-§f §k-§o §r）
        static readonly Regex CodeStrip = new Regex("[§][0-9A-FK-ORa-fk-or]", RegexOptions.Compiled);
        static readonly SemaphoreSlim sem = new SemaphoreSlim(MaxConcurrent);

        class CacheEntry { public ServerPingResult Result; public DateTime At; }
        static readonly Dictionary<string, CacheEntry> cache = new Dictionary<string, CacheEntry>();

        class SrvRecord { public string Target; public int Port; }

        // ===== 对外接口 =====

        // 30 秒内的缓存命中则直接返回 true
        public static bool TryGetCached(string address, out ServerPingResult result)
        {
            lock (cache)
            {
                CacheEntry e;
                if (cache.TryGetValue(address, out e)
                    && (DateTime.Now - e.At).TotalMilliseconds < CacheTtlMs)
                {
                    result = e.Result;
                    return true;
                }
            }
            result = null;
            return false;
        }

        // 异步 ping：缓存命中直接回；否则后台线程查询。
        // 回调经调用时捕获的 SynchronizationContext 送回原线程（UI 线程调用 → 回调在 UI 线程）。
        public static void PingAsync(string address, Action<ServerPingResult> onDone)
        {
            var ctx = SynchronizationContext.Current;
            Action<ServerPingResult> deliver = delegate(ServerPingResult r)
            {
                if (onDone == null) return;
                if (ctx != null)
                    ctx.Post(delegate(object s)
                    {
                        try { onDone((ServerPingResult)s); } catch { }
                    }, r);
                else
                {
                    try { onDone(r); } catch { }
                }
            };
            ServerPingResult hit;
            if (TryGetCached(address, out hit)) { deliver(hit); return; }
            string addr = address;
            ThreadPool.QueueUserWorkItem(delegate
            {
                sem.Wait();
                ServerPingResult r;
                try { r = Query(addr); }
                finally { sem.Release(); }
                lock (cache) cache[addr] = new CacheEntry { Result = r, At = DateTime.Now };
                deliver(r);
            });
        }

        // ===== Ping 主流程（后台线程执行）=====

        static ServerPingResult Query(string address)
        {
            var r = new ServerPingResult();
            try
            {
                string host;
                int port;
                bool explicitPort;
                ParseAddress(address, out host, out port, out explicitPort);

                // 无显式端口时先试 SRV 记录；任何失败都静默回退 <host>:25565
                string connHost = host;
                int connPort = port;
                if (!explicitPort)
                {
                    var srv = ResolveSrv(host);
                    if (srv != null) { connHost = srv.Target; connPort = srv.Port; }
                }

                var watch = Stopwatch.StartNew();
                string jsonText;
                using (var tcp = new TcpClient())
                {
                    tcp.SendTimeout = IoTimeoutMs;
                    tcp.ReceiveTimeout = IoTimeoutMs;
                    tcp.NoDelay = true;
                    // BeginConnect + WaitOne：给连接加上可控超时（Connect() 本身会阻塞很久）
                    var ar = tcp.BeginConnect(connHost, connPort, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(IoTimeoutMs))
                    {
                        try { tcp.Close(); } catch { }
                        throw new IOException("连接超时");
                    }
                    tcp.EndConnect(ar);
                    var ns = tcp.GetStream();

                    // 握手包：PacketID 0x00 + 协议版本 767 + 服务器地址 + 端口（大端 ushort）+ NextState 1
                    // 地址必须用用户可见的原始 host（不是 SRV target），虚拟主机靠它路由
                    var body = new MemoryStream();
                    WriteVarInt(body, 0);
                    WriteVarInt(body, 767);
                    WriteString(body, host);
                    WriteU16(body, port);
                    WriteVarInt(body, 1);
                    SendPacket(ns, body);

                    // 状态请求包：长度 1 + PacketID 0x00
                    var req = new MemoryStream();
                    WriteVarInt(req, 0);
                    SendPacket(ns, req);

                    // 状态响应：VarInt 总长 + VarInt PacketID + VarInt JSON 长度 + JSON 字节
                    ReadVarInt(ns); // 总长（以 JSON 长度字段为准，这里只消耗）
                    ReadVarInt(ns); // PacketID（应为 0x00）
                    int jsonLen = ReadVarInt(ns);
                    if (jsonLen <= 0 || jsonLen > 2 * 1024 * 1024) throw new IOException("状态响应长度异常");
                    var jsonBytes = ReadExact(ns, jsonLen);
                    watch.Stop();
                    r.LatencyMs = watch.ElapsedMilliseconds;
                    jsonText = Encoding.UTF8.GetString(jsonBytes);
                }
                ParseStatus(jsonText, r);
                r.Ok = true;
            }
            catch (Exception ex)
            {
                r.Ok = false;
                r.Error = ex.Message;
            }
            return r;
        }

        // "host" 或 "host:port"；裸 IPv6 不展开（名录内没有）
        static void ParseAddress(string address, out string host, out int port, out bool explicitPort)
        {
            host = address.Trim();
            port = DefaultPort;
            explicitPort = false;
            int colon = host.LastIndexOf(':');
            if (colon > 0 && colon == host.IndexOf(':'))
            {
                int p;
                if (int.TryParse(host.Substring(colon + 1), out p) && p >= 1 && p <= 65535)
                {
                    port = p;
                    explicitPort = true;
                    host = host.Substring(0, colon);
                }
            }
        }

        // ===== 状态 JSON 解析 =====

        static void ParseStatus(string jsonText, ServerPingResult r)
        {
            var root = Json.Parse(jsonText);
            if (root == null) throw new FormatException("状态 JSON 解析失败");
            object v;
            if (root.TryGetValue("version", out v))
            {
                var ver = Json.Obj(v);
                if (ver != null) r.VersionName = Json.Str(ver, "name");
            }
            if (root.TryGetValue("players", out v))
            {
                var pl = Json.Obj(v);
                if (pl != null)
                {
                    r.Online = (int)Json.Long(pl, "online");
                    r.Max = (int)Json.Long(pl, "max");
                }
            }
            if (root.TryGetValue("description", out v) && v != null)
                r.Motd = StripCodes(FlattenChat(v)).Trim();
            if (root.TryGetValue("favicon", out v))
            {
                var fav = v as string;
                const string prefix = "data:image/png;base64,";
                if (fav != null && fav.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        byte[] png = Convert.FromBase64String(fav.Substring(prefix.Length));
                        // Image.FromStream 依赖流存活：拷成独立 Bitmap 后即可释放流
                        using (var ms = new MemoryStream(png))
                        using (var tmp = Image.FromStream(ms))
                        {
                            r.Favicon = new Bitmap(tmp);
                            r.FaviconPng = png;
                        }
                    }
                    catch { } // 图标坏了不影响整体状态
                }
            }
        }

        // description 可能是纯字符串，也可能是聊天组件字典：递归摊平 text + extra[]
        static string FlattenChat(object o)
        {
            var s = o as string;
            if (s != null) return s;
            var arr = o as System.Collections.ArrayList;
            if (arr != null)
            {
                var sb = new StringBuilder();
                foreach (var item in arr) sb.Append(FlattenChat(item));
                return sb.ToString();
            }
            var d = Json.Obj(o);
            if (d == null) return o == null ? "" : o.ToString();
            var sb2 = new StringBuilder();
            string t = Json.Str(d, "text");
            if (t != null) sb2.Append(t);
            object extra;
            if (d.TryGetValue("extra", out extra) && extra != null) sb2.Append(FlattenChat(extra));
            return sb2.ToString();
        }

        static string StripCodes(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            return CodeStrip.Replace(s, "");
        }

        // ===== MC 协议读写小助手 =====

        static void WriteVarInt(Stream s, int value)
        {
            uint v = (uint)value;
            while (true)
            {
                byte b = (byte)(v & 0x7F);
                v >>= 7;
                if (v != 0) s.WriteByte((byte)(b | 0x80));
                else { s.WriteByte(b); break; }
            }
        }

        static int ReadVarInt(Stream s)
        {
            int result = 0;
            for (int i = 0; i < 5; i++)
            {
                int b = s.ReadByte();
                if (b < 0) throw new EndOfStreamException();
                result |= (b & 0x7F) << (7 * i);
                if ((b & 0x80) == 0) return result;
            }
            throw new FormatException("VarInt 过长");
        }

        static void WriteU16(Stream s, int v)
        {
            s.WriteByte((byte)(v >> 8));
            s.WriteByte((byte)(v & 0xFF));
        }

        static void WriteString(Stream s, string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            WriteVarInt(s, bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        // 整包前缀 VarInt 长度后发出
        static void SendPacket(Stream s, MemoryStream body)
        {
            var bytes = body.ToArray();
            WriteVarInt(s, bytes.Length);
            s.Write(bytes, 0, bytes.Length);
        }

        static byte[] ReadExact(Stream s, int len)
        {
            var buf = new byte[len];
            int got = 0;
            while (got < len)
            {
                int n = s.Read(buf, got, len - got);
                if (n <= 0) throw new EndOfStreamException();
                got += n;
            }
            return buf;
        }

        // ===== DNS SRV 解析（BCL 不支持 SRV 查询，手写 UDP DNS 客户端）=====

        // 阿里 DNS（国内快）→ 失败回退腾讯 DNSPod
        static readonly string[] DnsServers = new[] { "223.5.5.5", "119.29.29.29" };

        // 查询 _minecraft._tcp.<host> 的 SRV 记录；任何失败返回 null（调用方回退 host:25565）
        static SrvRecord ResolveSrv(string host)
        {
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return null; // 裸 IP 无 SRV 可查
            string qname = "_minecraft._tcp." + host;
            foreach (var server in DnsServers)
            {
                try
                {
                    var rec = QuerySrvOnce(server, qname);
                    if (rec != null) return rec;
                }
                catch { }
            }
            return null;
        }

        static SrvRecord QuerySrvOnce(string dnsServer, string qname)
        {
            ushort id = (ushort)new Random().Next(0, 65536);
            byte[] query = BuildDnsQuery(id, qname);
            using (var sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                sock.SendTimeout = DnsTimeoutMs;
                sock.ReceiveTimeout = DnsTimeoutMs;
                sock.SendTo(query, new IPEndPoint(IPAddress.Parse(dnsServer), 53));
                var buf = new byte[2048];
                EndPoint from = new IPEndPoint(IPAddress.Any, 0);
                int n = sock.ReceiveFrom(buf, ref from);
                if (n < buf.Length)
                {
                    var exact = new byte[n];
                    Array.Copy(buf, exact, n);
                    buf = exact;
                }
                return ParseDnsResponse(buf, id);
            }
        }

        // 标准查询包：随机 ID、RD=1、QDCOUNT=1，QNAME 标签编码，QTYPE=33(SRV)，QCLASS=1(IN)
        static byte[] BuildDnsQuery(ushort id, string qname)
        {
            var ms = new MemoryStream();
            WriteU16(ms, id);
            WriteU16(ms, 0x0100);
            WriteU16(ms, 1); // QDCOUNT
            WriteU16(ms, 0); // ANCOUNT
            WriteU16(ms, 0); // NSCOUNT
            WriteU16(ms, 0); // ARCOUNT
            foreach (var label in qname.Split('.'))
            {
                var bytes = Encoding.ASCII.GetBytes(label);
                if (bytes.Length == 0 || bytes.Length > 63) throw new FormatException("DNS 标签非法");
                ms.WriteByte((byte)bytes.Length);
                ms.Write(bytes, 0, bytes.Length);
            }
            ms.WriteByte(0);  // 根标签结束 QNAME
            WriteU16(ms, 33); // QTYPE = SRV
            WriteU16(ms, 1);  // QCLASS = IN
            return ms.ToArray();
        }

        // 解析应答：跳过问题节，在应答节找 TYPE=33 记录，取 port(2 字节) 与 target（标签序列）
        static SrvRecord ParseDnsResponse(byte[] buf, ushort expectId)
        {
            if (buf.Length < 12) return null;
            if (((buf[0] << 8) | buf[1]) != expectId) return null;
            int flags = (buf[2] << 8) | buf[3];
            if ((flags & 0x8000) == 0) return null; // 不是应答
            if ((flags & 0x000F) != 0) return null; // RCODE 非 0
            int qd = (buf[4] << 8) | buf[5];
            int an = (buf[6] << 8) | buf[7];
            int pos = 12;
            for (int i = 0; i < qd; i++)
            {
                ReadDnsName(buf, ref pos); // 跳过 QNAME
                pos += 4;                  // QTYPE + QCLASS
            }
            for (int i = 0; i < an; i++)
            {
                ReadDnsName(buf, ref pos); // NAME（常为 0xC0 压缩指针）
                if (pos + 10 > buf.Length) return null;
                int type = (buf[pos] << 8) | buf[pos + 1];
                int rdlen = (buf[pos + 8] << 8) | buf[pos + 9];
                pos += 10;
                if (pos + rdlen > buf.Length) return null;
                if (type == 33 && rdlen >= 3)
                {
                    int port = (buf[pos + 4] << 8) | buf[pos + 5];
                    int tp = pos + 6;
                    string target = ReadDnsName(buf, ref tp);
                    if (target.Length > 0)
                        return new SrvRecord { Target = target, Port = port };
                }
                pos += rdlen;
            }
            return null;
        }

        // 域名标签解码（含 0xC0 压缩指针跳转；pos 推进到名字在原位置的结束处）
        static string ReadDnsName(byte[] buf, ref int pos)
        {
            var sb = new StringBuilder();
            int p = pos;
            int resume = -1;
            int hops = 0;
            while (p < buf.Length)
            {
                int len = buf[p];
                if (len == 0) { p++; break; }
                if ((len & 0xC0) == 0xC0)
                {
                    if (p + 1 >= buf.Length) { p = buf.Length; break; }
                    if (resume < 0) resume = p + 2;
                    p = ((len & 0x3F) << 8) | buf[p + 1];
                    if (++hops > 32) break; // 防御恶意指针环
                    continue;
                }
                p++;
                if (p + len > buf.Length) { p = buf.Length; break; }
                if (sb.Length > 0) sb.Append('.');
                sb.Append(Encoding.ASCII.GetString(buf, p, len));
                p += len;
            }
            pos = resume >= 0 ? resume : p;
            return sb.ToString();
        }
    }
}
