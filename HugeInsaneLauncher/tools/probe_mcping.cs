// probe_mcping - 真 MC 状态协议握手（handshake + status request），打印服务器返回的 JSON
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

class McPing
{
    static void WriteVarInt(Stream s, int v)
    {
        uint uv = (uint)v;
        while (true)
        {
            byte b = (byte)(uv & 0x7F);
            uv >>= 7;
            if (uv != 0) b |= 0x80;
            s.WriteByte(b);
            if (uv == 0) break;
        }
    }
    static void WriteString(Stream s, string str)
    {
        var bytes = Encoding.UTF8.GetBytes(str);
        WriteVarInt(s, bytes.Length);
        s.Write(bytes, 0, bytes.Length);
    }
    static int ReadVarInt(Stream s)
    {
        int result = 0, shift = 0;
        while (true)
        {
            int b = s.ReadByte();
            if (b < 0) throw new Exception("连接被关闭");
            result |= (b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
            if (shift > 35) throw new Exception("VarInt 过长");
        }
        return result;
    }
    static void SendPacket(Stream s, int id, byte[] payload)
    {
        var ms = new MemoryStream();
        WriteVarInt(ms, id);
        if (payload != null) ms.Write(payload, 0, payload.Length);
        var data = ms.ToArray();
        WriteVarInt(s, data.Length);
        s.Write(data, 0, data.Length);
    }

    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: probe_mcping <host> <port>"); return 2; }
        string host = args[0]; int port = int.Parse(args[1]);
        try
        {
            var c = new TcpClient();
            c.ReceiveTimeout = 8000; c.SendTimeout = 8000;
            var ar = c.BeginConnect(host, port, null, null);
            if (!ar.AsyncWaitHandle.WaitOne(8000)) { Console.WriteLine("FAIL: TCP 连接超时"); return 1; }
            c.EndConnect(ar);
            Console.WriteLine("TCP 已连接 -> " + host + ":" + port);
            var s = c.GetStream();
            var hs = new MemoryStream();
            WriteVarInt(hs, 767);            // 协议号（状态查询不在意版本）
            WriteString(hs, host);
            hs.WriteByte((byte)(port >> 8)); hs.WriteByte((byte)(port & 0xFF));
            WriteVarInt(hs, 1);              // next state = status
            SendPacket(s, 0, hs.ToArray());
            SendPacket(s, 0, null);          // status request
            int len = ReadVarInt(s);
            int id = ReadVarInt(s);
            int jsonLen = ReadVarInt(s);
            var buf = new byte[jsonLen];
            int got = 0;
            while (got < jsonLen)
            {
                int n = s.Read(buf, got, jsonLen - got);
                if (n <= 0) break;
                got += n;
            }
            string json = Encoding.UTF8.GetString(buf, 0, got);
            Console.WriteLine("STATUS OK (" + len + " 字节, id=" + id + "):");
            Console.WriteLine(json.Length > 500 ? json.Substring(0, 500) + "..." : json);
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("FAIL: " + ex.GetType().Name + ": " + ex.Message);
            return 1;
        }
    }
}
