// 隧道联调：本地模拟官网中继（明文 ws）+ TCP echo（模拟 MC 服务端），
// 反射驱动 HugeInsaneLauncher.exe 的 TunnelManager 做腐竹端+玩家端全链路验证。
// 用法：在 minecraft 根目录跑 tunnel_test.exe
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

class TunnelTest
{
    // ================= 模拟官网 RelayRoom（协议与 _worker.js 一致）=================
    class RelayServer
    {
        class Conn
        {
            public TcpClient Tcp;
            public NetworkStream Stream;
            public string Room, Role;
            public int JoinerId;
            public readonly object SendLock = new object();
        }
        class Room
        {
            public Conn Host;
            public readonly Dictionary<int, Conn> Joiners = new Dictionary<int, Conn>();
        }
        readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        readonly object roomsLock = new object();

        public void Start(int port)
        {
            var l = new TcpListener(IPAddress.Loopback, port);
            l.Start();
            new Thread(new ThreadStart(delegate
            {
                for (;;)
                {
                    TcpClient c;
                    try { c = l.AcceptTcpClient(); } catch { break; }
                    new Thread(new ThreadStart(delegate { Handle(c); })) { IsBackground = true }.Start();
                }
            })) { IsBackground = true }.Start();
        }

        void Handle(TcpClient tcp)
        {
            var conn = new Conn { Tcp = tcp, Stream = tcp.GetStream() };
            try
            {
                // HTTP 握手
                string reqLine = ReadLine(conn.Stream);
                if (reqLine == null || !reqLine.StartsWith("GET ")) { tcp.Close(); return; }
                string path = reqLine.Split(' ')[1];
                string key = null;
                string line;
                while ((line = ReadLine(conn.Stream)) != null && line.Length > 0)
                    if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                        key = line.Substring(18).Trim();
                var q = new Dictionary<string, string>();
                foreach (var kv in path.Substring(path.IndexOf('?') + 1).Split('&'))
                {
                    var p = kv.Split('=');
                    if (p.Length == 2) q[p[0]] = p[1];
                }
                conn.Room = q["room"];
                conn.Role = q["role"];
                string accept = Convert.ToBase64String(SHA1.Create().ComputeHash(
                    Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
                var resp = Encoding.ASCII.GetBytes("HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\nSec-WebSocket-Accept: " + accept + "\r\n\r\n");
                conn.Stream.Write(resp, 0, resp.Length);

                lock (roomsLock)
                {
                    Room room;
                    if (!rooms.TryGetValue(conn.Room, out room)) { room = new Room(); rooms[conn.Room] = room; }
                    if (conn.Role == "host")
                    {
                        if (room.Host != null)
                            foreach (var j in new List<Conn>(room.Joiners.Values))
                                SendText(j, "{\"type\":\"error\",\"message\":\"腐竹重新上线，请重新加入\"}");
                        room.Host = conn;
                        SendText(conn, "{\"type\":\"ok\",\"role\":\"host\",\"ip\":\"127.0.0.1\"}");
                    }
                    else
                    {
                        if (room.Host == null)
                        {
                            SendText(conn, "{\"type\":\"error\",\"message\":\"房间不存在或腐竹已离线\"}");
                            tcp.Close(); return;
                        }
                        int jid = 1;
                        while (room.Joiners.ContainsKey(jid)) jid++;
                        conn.JoinerId = jid;
                        room.Joiners[jid] = conn;
                        SendText(conn, "{\"type\":\"ok\",\"role\":\"join\",\"id\":" + jid + ",\"hip\":\"127.0.0.1\"}");
                        SendText(room.Host, "{\"type\":\"peer\",\"j\":" + jid + ",\"action\":\"join\",\"ip\":\"127.0.0.1\"}");
                    }
                }
                RecvLoop(conn);
            }
            catch { }
            finally { Remove(conn); try { tcp.Close(); } catch { } }
        }

        void Remove(Conn conn)
        {
            lock (roomsLock)
            {
                Room room;
                if (conn.Room == null || !rooms.TryGetValue(conn.Room, out room)) return;
                if (conn.Role == "host")
                {
                    if (room.Host == conn) room.Host = null;
                    foreach (var j in new List<Conn>(room.Joiners.Values))
                    {
                        SendText(j, "{\"type\":\"error\",\"message\":\"腐竹关闭了隧道\"}");
                        try { j.Tcp.Close(); } catch { }
                    }
                    room.Joiners.Clear();
                }
                else
                {
                    room.Joiners.Remove(conn.JoinerId);
                    if (room.Host != null)
                        SendText(room.Host, "{\"type\":\"peer\",\"j\":" + conn.JoinerId + ",\"action\":\"leave\"}");
                }
            }
        }

        void RecvLoop(Conn conn)
        {
            var frag = new MemoryStream();
            int fragOp = 0;
            for (;;)
            {
                byte[] hdr = ReadExact(conn.Stream, 2);
                bool fin = (hdr[0] & 0x80) != 0;
                int op = hdr[0] & 0x0F;
                long len = hdr[1] & 0x7F;
                if (len == 126) { var b2 = ReadExact(conn.Stream, 2); len = (b2[0] << 8) | b2[1]; }
                else if (len == 127)
                {
                    var b8 = ReadExact(conn.Stream, 8);
                    len = 0;
                    for (int i = 0; i < 8; i++) len = (len << 8) | b8[i];
                }
                bool masked = (hdr[1] & 0x80) != 0;
                byte[] mask = masked ? ReadExact(conn.Stream, 4) : null;
                byte[] payload = ReadExact(conn.Stream, (int)len);
                if (mask != null)
                    for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i & 3];
                if (op == 0x8) break;
                if (op == 0x9) { SendFrame(conn, 0xA, payload); continue; }
                if (op == 0xA) continue;
                if (op == 0x1 || op == 0x2)
                {
                    if (fin) Dispatch(conn, op, payload);
                    else { fragOp = op; frag.SetLength(0); frag.Write(payload, 0, payload.Length); }
                }
                else if (op == 0x0 && fragOp != 0)
                {
                    frag.Write(payload, 0, payload.Length);
                    if (fin) { Dispatch(conn, fragOp, frag.ToArray()); fragOp = 0; }
                }
            }
        }

        void Dispatch(Conn conn, int op, byte[] payload)
        {
            if (op == 0x1)
            {
                string text = Encoding.UTF8.GetString(payload);
                if (text.IndexOf("\"ka\"") >= 0) { SendText(conn, "{\"type\":\"ka\"}"); return; }
                // close / sig 控制帧：玩家→腐竹补 j，腐竹→玩家剥 j
                if (text.IndexOf("\"close\"") >= 0 || text.IndexOf("\"sig\"") >= 0)
                {
                    lock (roomsLock)
                    {
                        Room room;
                        if (!rooms.TryGetValue(conn.Room, out room)) return;
                        if (conn.Role == "join" && room.Host != null)
                            SendText(room.Host, InjectJoiner(text, conn.JoinerId));
                        else if (conn.Role == "host")
                        {
                            int j = ExtractInt(text, "j");
                            Conn target;
                            if (room.Joiners.TryGetValue(j, out target)) SendText(target, StripJoiner(text));
                        }
                    }
                }
                return;
            }
            // 二进制中继
            lock (roomsLock)
            {
                Room room;
                if (!rooms.TryGetValue(conn.Room, out room)) return;
                if (conn.Role == "join")
                {
                    if (room.Host == null) return;
                    var outb = new byte[payload.Length + 2];
                    outb[0] = (byte)(conn.JoinerId >> 8); outb[1] = (byte)conn.JoinerId;
                    Buffer.BlockCopy(payload, 0, outb, 2, payload.Length);
                    SendFrame(room.Host, 0x2, outb);
                }
                else
                {
                    int jid = (payload[0] << 8) | payload[1];
                    Conn target;
                    if (!room.Joiners.TryGetValue(jid, out target)) return;
                    var outb = new byte[payload.Length - 2];
                    Buffer.BlockCopy(payload, 2, outb, 0, outb.Length);
                    SendFrame(target, 0x2, outb);
                }
            }
        }

        static string InjectJoiner(string text, int j)
        {
            // {"type":"close","s":5} → {"type":"close","j":1,"s":5}
            if (text.IndexOf("\"s\":") >= 0)
                return text.Replace("\"s\":", "\"j\":" + j + ",\"s\":");
            // {"type":"sig","p":123} → {"type":"sig","j":1,"p":123}
            return text.Replace("\"p\":", "\"j\":" + j + ",\"p\":");
        }
        static string StripJoiner(string text)
        {
            // {"type":"close","j":1,"s":5} → {"type":"close","s":5}
            int i = text.IndexOf("\"j\":");
            if (i < 0) return text;
            int comma = text.IndexOf(',', i);
            return comma < 0 ? text : text.Remove(i, comma - i + 1);
        }
        static int ExtractInt(string text, string key)
        {
            int i = text.IndexOf("\"" + key + "\":");
            if (i < 0) return 0;
            i += key.Length + 3;
            int e = i;
            while (e < text.Length && char.IsDigit(text[e])) e++;
            return int.Parse(text.Substring(i, e - i));
        }

        void SendText(Conn c, string text) { SendFrame(c, 0x1, Encoding.UTF8.GetBytes(text)); }

        void SendFrame(Conn c, int op, byte[] payload)
        {
            lock (c.SendLock)
            {
                var ms = new MemoryStream();
                ms.WriteByte((byte)(0x80 | op));
                int len = payload.Length;
                if (len < 126) ms.WriteByte((byte)len);
                else if (len <= 0xFFFF) { ms.WriteByte(126); ms.WriteByte((byte)(len >> 8)); ms.WriteByte((byte)len); }
                else { ms.WriteByte(127); for (int i = 7; i >= 0; i--) ms.WriteByte((byte)((len >> (i * 8)) & 0xFF)); }
                ms.Write(payload, 0, payload.Length);
                var f = ms.ToArray();
                c.Stream.Write(f, 0, f.Length);
            }
        }

        static string ReadLine(Stream s)
        {
            var buf = new List<byte>();
            int b;
            while ((b = s.ReadByte()) >= 0)
            {
                if (b == '\n') break;
                if (b != '\r') buf.Add((byte)b);
            }
            if (b < 0 && buf.Count == 0) return null;
            return Encoding.ASCII.GetString(buf.ToArray());
        }
        static byte[] ReadExact(Stream s, int n)
        {
            var buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) throw new Exception("eof");
                off += r;
            }
            return buf;
        }
    }

    // ================= TCP echo（模拟 MC 服务端）=================
    static void StartEcho(int port)
    {
        var l = new TcpListener(IPAddress.Loopback, port);
        l.Start();
        new Thread(new ThreadStart(delegate
        {
            for (;;)
            {
                var c = l.AcceptTcpClient();
                new Thread(new ThreadStart(delegate
                {
                    try
                    {
                        var st = c.GetStream();
                        var buf = new byte[8192];
                        int r;
                        while ((r = st.Read(buf, 0, buf.Length)) > 0) st.Write(buf, 0, r);
                    }
                    catch { }
                    try { c.Close(); } catch { }
                })) { IsBackground = true }.Start();
            }
        })) { IsBackground = true }.Start();
    }

    static int Main(string[] args)
    {
        bool live = args.Length > 0 && args[0] == "live";   // live = 打真实 Cloudflare 中继
        int relayPort = 29888, echoPort = 29777;
        if (!live) new RelayServer().Start(relayPort);
        StartEcho(echoPort);
        Console.WriteLine(live
            ? "[TEST] live 模式：走线上链路 hugegoing.pages.dev/api/relay"
            : "[TEST] 本地中继 ws://127.0.0.1:" + relayPort + " + echo :" + echoPort + " 已起");

        var asm = Assembly.LoadFrom("HugeInsaneLauncher.exe");
        var tm = asm.GetType("HugeLauncher.TunnelManager");
        var bf = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        if (!live)
            tm.GetField("RelayOverride", BindingFlags.NonPublic | BindingFlags.Static)
                .SetValue(null, "127.0.0.1:" + relayPort);

        // 腐竹端上线
        tm.GetMethod("StartHost", bf).Invoke(null, new object[] { "test", echoPort });
        string room = null;
        for (int i = 0; i < 60 && room == null; i++)
        {
            Thread.Sleep(250);
            room = (string)tm.GetProperty("HostRoom", bf).GetValue(null, null);
        }
        if (room == null) { Console.WriteLine("[FAIL] 腐竹端没拿到房间码"); return 1; }
        Console.WriteLine("[OK] 腐竹端房间码: " + room);

        // 玩家端加入
        tm.GetMethod("StartJoin", bf).Invoke(null, new object[] { room });
        int localPort = 0;
        for (int i = 0; i < 60 && localPort == 0; i++)
        {
            Thread.Sleep(250);
            localPort = (int)tm.GetProperty("JoinLocalPort", bf).GetValue(null, null);
        }
        if (localPort == 0) { Console.WriteLine("[FAIL] 玩家端本地监听没起来"); return 1; }
        Console.WriteLine("[OK] 玩家端本地端口: " + localPort);

        // 等打洞直连建立（本地双实例目标是 127.0.0.1，无 NAT，必须打通）
        var dlProp = tm.GetProperty("TestJoinDirect", bf);
        var hcProp = tm.GetProperty("TestHostLinkCount", bf);
        if (dlProp != null && hcProp != null)
        {
            bool direct = false;
            for (int i = 0; i < 48 && !direct; i++)
            {
                Thread.Sleep(250);
                direct = (bool)dlProp.GetValue(null, null);
            }
            if (!direct)
            {
                if (!live) { Console.WriteLine("[FAIL] 12 秒内打洞没打通（本地 127.0.0.1 理应成功）"); return 1; }
                Console.WriteLine("[TEST] 打洞未通（同机打自己公网 IP 需要路由器 hairpin，打不通属正常）→ 验证中继回落");
            }
            else
            {
                Console.WriteLine("[OK] 打洞成功，玩家端已直连");
                int links = 0;
                for (int i = 0; i < 20 && links == 0; i++)
                {
                    Thread.Sleep(250);
                    links = (int)hcProp.GetValue(null, null);
                }
                if (links < 1) { Console.WriteLine("[FAIL] 腐竹侧没登记直连链路"); return 1; }
                Console.WriteLine("[OK] 腐竹侧直连链路数: " + links + "（echo 将走直连通道）");
            }
        }
        else Console.WriteLine("[TEST] 无打洞测试钩子（旧版 exe），跳过直连断言");

        // 两个会话各发一句，验证 echo 全链路
        string[] msgs = { "hello-huge-tunnel", "second-session-42" };
        for (int s = 0; s < 2; s++)
        {
            var c = new TcpClient();
            c.Connect(IPAddress.Loopback, localPort);
            var st = c.GetStream();
            var data = Encoding.ASCII.GetBytes(msgs[s]);
            st.Write(data, 0, data.Length);
            var buf = new byte[data.Length];
            int off = 0;
            var deadline = DateTime.Now.AddSeconds(5);
            while (off < buf.Length && DateTime.Now < deadline)
            {
                if (st.DataAvailable) off += st.Read(buf, off, buf.Length - off);
                else Thread.Sleep(20);
            }
            string got = Encoding.ASCII.GetString(buf, 0, off);
            if (got != msgs[s]) { Console.WriteLine("[FAIL] 会话" + s + " echo 不符: " + got); return 1; }
            Console.WriteLine("[OK] 会话" + s + " 全链路 echo 正确: " + got);
            c.Close();
        }

        // 大数据帧（>64KB，触发 64 位长度编码）
        var big = new byte[70000];
        new Random(1).NextBytes(big);
        var bc = new TcpClient();
        bc.Connect(IPAddress.Loopback, localPort);
        var bs = bc.GetStream();
        bs.Write(big, 0, big.Length);
        var back = new byte[big.Length];
        int boff = 0;
        var dl = DateTime.Now.AddSeconds(10);
        while (boff < back.Length && DateTime.Now < dl)
        {
            if (bs.DataAvailable) boff += bs.Read(back, boff, back.Length - boff);
            else Thread.Sleep(10);
        }
        bool same = boff == big.Length;
        if (same) for (int i = 0; i < big.Length; i += 997) if (big[i] != back[i]) { same = false; break; }
        Console.WriteLine(same ? "[OK] 70KB 大帧 echo 正确" : "[FAIL] 大帧不符（收到 " + boff + "/" + big.Length + "）");
        bc.Close();
        if (!same) return 1;

        tm.GetMethod("StopAll", bf).Invoke(null, null);
        Console.WriteLine("=== 隧道全链路测试通过 ===");
        return 0;
    }
}
