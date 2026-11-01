// HugeLauncher - Huge 隧道：自研内网穿透（官网 Cloudflare WebSocket 中继）
// 腐竹端：注册 6 位房间码，把中继来的会话桥接到本地 MC 服务端（127.0.0.1:port）。
// 玩家端：连房间 → 本机开监听（默认 25565），把 MC 客户端的流量经中继送给腐竹；
//         同时发假 LAN 广播（224.0.2.60:4445），让房间出现在 MC 多人游戏局域网列表里。
// 网络底座与 AiClient 同宗：IPv4 优先 + SslStream 手写握手（绕开被运营商黑洞的 IPv6）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HugeLauncher
{
    // ===== 迷你 WebSocket 客户端 =====
    // 只实现客户端必需面：文本/二进制帧、ping/pong、close、分片拼装。发送必带掩码（RFC6455）。
    class WsClient
    {
        public event Action<string> Text;      // 收到完整文本帧
        public event Action<byte[]> Binary;    // 收到完整二进制帧
        public event Action<string> Closed;    // 连接关闭（原因说明；正常/异常都会触发一次）

        Stream io;
        TcpClient tcp;
        Thread recvThread, kaThread;
        volatile bool open;
        bool closeRaised;
        readonly object sendLock = new object();
        readonly Random rng = new Random();

        public bool IsOpen { get { return open; } }

        // secure=true 走 TLS（生产）；false 明文 ws（仅本地联调）
        // preResolved：DoH 预解析的 IP（workers.dev 在国内被 DNS 污染，系统解析不可信）
        public void Connect(string host, int port, string path, bool secure, IPAddress[] preResolved)
        {
            var addrs = preResolved != null && preResolved.Length > 0 ? preResolved
                : secure ? Dns.GetHostAddresses(host) : new[] { IPAddress.Parse(host) };
            Array.Sort(addrs, delegate(IPAddress a, IPAddress b)
            {
                bool av4 = a.AddressFamily == AddressFamily.InterNetwork;
                bool bv4 = b.AddressFamily == AddressFamily.InterNetwork;
                return av4 == bv4 ? 0 : (av4 ? -1 : 1);
            });
            Exception lastErr = null;
            foreach (var ip in addrs)
            {
                try { ConnectOne(ip, host, port, path, secure); return; }
                catch (Exception ex) { lastErr = ex; CloseAll(); }
            }
            throw lastErr ?? new Exception("无法连接 " + host);
        }

        void ConnectOne(IPAddress ip, string host, int port, string path, bool secure)
        {
            tcp = new TcpClient();
            var ar = tcp.BeginConnect(ip, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(8000)) throw new Exception("连接 " + ip + " 超时");
            tcp.EndConnect(ar);
            tcp.NoDelay = true;
            if (secure)
            {
                var ssl = new SslStream(tcp.GetStream(), false);
                ssl.AuthenticateAsClient(host, null,
                    (System.Security.Authentication.SslProtocols)3072 /*Tls12*/, false);
                io = ssl;
            }
            else io = tcp.GetStream();

            byte[] keyBytes = new byte[16];
            rng.NextBytes(keyBytes);
            string key = Convert.ToBase64String(keyBytes);
            var sb = new StringBuilder();
            sb.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
            sb.Append("Host: ").Append(host).Append("\r\n");
            sb.Append("Upgrade: websocket\r\nConnection: Upgrade\r\n");
            sb.Append("Sec-WebSocket-Key: ").Append(key).Append("\r\n");
            sb.Append("Sec-WebSocket-Version: 13\r\n");
            sb.Append("User-Agent: HugeInsaneLauncher/3.0\r\n\r\n");
            var head = Encoding.ASCII.GetBytes(sb.ToString());
            io.Write(head, 0, head.Length);
            io.Flush();
            string statusLine = ReadLine(io);
            if (statusLine == null || statusLine.IndexOf(" 101") < 0)
                throw new Exception("WebSocket 握手失败: " + (statusLine ?? "无响应"));
            string expect = Convert.ToBase64String(SHA1.Create().ComputeHash(
                Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            bool okAccept = false;
            int headBytes = 0;
            string line;
            while ((line = ReadLine(io)) != null && line.Length > 0)
            {
                headBytes += line.Length;
                if (headBytes > 16384) throw new Exception("握手响应头过大");
                if (line.StartsWith("Sec-WebSocket-Accept:", StringComparison.OrdinalIgnoreCase)
                    && line.Substring(21).Trim() == expect) okAccept = true;
            }
            if (!okAccept) throw new Exception("WebSocket 握手校验失败");

            open = true;
            closeRaised = false;
            recvThread = new Thread(RecvLoop);
            recvThread.IsBackground = true;
            recvThread.Start();
            kaThread = new Thread(KaLoop);
            kaThread.IsBackground = true;
            kaThread.Start();
        }

        static string ReadLine(Stream s)
        {
            var buf = new List<byte>();
            int b;
            while ((b = s.ReadByte()) >= 0)
            {
                if (b == '\n') break;
                if (b != '\r') buf.Add((byte)b);
                if (buf.Count > 8192) throw new Exception("行过长");
            }
            if (b < 0 && buf.Count == 0) return null;
            return Encoding.ASCII.GetString(buf.ToArray());
        }

        void RecvLoop()
        {
            var frag = new MemoryStream();
            int fragOp = 0;
            string closeReason = "连接已断开";
            try
            {
                var hdr = new byte[2];
                while (open)
                {
                    ReadExact(io, hdr, 0, 2);
                    bool fin = (hdr[0] & 0x80) != 0;
                    int op = hdr[0] & 0x0F;
                    bool masked = (hdr[1] & 0x80) != 0;
                    long len = hdr[1] & 0x7F;
                    if (len == 126) { ReadExact(io, hdr, 0, 2); len = (hdr[0] << 8) | hdr[1]; }
                    else if (len == 127)
                    {
                        var b8 = new byte[8];
                        ReadExact(io, b8, 0, 8);
                        len = 0;
                        for (int i = 0; i < 8; i++) len = (len << 8) | b8[i];
                    }
                    if (len > 8 * 1024 * 1024) throw new Exception("帧过大");
                    byte[] mask = null;
                    if (masked) { mask = new byte[4]; ReadExact(io, mask, 0, 4); }
                    var payload = new byte[(int)len];
                    if (len > 0) ReadExact(io, payload, 0, (int)len);
                    if (mask != null)
                        for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];

                    if (op == 0x9) { SendFrame(0xA, payload); continue; }  // ping → pong
                    if (op == 0xA) continue;                               // pong
                    if (op == 0x8) { closeReason = "远端关闭了连接"; break; } // close
                    if (op == 0x1 || op == 0x2)
                    {
                        if (fin) Dispatch(op, payload);
                        else { fragOp = op; frag.SetLength(0); frag.Write(payload, 0, payload.Length); }
                    }
                    else if (op == 0x0 && fragOp != 0)                     // continuation
                    {
                        frag.Write(payload, 0, payload.Length);
                        if (fin) { Dispatch(fragOp, frag.ToArray()); fragOp = 0; frag.SetLength(0); }
                    }
                }
            }
            catch (Exception ex) { if (open) closeReason = ex.Message; }
            RaiseClosed(closeReason);
        }

        void Dispatch(int op, byte[] payload)
        {
            try
            {
                if (op == 0x1) { var h = Text; if (h != null) h(Encoding.UTF8.GetString(payload)); }
                else { var h = Binary; if (h != null) h(payload); }
            }
            catch (Exception ex) { HilLog.Write("隧道帧处理异常: " + ex.Message); }
        }

        void KaLoop()
        {
            while (open)
            {
                Thread.Sleep(25000);
                if (!open) break;
                try { SendText("{\"type\":\"ka\"}"); }
                catch { break; }
            }
        }

        static void ReadExact(Stream s, byte[] buf, int off, int n)
        {
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) throw new Exception("连接提前断开");
                off += r;
            }
        }

        public void SendText(string text) { SendFrame(0x1, Encoding.UTF8.GetBytes(text)); }
        public void SendBinary(byte[] data) { SendFrame(0x2, data); }

        void SendFrame(int op, byte[] payload)
        {
            lock (sendLock)
            {
                if (!open) throw new Exception("连接未打开");
                var ms = new MemoryStream();
                ms.WriteByte((byte)(0x80 | op));
                int len = payload.Length;
                if (len < 126) ms.WriteByte((byte)(0x80 | len));
                else if (len <= 0xFFFF)
                {
                    ms.WriteByte(0x80 | 126);
                    ms.WriteByte((byte)(len >> 8)); ms.WriteByte((byte)len);
                }
                else
                {
                    ms.WriteByte(0x80 | 127);
                    for (int i = 7; i >= 0; i--) ms.WriteByte((byte)((len >> (i * 8)) & 0xFF));
                }
                var mask = new byte[4];
                rng.NextBytes(mask);
                ms.Write(mask, 0, 4);
                for (int i = 0; i < payload.Length; i++) ms.WriteByte((byte)(payload[i] ^ mask[i & 3]));
                var frame = ms.ToArray();
                io.Write(frame, 0, frame.Length);
                io.Flush();
            }
        }

        public void Close()
        {
            if (!open) { RaiseClosed("连接已关闭"); return; }
            open = false;
            try { var f = BuildCloseFrame(); io.Write(f, 0, f.Length); io.Flush(); } catch { }
            CloseAll();
            RaiseClosed("连接已关闭");
        }

        byte[] BuildCloseFrame()
        {
            var f = new byte[6];
            f[0] = 0x88; f[1] = 0x80;
            rng.NextBytes(f);           // 填充掩码位
            f[0] = 0x88; f[1] = 0x80;   // 写回被随机数覆盖的头两字节
            return f;                   // 空载荷 close：掩码之后的 0 字节内容
        }

        void CloseAll()
        {
            open = false;
            try { if (io != null) io.Close(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
        }

        void RaiseClosed(string reason)
        {
            lock (sendLock)
            {
                if (closeRaised) return;
                closeRaised = true;
            }
            open = false;
            CloseAll();
            var h = Closed;
            if (h != null)
                try { h(reason); } catch { }
        }
    }

    // ===== 直连链路：一条打通的 TCP 承载全部会话 =====
    // 帧格式：[u32 帧长（含2字节会话号）][u16 会话][载荷]；会话 0xFFFF = 控制 JSON（UTF-8）
    class DirectLink
    {
        public event Action<int, byte[]> Data;
        public event Action<string> Control;
        public event Action Closed;

        Socket sock;
        Thread rx;
        volatile bool open;
        readonly object sendLock = new object();
        public bool IsOpen { get { return open; } }

        public static DirectLink Wrap(Socket s)
        {
            s.NoDelay = true;
            s.ReceiveTimeout = 0;   // 打洞探测时设过超时，接管前复位成无限
            var l = new DirectLink();
            l.sock = s;
            l.open = true;
            l.rx = new Thread(new ThreadStart(l.Loop));
            l.rx.IsBackground = true;
            l.rx.Start();
            return l;
        }

        void Loop()
        {
            var hdr = new byte[4];
            try
            {
                while (open)
                {
                    ReadExact(hdr, 0, 4);
                    int len = (hdr[0] << 24) | (hdr[1] << 16) | (hdr[2] << 8) | hdr[3];
                    if (len < 2 || len > 8 * 1024 * 1024) throw new Exception("帧长度异常");
                    var buf = new byte[len];
                    ReadExact(buf, 0, len);
                    int s = (buf[0] << 8) | buf[1];
                    if (s == 0xFFFF)
                    {
                        var h = Control;
                        if (h != null) h(Encoding.UTF8.GetString(buf, 2, len - 2));
                    }
                    else
                    {
                        var h = Data;
                        if (h != null)
                        {
                            var payload = new byte[len - 2];
                            Buffer.BlockCopy(buf, 2, payload, 0, payload.Length);
                            h(s, payload);
                        }
                    }
                }
            }
            catch { }
            open = false;
            try { sock.Close(); } catch { }
            var c = Closed;
            if (c != null) try { c(); } catch { }
        }

        void ReadExact(byte[] b, int off, int n)
        {
            while (off < n)
            {
                int r = sock.Receive(b, off, n - off, SocketFlags.None);
                if (r <= 0) throw new Exception("eof");
                off += r;
            }
        }

        public void Send(int session, byte[] payload)
        {
            lock (sendLock)
            {
                if (!open) throw new Exception("链路已关闭");
                var frame = new byte[6 + payload.Length];
                int len = 2 + payload.Length;
                frame[0] = (byte)(len >> 24); frame[1] = (byte)(len >> 16);
                frame[2] = (byte)(len >> 8); frame[3] = (byte)len;
                frame[4] = (byte)(session >> 8); frame[5] = (byte)session;
                Buffer.BlockCopy(payload, 0, frame, 6, payload.Length);
                sock.Send(frame);
            }
        }

        public void SendControl(string json) { Send(0xFFFF, Encoding.UTF8.GetBytes(json)); }

        public void Close() { open = false; try { sock.Close(); } catch { } }
    }

    // ===== TCP 打洞器 =====
    // 双方在各自端口上既监听又外发（SO_REUSEADDR 复用绑定，外发绑定与监听同一端口，
    // 这样锥型 NAT 的端口映射才能和监听器对上）。链路认定规则（防双连接精神分裂）：
    // 只有「腐竹侧监听器接到的、且通过魔数验证的连接」才算数——玩家的外发连通后报
    // 家门 HILDJ，腐竹的监听器回 HILDH 认领；玩家收到认领的那条外发连接即链路另一端。
    // 腐竹的外发、玩家的监听只负责把各自 NAT 的洞撞开，连上了也立刻关掉。
    class TcpPuncher
    {
        public event Action<DirectLink> Linked;

        readonly bool isHost;
        readonly string peerIp;
        readonly int peerPort, listenPort;
        readonly object lockObj = new object();
        readonly List<Socket> pending = new List<Socket>();
        TcpListener listener;
        volatile bool stopped, linked;

        // 选一个空闲端口（40000-60000 随机，最多试 20 次）
        public static int PickPort()
        {
            var rng = new Random(Guid.NewGuid().GetHashCode());
            for (int i = 0; i < 20; i++)
            {
                int p = 40000 + rng.Next(20000);
                try
                {
                    var l = new TcpListener(IPAddress.Any, p);
                    l.Start();
                    l.Stop();
                    return p;
                }
                catch { }
            }
            return 0;
        }

        public TcpPuncher(bool hostSide, string peerIp, int peerPort, int listenPort)
        {
            isHost = hostSide;
            this.peerIp = peerIp;
            this.peerPort = peerPort;
            this.listenPort = listenPort;
        }

        public void Start()
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, listenPort);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start(50);
            }
            catch (Exception ex) { HilLog.Write("打洞监听失败: " + ex.Message); return; }
            var at = new Thread(new ThreadStart(AcceptLoop));
            at.IsBackground = true;
            at.Start();
            var pt = new Thread(new ThreadStart(PunchLoop));
            pt.IsBackground = true;
            pt.Start();
        }

        void AcceptLoop()
        {
            while (!stopped)
            {
                Socket s;
                try { s = listener.AcceptSocket(); }
                catch { break; }
                if (isHost)
                {
                    var t = new Thread(new ThreadStart(delegate { HostVerify(s); }));
                    t.IsBackground = true;
                    t.Start();
                }
                else { try { s.Close(); } catch { } }   // 玩家的监听只用来接腐竹的撞洞包
            }
        }

        // 腐竹侧：验证来者是不是玩家的探测（防端口扫描/误连被当成链路）
        void HostVerify(Socket s)
        {
            try
            {
                s.ReceiveTimeout = 1500;
                var buf = new byte[5];
                int off = 0;
                while (off < 5)
                {
                    int r = s.Receive(buf, off, 5 - off, SocketFlags.None);
                    if (r <= 0) throw new Exception("eof");
                    off += r;
                }
                if (Encoding.ASCII.GetString(buf) != "HILDJ") { s.Close(); return; }
                s.Send(Encoding.ASCII.GetBytes("HILDH"));
                Adopt(s);
            }
            catch { try { s.Close(); } catch { } }
        }

        void PunchLoop()
        {
            var deadline = DateTime.Now.AddSeconds(8);
            while (!stopped && !linked && DateTime.Now < deadline)
            {
                Socket s = null;
                try
                {
                    s = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                    s.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                    s.Bind(new IPEndPoint(IPAddress.Any, listenPort));
                    s.NoDelay = true;
                    lock (lockObj) pending.Add(s);
                    var ar = s.BeginConnect(IPAddress.Parse(peerIp), peerPort, null, null);
                    if (ar.AsyncWaitHandle.WaitOne(2000))
                    {
                        s.EndConnect(ar);
                        if (isHost) ClosePending(s);   // 腐竹外发只为开洞
                        else JoinerProbe(s);            // 玩家外发成功 → 报家门等认领
                    }
                    else ClosePending(s);
                }
                catch { ClosePending(s); }
                int wait = 0;
                while (!stopped && !linked && wait < 150) { Thread.Sleep(25); wait += 25; }
            }
        }

        // 玩家侧：外发连通 → 报家门，等腐竹的监听器认领（收到 HILDH 的那条才是链路）
        void JoinerProbe(Socket s)
        {
            if (linked) { ClosePending(s); return; }
            var t = new Thread(new ThreadStart(delegate
            {
                try
                {
                    s.Send(Encoding.ASCII.GetBytes("HILDJ"));
                    s.ReceiveTimeout = 1500;
                    var buf = new byte[5];
                    int off = 0;
                    while (off < 5)
                    {
                        int r = s.Receive(buf, off, 5 - off, SocketFlags.None);
                        if (r <= 0) throw new Exception("eof");
                        off += r;
                    }
                    if (Encoding.ASCII.GetString(buf) == "HILDH") Adopt(s);
                    else ClosePending(s);
                }
                catch { ClosePending(s); }
            }));
            t.IsBackground = true;
            t.Start();
        }

        void Adopt(Socket s)
        {
            lock (lockObj)
            {
                if (linked || stopped) { try { s.Close(); } catch { } return; }
                linked = true;
                pending.Remove(s);
            }
            var l = DirectLink.Wrap(s);
            HilLog.Write("打洞成功: " + (isHost ? "腐竹侧" : "玩家侧") + " 直连 " + peerIp + ":" + peerPort);
            var h = Linked;
            if (h != null) try { h(l); } catch { }
            Stop();
        }

        void ClosePending(Socket s)
        {
            if (s == null) return;
            lock (lockObj) pending.Remove(s);
            try { s.Close(); } catch { }
        }

        public void Stop()
        {
            stopped = true;
            try { if (listener != null) listener.Stop(); } catch { }
            lock (lockObj)
            {
                foreach (var s in pending) try { s.Close(); } catch { }
                pending.Clear();
            }
        }
    }

    // ===== 隧道管理器：单例。腐竹模式与玩家模式互不影响，可同时进行 =====
    static class TunnelManager
    {
        public const string RelayHost = "hugeinsane.pages.dev";
        const string RelayPath = "/api/relay";
        internal static string RelayOverride;   // "127.0.0.1:端口" 明文 ws（仅本地联调）

        // 事件都在后台线程触发，UI 侧需 Dispatcher.BeginInvoke 归队
        public static event Action<string> HostStatus;          // 腐竹侧状态文本
        public static event Action<string, string> HostPeer;    // ("join"/"leave", 玩家编号)
        public static event Action<string> JoinStatus;          // 玩家侧状态文本（错误也走这里）
        public static event Action<int> JoinReady;              // 本地监听就绪（端口号）

        // 会话记录：Tcp = 本地连接；Link = 数据走的通道（null = 中继 WS，否则打洞直连）。
        // 通道在会话创建时按来源钉死，避免中继/直连间切换造成字节流乱序。
        class HostSession { public TcpClient Tcp; public DirectLink Link; }
        class JoinSession { public TcpClient Tcp; public DirectLink Link; }

        // ---------- 腐竹侧 ----------
        static WsClient hostWs;
        static string hostRoom, hostInstance;
        static int hostPort;
        static bool hostActive;
        static readonly Dictionary<long, HostSession> hostSessions = new Dictionary<long, HostSession>();
        static readonly object hostLock = new object();
        // 打洞状态（每玩家一份）
        static readonly Dictionary<int, DirectLink> hostLinks = new Dictionary<int, DirectLink>();
        static readonly Dictionary<int, string> hostPeerIps = new Dictionary<int, string>();
        static readonly Dictionary<int, int> hostListenPorts = new Dictionary<int, int>();
        static readonly Dictionary<int, TcpPuncher> hostPunchers = new Dictionary<int, TcpPuncher>();

        // ---------- 玩家侧 ----------
        static WsClient joinWs;
        static TcpListener joinListener;
        static int joinLocalPort, joinNextSession = 1;
        static string joinCode;
        static bool joinActive;
        static readonly Dictionary<int, JoinSession> joinSessions = new Dictionary<int, JoinSession>();
        static readonly object joinLock = new object();
        static Thread lanThread;
        static volatile bool lanOn;
        // 打洞状态
        static DirectLink joinLink;
        static TcpPuncher joinPuncher;
        static string joinHostIp;
        static int joinListenPort;
        static bool joinReadyFired;
        static string joinLanIp;   // 假 LAN 广播的源 IP（本机局域网地址），兜底显示用

        // 测试钩子（tunnel_test 反射断言用）
        internal static int TestHostLinkCount { get { return hostLinks.Count; } }
        internal static bool TestJoinDirect { get { return joinLink != null && joinLink.IsOpen; } }

        public static bool IsHostActive { get { return hostActive; } }
        public static bool IsJoinActive { get { return joinActive; } }
        public static string HostRoom { get { return hostRoom; } }
        public static string HostInstance { get { return hostInstance; } }   // "lan"=局域网联机房间
        public static int JoinLocalPort { get { return joinLocalPort; } }
        // 手动兜底地址：与假 LAN 广播同款的本机 IP + 永远显式端口。
        // （用 127.0.0.1 且无端口时会撞本机默认 25565 上的其他 LAN 世界 → 无效会话）
        public static string JoinDisplayAddress
        {
            get
            {
                if (joinLocalPort <= 0) return null;
                return (joinLanIp ?? "127.0.0.1") + ":" + joinLocalPort;
            }
        }

        // 算出组播发送会选的本机源 IP（UDP connect 不发包，只做路由选择）
        static string DetectLanIp()
        {
            try
            {
                using (var udp = new UdpClient())
                {
                    udp.Connect(IPAddress.Parse("224.0.2.60"), 4445);
                    var ip = ((IPEndPoint)udp.Client.LocalEndPoint).Address;
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
                }
            }
            catch { }
            // 兜底：第一张非回环 IPv4 网卡
            try
            {
                foreach (var ip in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        return ip.ToString();
            }
            catch { }
            return null;
        }
        public static string JoinCode { get { return joinCode; } }

        const string CodeChars = "ABCDEFGHJKMNPQRSTUVWXYZ23456789"; // 去掉 0/O/1/I/L

        static string GenerateCode()
        {
            var rng = new Random(Guid.NewGuid().GetHashCode());
            var sb = new StringBuilder(6);
            for (int i = 0; i < 6; i++) sb.Append(CodeChars[rng.Next(CodeChars.Length)]);
            return sb.ToString();
        }

        static void DialInto(WsClient ws, string room, string role)
        {
            string path = RelayPath + "?room=" + room + "&role=" + role;
            if (RelayOverride != null)
            {
                var parts = RelayOverride.Split(':');
                ws.Connect(parts[0], int.Parse(parts[1]), path, false, null);
            }
            else ws.Connect(RelayHost, 443, path, true, DohResolve(RelayHost));
        }

        // workers.dev 在国内被 DNS 污染（解析到 Facebook/Twitter 的假 IP）。
        // 走阿里 DoH（HTTPS 加密查询，链路上插不进假答案）拿真实 IP；失败退回系统 DNS。
        static IPAddress[] DohResolve(string host)
        {
            try
            {
                var tcp = new TcpClient();
                var ar = tcp.BeginConnect(IPAddress.Parse("223.5.5.5"), 443, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(5000)) throw new Exception("DoH 连接超时");
                tcp.EndConnect(ar);
                var ssl = new SslStream(tcp.GetStream(), false);
                ssl.AuthenticateAsClient("dns.alidns.com", null,
                    (System.Security.Authentication.SslProtocols)3072, false);
                string req = "GET /resolve?name=" + host + "&type=A HTTP/1.1\r\n"
                    + "Host: dns.alidns.com\r\nUser-Agent: HugeInsaneLauncher/3.0\r\nConnection: close\r\n\r\n";
                var rb = Encoding.ASCII.GetBytes(req);
                ssl.Write(rb, 0, rb.Length);
                ssl.Flush();
                var ms = new MemoryStream();
                var buf = new byte[8192];
                ssl.ReadTimeout = 8000;
                for (;;)
                {
                    int r;
                    try { r = ssl.Read(buf, 0, buf.Length); } catch { break; }
                    if (r <= 0) break;
                    ms.Write(buf, 0, r);
                }
                try { ssl.Close(); tcp.Close(); } catch { }
                string raw = Encoding.UTF8.GetString(ms.ToArray());
                int bodyStart = raw.IndexOf("\r\n\r\n");
                if (bodyStart < 0) return null;
                string body = raw.Substring(bodyStart + 4);
                // chunked 传输时剥掉块头（简易处理：找到第一个 { 截到最后一个 }）
                int js = body.IndexOf('{'), je = body.LastIndexOf('}');
                if (js < 0 || je <= js) return null;
                var j = Json.Parse(body.Substring(js, je - js + 1));
                var ans = Json.Arr(j["Answer"]);
                if (ans == null) return null;
                var ips = new List<IPAddress>();
                foreach (var a in ans)
                {
                    var o = Json.Obj(a);
                    if (o == null) continue;
                    if (Json.Long(o, "type") != 1) continue;   // 只要 A 记录
                    IPAddress ip;
                    string data = Json.Str(o, "data");
                    if (data != null && IPAddress.TryParse(data, out ip)
                        && ip.AddressFamily == AddressFamily.InterNetwork)
                        ips.Add(ip);
                }
                HilLog.Write("DoH 解析 " + host + " → " + ips.Count + " 个 IPv4");
                return ips.Count > 0 ? ips.ToArray() : null;
            }
            catch (Exception ex)
            {
                HilLog.Write("DoH 解析失败（退回系统 DNS）: " + ex.Message);
                return null;
            }
        }

        // ================= 腐竹端 =================

        public static void StartHost(string instance, int port)
        {
            StopHost();
            hostInstance = instance;
            hostPort = port;
            hostRoom = null;
            hostActive = true;
            var t = new Thread(HostConnect);
            t.IsBackground = true;
            t.Start();
        }

        static void HostConnect()
        {
            RaiseHostStatus("正在连接隧道服务器…");
            for (int attempt = 0; attempt < 3 && hostActive; attempt++)
            {
                string room = hostRoom ?? GenerateCode();
                WsClient ws = null;
                try
                {
                    ws = new WsClient();
                    // 先挂事件再握手：中继端握手成功立刻推 ok/控制帧，挂晚了会丢
                    ws.Text += HostOnText;
                    ws.Binary += HostOnBinary;
                    ws.Closed += HostOnClosed;
                    DialInto(ws, room, "host");
                    hostWs = ws;
                    hostRoom = room;
                    RaiseHostStatus("ROOM:" + room);
                    HilLog.Write("Huge隧道 腐竹端上线: 房间 " + room + " → 127.0.0.1:" + hostPort);
                    return;
                }
                catch (Exception ex)
                {
                    HilLog.Write("Huge隧道 连接失败(" + (attempt + 1) + "/3): " + ex.Message);
                    try { if (ws != null) ws.Close(); } catch { }
                    if (attempt < 2) Thread.Sleep(2000);
                }
            }
            hostActive = false;
            RaiseHostStatus("ERR:连不上隧道服务器，请检查网络");
        }

        static void HostOnText(string text)
        {
            Dictionary<string, object> j;
            try { j = Json.Parse(text); } catch { return; }
            string type = Json.Str(j, "type");
            if (type == "close")
            {
                long key = HostKey((int)Json.Long(j, "j"), (int)Json.Long(j, "s"));
                CloseHostSession(key, false);
            }
            else if (type == "peer")
            {
                string action = Json.Str(j, "action");
                int jid = (int)Json.Long(j, "j");
                if (action == "join")
                {
                    // 记下玩家公网 IP，选个端口经中继告诉它（打洞邀约）
                    string ip = Json.Str(j, "ip");
                    if (!string.IsNullOrEmpty(ip))
                    {
                        hostPeerIps[jid] = ip;
                        int port = TcpPuncher.PickPort();
                        if (port > 0)
                        {
                            hostListenPorts[jid] = port;
                            try
                            {
                                var w = hostWs;
                                if (w != null && w.IsOpen)
                                    w.SendText("{\"type\":\"sig\",\"j\":" + jid + ",\"p\":" + port + "}");
                            }
                            catch { }
                        }
                    }
                }
                else if (action == "leave")
                {
                    TcpPuncher p;
                    if (hostPunchers.TryGetValue(jid, out p)) { hostPunchers.Remove(jid); p.Stop(); }
                    DirectLink l;
                    lock (hostLock) if (hostLinks.TryGetValue(jid, out l)) { hostLinks.Remove(jid); }
                    if (l != null) try { l.Close(); } catch { }
                    hostPeerIps.Remove(jid);
                    hostListenPorts.Remove(jid);
                }
                var h = HostPeer;
                if (h != null) h(action, jid.ToString());
            }
            else if (type == "sig")
            {
                // 收到玩家的打洞端口 → 启动打洞器（我方端口已在 peer join 时选定）
                int jid = (int)Json.Long(j, "j");
                int p = (int)Json.Long(j, "p");
                string ip;
                int myPort;
                if (p > 0 && hostPeerIps.TryGetValue(jid, out ip)
                    && hostListenPorts.TryGetValue(jid, out myPort) && !hostPunchers.ContainsKey(jid))
                {
                    var punch = new TcpPuncher(true, ip, p, myPort);
                    int captured = jid;
                    punch.Linked += delegate(DirectLink l) { HostLinked(captured, l); };
                    hostPunchers[jid] = punch;
                    punch.Start();
                }
            }
            else if (type == "error")
            {
                RaiseHostStatus("ERR:" + Json.Str(j, "message"));
            }
        }

        // 与某玩家的打洞成功：登记直连链路并接管数据/控制帧
        static void HostLinked(int jid, DirectLink l)
        {
            lock (hostLock) hostLinks[jid] = l;
            l.Data += delegate(int s, byte[] payload) { HostOnData(jid, s, payload, 0, payload.Length, l); };
            l.Control += delegate(string text)
            {
                try
                {
                    var c = Json.Parse(text);
                    if (Json.Str(c, "type") == "close")
                        CloseHostSession(HostKey(jid, (int)Json.Long(c, "s")), false);
                }
                catch { }
            };
            l.Closed += delegate
            {
                lock (hostLock)
                {
                    DirectLink cur;
                    if (hostLinks.TryGetValue(jid, out cur) && cur == l) hostLinks.Remove(jid);
                    // 这条链路上的会话随链路终止（玩家 MC 会掉线；重进即走中继）
                    var dead = new List<long>();
                    foreach (var kv in hostSessions) if (kv.Value.Link == l) dead.Add(kv.Key);
                    foreach (var k in dead)
                    {
                        try { hostSessions[k].Tcp.Close(); } catch { }
                        hostSessions.Remove(k);
                    }
                }
                if (hostActive) RaiseHostStatus("玩家" + jid + " 的直连中断，后续走中继");
            };
            RaiseHostStatus("玩家" + jid + " 打洞成功，走直连");
            var h = HostPeer;
            if (h != null) h("direct", jid.ToString());
        }

        static void HostOnBinary(byte[] frame)
        {
            if (frame.Length < 4) return;
            int j = (frame[0] << 8) | frame[1];
            int s = (frame[2] << 8) | frame[3];
            HostOnData(j, s, frame, 4, frame.Length - 4, null);
        }

        // 中继/直连共用：数据写入本地 MC 服务端（新会话按来源通道钉死）
        static void HostOnData(int j, int s, byte[] buf, int off, int len, DirectLink via)
        {
            long key = HostKey(j, s);
            HostSession sess;
            lock (hostLock)
            {
                if (!hostSessions.TryGetValue(key, out sess))
                {
                    try
                    {
                        var c = new TcpClient();
                        c.Connect(IPAddress.Loopback, hostPort);
                        c.NoDelay = true;
                        sess = new HostSession { Tcp = c, Link = via };
                        hostSessions[key] = sess;
                        var captured = sess;
                        var t = new Thread(new ThreadStart(delegate { HostPump(key, j, s, captured); }));
                        t.IsBackground = true;
                        t.Start();
                        HilLog.Write("Huge隧道 新会话: 玩家" + j + " 会话" + s + (via != null ? "（直连）" : ""));
                    }
                    catch (Exception ex)
                    {
                        HilLog.Write("Huge隧道 连本地服务端失败: " + ex.Message);
                        SendHostClose(j, s);
                        return;
                    }
                }
            }
            if (len > 0)
            {
                try { sess.Tcp.GetStream().Write(buf, off, len); }
                catch { CloseHostSession(key, true); }
            }
        }

        // 本地服务端 → 中继/直连（按会话钉死的通道）
        static void HostPump(long key, int j, int s, HostSession sess)
        {
            var buf = new byte[16384];
            try
            {
                for (;;)
                {
                    int r = sess.Tcp.GetStream().Read(buf, 0, buf.Length);
                    if (r <= 0) break;
                    if (sess.Link != null)
                    {
                        if (!sess.Link.IsOpen) break;
                        var payload = new byte[r];
                        Buffer.BlockCopy(buf, 0, payload, 0, r);
                        sess.Link.Send(s, payload);
                    }
                    else
                    {
                        var frame = new byte[4 + r];
                        frame[0] = (byte)(j >> 8); frame[1] = (byte)j;
                        frame[2] = (byte)(s >> 8); frame[3] = (byte)s;
                        Buffer.BlockCopy(buf, 0, frame, 4, r);
                        var ws = hostWs;
                        if (ws == null || !ws.IsOpen) break;
                        ws.SendBinary(frame);
                    }
                }
            }
            catch { }
            CloseHostSession(key, true);
        }

        static void CloseHostSession(long key, bool notify)
        {
            HostSession sess;
            lock (hostLock)
            {
                if (!hostSessions.TryGetValue(key, out sess)) return;
                hostSessions.Remove(key);
            }
            try { sess.Tcp.Close(); } catch { }
            if (notify)
            {
                int j = (int)(key >> 16), s = (int)(key & 0xFFFF);
                if (sess.Link != null)
                {
                    try { sess.Link.SendControl("{\"type\":\"close\",\"s\":" + s + "}"); } catch { }
                }
                else SendHostClose(j, s);
            }
        }

        static void SendHostClose(int j, int s)
        {
            try
            {
                var ws = hostWs;
                if (ws != null && ws.IsOpen)
                    ws.SendText("{\"type\":\"close\",\"j\":" + j + ",\"s\":" + s + "}");
            }
            catch { }
        }

        static void HostOnClosed(string reason)
        {
            if (!hostActive) return;
            HilLog.Write("Huge隧道 腐竹端掉线: " + reason);
            lock (hostLock)
            {
                foreach (var kv in hostSessions) try { kv.Value.Tcp.Close(); } catch { }
                hostSessions.Clear();
            }
            // 服务器还开着就自动重连（保留房间码，最多 3 次）
            RaiseHostStatus("隧道连接中断，正在重连…");
            Thread.Sleep(3000);
            if (hostActive)
            {
                var t = new Thread(HostConnect);
                t.IsBackground = true;
                t.Start();
            }
        }

        public static void StopHost()
        {
            hostActive = false;
            var ws = hostWs;
            hostWs = null;
            try { if (ws != null) ws.Close(); } catch { }
            lock (hostLock)
            {
                foreach (var kv in hostSessions) try { kv.Value.Tcp.Close(); } catch { }
                hostSessions.Clear();
                foreach (var kv in hostLinks) try { kv.Value.Close(); } catch { }
                hostLinks.Clear();
            }
            foreach (var kv in hostPunchers) try { kv.Value.Stop(); } catch { }
            hostPunchers.Clear();
            hostPeerIps.Clear();
            hostListenPorts.Clear();
            hostRoom = null;
            hostInstance = null;
        }

        static void RaiseHostStatus(string s)
        {
            var h = HostStatus;
            if (h != null)
                try { h(s); } catch { }
        }

        static long HostKey(int j, int s) { return ((long)j << 16) | ((long)s & 0xFFFF); }

        // ================= 玩家端 =================

        public static void StartJoin(string code)
        {
            StopJoin();
            joinCode = code.ToUpperInvariant();
            joinActive = true;
            var t = new Thread(JoinConnect);
            t.IsBackground = true;
            t.Start();
        }

        static void JoinConnect()
        {
            RaiseJoinStatus("正在连接房间 " + joinCode + " …");
            WsClient ws = new WsClient();
            // 先挂事件再握手：ok 帧紧随 101 到达，挂晚了会丢（房间码错误信息也在里面）
            ws.Text += JoinOnText;
            ws.Binary += JoinOnBinary;
            ws.Closed += JoinOnClosed;
            joinWs = ws;   // 必须在 DialInto 前赋值：ok 帧紧随握手到达，StartJoinPunch 要用它发 sig
            try
            {
                DialInto(ws, joinCode, "join");
            }
            catch (Exception ex)
            {
                joinWs = null;
                joinActive = false;
                RaiseJoinStatus("ERR:连不上隧道服务器：" + ex.Message);
                return;
            }
            HilLog.Write("Huge隧道 玩家端已连接: 房间 " + joinCode);
        }

        static void JoinOnText(string text)
        {
            Dictionary<string, object> j;
            try { j = Json.Parse(text); } catch { return; }
            string type = Json.Str(j, "type");
            if (type == "ok")
            {
                // 记下腐竹公网 IP（中继在 ok 里带来），本地监听先开，再决定打洞还是直接中继
                joinHostIp = Json.Str(j, "hip");
                joinReadyFired = false;
                OpenLocalListener();
                if (!string.IsNullOrEmpty(joinHostIp) && joinActive && joinListener != null)
                    StartJoinPunch();
                else if (joinListener != null)
                    FireJoinReady(false);
            }
            else if (type == "sig")
            {
                // 腐竹的打洞端口到了 → 作为 responder 开始打洞
                int p = (int)Json.Long(j, "p");
                if (p > 0) StartJoinPunchTo(p);
            }
            else if (type == "error")
            {
                string msg = Json.Str(j, "message");
                RaiseJoinStatus("ERR:" + msg);
                StopJoin();
            }
            else if (type == "close")
            {
                CloseJoinSession((int)Json.Long(j, "s"), false);
            }
        }

        // ok 后开始打洞：把我方打洞端口报给腐竹，8.5 秒没打通就回落中继
        static void StartJoinPunch()
        {
            int port = TcpPuncher.PickPort();
            joinListenPort = port;
            RaiseJoinStatus("正在尝试打洞直连…");
            try
            {
                var ws = joinWs;
                if (ws != null && ws.IsOpen) ws.SendText("{\"type\":\"sig\",\"p\":" + port + "}");
            }
            catch { }
            var t = new Thread(new ThreadStart(delegate
            {
                Thread.Sleep(8500);
                lock (joinLock)
                {
                    if (!joinActive || joinReadyFired || joinLink != null) return;
                }
                RaiseJoinStatus("打洞没打通，走中继（延迟较高）");
                FireJoinReady(false);
            }));
            t.IsBackground = true;
            t.Start();
        }

        // 收到腐竹 sig 后作为 responder 打洞（发起方是腐竹，魔数它先验）
        static void StartJoinPunchTo(int hostPort)
        {
            if (string.IsNullOrEmpty(joinHostIp) || joinListenPort <= 0) return;
            if (joinPuncher != null || joinLink != null) return;
            joinPuncher = new TcpPuncher(false, joinHostIp, hostPort, joinListenPort);
            joinPuncher.Linked += JoinLinked;
            joinPuncher.Start();
        }

        // 打洞成功：直连成为唯一数据通道（中继 ws 只留控制信令）
        static void JoinLinked(DirectLink l)
        {
            lock (joinLock)
            {
                if (!joinActive || joinLink != null) { try { l.Close(); } catch { } return; }
                joinLink = l;
            }
            l.Data += delegate(int s, byte[] payload) { JoinOnData(s, payload, 0, payload.Length, l); };
            l.Control += delegate(string text)
            {
                try
                {
                    var c = Json.Parse(text);
                    if (Json.Str(c, "type") == "close")
                        CloseJoinSession((int)Json.Long(c, "s"), false);
                }
                catch { }
            };
            l.Closed += delegate
            {
                List<int> dead = new List<int>();
                lock (joinLock)
                {
                    DirectLink cur = joinLink;
                    if (cur == l) joinLink = null;
                    foreach (var kv in joinSessions)
                        if (kv.Value.Link == l) dead.Add(kv.Key);
                }
                foreach (int s in dead) CloseJoinSession(s, false);
                if (joinActive) RaiseJoinStatus("直连中断，后续连接走中继");
            };
            HilLog.Write("Huge隧道 打洞成功，走直连");
            FireJoinReady(true);
        }

        // 打洞/中继结果就绪（幂等：8.5 秒兜底与直连成功可能撞车）
        static void FireJoinReady(bool direct)
        {
            lock (joinLock)
            {
                if (joinReadyFired || !joinActive || joinListener == null) return;
                joinReadyFired = true;
            }
            RaiseJoinStatus("已加入房间 " + joinCode + (direct ? "（直连）" : "（中继）"));
            var ready = JoinReady;
            if (ready != null)
                try { ready(joinLocalPort); } catch { }
        }

        static void OpenLocalListener()
        {
            // 绑 0.0.0.0：假 LAN 广播的来源 IP 是网卡地址，MC 会按来源 IP 连接；
            // 只绑 127.0.0.1 会导致广播进来了却连不上。手动填 127.0.0.1 同样有效。
            int port = 25565;
            TcpListener listener = null;
            for (int i = 0; i < 20; i++)
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, port + i);
                    listener.Start();
                    port += i;
                    break;
                }
                catch { listener = null; }
            }
            if (listener == null)
            {
                RaiseJoinStatus("ERR:本机 25565-25584 端口全被占用");
                StopJoin();
                return;
            }
            joinListener = listener;
            joinLocalPort = port;
            joinLanIp = DetectLanIp();
            StartLanBeacon(port);
            // 注意：就绪状态与 JoinReady 事件由 FireJoinReady 统一触发（要等打洞结果）
            var t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Start();
        }

        static void AcceptLoop()
        {
            while (joinActive && joinListener != null)
            {
                TcpClient c;
                try { c = joinListener.AcceptTcpClient(); }
                catch { break; }
                c.NoDelay = true;
                int sid;
                // 接受时钉死当时的数据通道（直连优先）
                DirectLink link = joinLink;
                var sess = new JoinSession { Tcp = c, Link = link };
                lock (joinLock) { sid = joinNextSession++; joinSessions[sid] = sess; }
                HilLog.Write("Huge隧道 玩家会话接入: " + sid + "（本机端口 " + joinLocalPort + (link != null ? "，直连" : "，中继") + "）");
                var t = new Thread(new ThreadStart(delegate { JoinPump(sid, sess); }));
                t.IsBackground = true;
                t.Start();
            }
        }

        // 本机 MC 客户端 → 直连/中继（按会话钉死的通道）
        static void JoinPump(int sid, JoinSession sess)
        {
            var buf = new byte[16384];
            try
            {
                for (;;)
                {
                    int r = sess.Tcp.GetStream().Read(buf, 0, buf.Length);
                    if (r <= 0) break;
                    if (sess.Link != null)
                    {
                        if (!sess.Link.IsOpen) break;
                        var payload = new byte[r];
                        Buffer.BlockCopy(buf, 0, payload, 0, r);
                        sess.Link.Send(sid, payload);
                    }
                    else
                    {
                        var frame = new byte[2 + r];
                        frame[0] = (byte)(sid >> 8); frame[1] = (byte)sid;
                        Buffer.BlockCopy(buf, 0, frame, 2, r);
                        var ws = joinWs;
                        if (ws == null || !ws.IsOpen) break;
                        ws.SendBinary(frame);
                    }
                }
            }
            catch { }
            CloseJoinSession(sid, true);
        }

        static void JoinOnBinary(byte[] frame)
        {
            if (frame.Length < 2) return;
            int s = (frame[0] << 8) | frame[1];
            JoinOnData(s, frame, 2, frame.Length - 2, null);
        }

        // 中继/直连共用：数据写入本机 MC 客户端
        static void JoinOnData(int s, byte[] buf, int off, int len, DirectLink via)
        {
            JoinSession sess;
            lock (joinLock)
            {
                if (!joinSessions.TryGetValue(s, out sess)) return;
            }
            if (len > 0)
            {
                try { sess.Tcp.GetStream().Write(buf, off, len); }
                catch { CloseJoinSession(s, true); }
            }
        }

        static void CloseJoinSession(int sid, bool notify)
        {
            JoinSession sess;
            lock (joinLock)
            {
                if (!joinSessions.TryGetValue(sid, out sess)) return;
                joinSessions.Remove(sid);
            }
            try { sess.Tcp.Close(); } catch { }
            if (notify)
            {
                try
                {
                    if (sess.Link != null && sess.Link.IsOpen)
                        sess.Link.SendControl("{\"type\":\"close\",\"s\":" + sid + "}");
                    else
                    {
                        var ws = joinWs;
                        if (ws != null && ws.IsOpen)
                            ws.SendText("{\"type\":\"close\",\"s\":" + sid + "}");
                    }
                }
                catch { }
            }
        }

        static void JoinOnClosed(string reason)
        {
            if (!joinActive) return;
            HilLog.Write("Huge隧道 玩家端掉线: " + reason);
            RaiseJoinStatus("ERR:连接中断：" + reason + "，请重新加入");
            StopJoin();
        }

        public static void StopJoin()
        {
            joinActive = false;
            lanOn = false;
            var ws = joinWs;
            joinWs = null;
            try { if (ws != null) ws.Close(); } catch { }
            var l = joinListener;
            joinListener = null;
            try { if (l != null) l.Stop(); } catch { }
            try { if (joinPuncher != null) joinPuncher.Stop(); } catch { }
            joinPuncher = null;
            try { if (joinLink != null) joinLink.Close(); } catch { }
            joinLink = null;
            lock (joinLock)
            {
                foreach (var kv in joinSessions) try { kv.Value.Tcp.Close(); } catch { }
                joinSessions.Clear();
            }
            joinCode = null;
            joinHostIp = null;
            joinListenPort = 0;
            joinReadyFired = false;
            joinLanIp = null;
        }

        // 假 LAN 广播：MC 客户端监听 224.0.2.60:4445，收到后按来源 IP + AD 端口显示局域网世界
        static void StartLanBeacon(int port)
        {
            lanOn = true;
            lanThread = new Thread(new ThreadStart(delegate
            {
                UdpClient udp = null;
                try
                {
                    udp = new UdpClient();
                    var ep = new IPEndPoint(IPAddress.Parse("224.0.2.60"), 4445);
                    byte[] payload = Encoding.UTF8.GetBytes(
                        "[MOTD]HIL 隧道房间 " + joinCode + "[/MOTD][AD]" + port + "[/AD]");
                    while (lanOn)
                    {
                        try { udp.Send(payload, payload.Length, ep); } catch { }
                        Thread.Sleep(1500);
                    }
                }
                catch (Exception ex) { HilLog.Write("Huge隧道 LAN广播失败: " + ex.Message); }
                finally { try { if (udp != null) udp.Close(); } catch { } }
            }));
            lanThread.IsBackground = true;
            lanThread.Start();
        }

        static void RaiseJoinStatus(string s)
        {
            var h = JoinStatus;
            if (h != null)
                try { h(s); } catch { }
        }

        // 退出启动器时调
        public static void StopAll()
        {
            StopHost();
            StopJoin();
        }
    }
}
