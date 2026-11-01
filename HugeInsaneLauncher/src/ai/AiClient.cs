// HugeLauncher.Ai - OpenAI 兼容聊天客户端
// 注意：不用 HttpWebRequest！Cloudflare 的 IPv6 在国内常被运营商黑洞（SYN 无响应），
// 而 .NET Framework 的 HttpWebRequest 没有 Happy Eyeballs，AAAA 记录存在时会先卡死整个超时。
// 这里用自实现的 MiniHttpsPost（IPv4 优先 + SslStream + 手写 HTTP/1.1）。
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Web.Script.Serialization;

namespace HugeLauncher.Ai
{
    // 一次 tool_call：id / 函数名 / 参数 JSON 字符串
    class AiToolCall
    {
        public string Id, Name, Arguments;
    }

    // 模型一轮回复：纯文本 content 与/或工具调用列表
    class AiResponse
    {
        public string Content;
        public readonly List<AiToolCall> ToolCalls = new List<AiToolCall>();
    }

    static class AiClient
    {
        // 内置免费通道：HIL 官网中转（Cloudflare Pages Function → GLM-4-Flash），用户无需 key
        public const string BuiltinRelay = "https://hugeinsane.pages.dev/api/hshi";
        public const string BuiltinModel = "glm-4-flash";
        public const string OwnKeyModel = "deepseek-chat";

        static readonly JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        // POST 聊天补全；endpoint 为内置中转时直用完整 URL，否则按 OpenAI 规范拼 /chat/completions。
        // apiKey 为空 = 走中转（key 在云端），不发送 Authorization 头。失败抛带中文说明的异常。
        public static AiResponse Chat(string endpoint, string apiKey, string model,
            List<Dictionary<string, object>> messages, List<Dictionary<string, object>> tools)
        {
            if (string.IsNullOrEmpty(endpoint)) endpoint = "https://api.deepseek.com/v1";
            var payload = new Dictionary<string, object>();
            payload["model"] = model;
            payload["messages"] = messages;
            payload["tools"] = tools;
            payload["tool_choice"] = "auto";
            byte[] body = Encoding.UTF8.GetBytes(ser.Serialize(payload));

            string url = endpoint == BuiltinRelay ? endpoint : endpoint.TrimEnd('/') + "/chat/completions";
            HilLog.Write("AI 请求: " + url + "（消息 " + messages.Count + " 条）");
            int status;
            string text;
            try
            {
                var uri = new Uri(url);
                text = MiniHttpsPost(uri.Host, uri.PathAndQuery, body,
                    string.IsNullOrEmpty(apiKey) ? null : apiKey, out status, 45000);
            }
            catch (Exception ex)
            {
                HilLog.Write("AI 请求失败: " + ex.Message);
                throw new Exception("连接 AI 服务失败：" + ex.Message + "，请检查网络。");
            }
            HilLog.Write("AI 响应: HTTP " + status + "，" + text.Length + " 字节 | " +
                (text.Length > 200 ? text.Substring(0, 200) : text));
            if (status < 200 || status >= 300)
                throw new Exception(ExplainStatus(status, text));

            try
            {
                return Parse(text);
            }
            catch (Exception ex)
            {
                throw new Exception("AI 返回的数据无法解析：" + ex.Message);
            }
        }

        // ---------- 迷你 HTTPS 客户端（IPv4 优先，绕开被黑洞的 IPv6） ----------

        // POST 一段 JSON，返回响应体文本；非 2xx 不抛异常（交给上层解释），网络错误抛异常。
        static string MiniHttpsPost(string host, string path, byte[] body, string bearer,
            out int statusCode, int timeoutMs)
        {
            statusCode = 0;
            // IPv4 优先排序（v6 被黑洞时也能先走通 v4；v4 全挂才试 v6）
            var addrs = Dns.GetHostAddresses(host);
            Array.Sort(addrs, delegate(IPAddress a, IPAddress b)
            {
                bool av4 = a.AddressFamily == AddressFamily.InterNetwork;
                bool bv4 = b.AddressFamily == AddressFamily.InterNetwork;
                return av4 == bv4 ? 0 : (av4 ? -1 : 1);
            });
            Exception lastErr = null;
            foreach (var ip in addrs)
            {
                TcpClient tcp = null;
                try
                {
                    tcp = new TcpClient();
                    var ar = tcp.BeginConnect(ip, 443, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(8000))
                        throw new Exception("连接 " + ip + " 超时");
                    tcp.EndConnect(ar);
                    tcp.NoDelay = true;

                    var ssl = new SslStream(tcp.GetStream(), false);
                    ssl.AuthenticateAsClient(host, null,
                        (System.Security.Authentication.SslProtocols)3072 /*Tls12*/, false);
                    ssl.ReadTimeout = timeoutMs;
                    ssl.WriteTimeout = timeoutMs;

                    // 手写请求头（Connection: close，避免 keep-alive 的边界问题）
                    var sb = new StringBuilder();
                    sb.Append("POST ").Append(path).Append(" HTTP/1.1\r\n");
                    sb.Append("Host: ").Append(host).Append("\r\n");
                    sb.Append("User-Agent: HugeInsaneLauncher/3.0\r\n");
                    sb.Append("Content-Type: application/json\r\n");
                    if (bearer != null) sb.Append("Authorization: Bearer ").Append(bearer).Append("\r\n");
                    sb.Append("Content-Length: ").Append(body.Length).Append("\r\n");
                    sb.Append("Connection: close\r\n\r\n");
                    var head = Encoding.ASCII.GetBytes(sb.ToString());
                    ssl.Write(head, 0, head.Length);
                    ssl.Write(body, 0, body.Length);
                    ssl.Flush();

                    // 读状态行与响应头
                    string statusLine = ReadLine(ssl);
                    if (statusLine == null || !statusLine.StartsWith("HTTP/"))
                        throw new Exception("响应格式异常");
                    var parts = statusLine.Split(' ');
                    if (parts.Length < 2) throw new Exception("响应格式异常");
                    statusCode = int.Parse(parts[1]);
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string line;
                    int headBytes = 0;
                    while ((line = ReadLine(ssl)) != null && line.Length > 0)
                    {
                        headBytes += line.Length;
                        if (headBytes > 32768) throw new Exception("响应头过大");
                        int colon = line.IndexOf(':');
                        if (colon > 0) headers[line.Substring(0, colon).Trim()] = line.Substring(colon + 1).Trim();
                    }

                    // 读响应体：Content-Length / chunked / 读到断连 三种形态
                    byte[] raw;
                    string cl;
                    if (headers.TryGetValue("Content-Length", out cl))
                    {
                        raw = ReadExact(ssl, int.Parse(cl), timeoutMs);
                    }
                    else
                    {
                        string te;
                        if (headers.TryGetValue("Transfer-Encoding", out te) && te.IndexOf("chunked") >= 0)
                            raw = ReadChunked(ssl, timeoutMs);
                        else
                            raw = ReadToEnd(ssl);
                    }
                    string text = Encoding.UTF8.GetString(raw);
                    try { ssl.Close(); } catch { }
                    try { tcp.Close(); } catch { }
                    return text;
                }
                catch (Exception ex)
                {
                    lastErr = ex;
                    try { if (tcp != null) tcp.Close(); } catch { }
                }
            }
            throw lastErr ?? new Exception("无法连接服务器");
        }

        static string ReadLine(Stream s)
        {
            var buf = new List<byte>();
            int b;
            while ((b = s.ReadByte()) >= 0)
            {
                if (b == '\n') break;
                if (b != '\r') buf.Add((byte)b);
                if (buf.Count > 8192) throw new Exception("响应行过长");
            }
            if (b < 0 && buf.Count == 0) return null;
            return Encoding.ASCII.GetString(buf.ToArray());
        }

        static byte[] ReadExact(Stream s, int n, int timeoutMs)
        {
            var buf = new byte[n];
            int off = 0;
            while (off < n)
            {
                int r = s.Read(buf, off, n - off);
                if (r <= 0) throw new Exception("连接提前断开");
                off += r;
            }
            return buf;
        }

        static byte[] ReadChunked(Stream s, int timeoutMs)
        {
            var ms = new MemoryStream();
            for (;;)
            {
                string sizeLine = ReadLine(s);
                if (sizeLine == null) throw new Exception("连接提前断开");
                int size = int.Parse(sizeLine.Split(';')[0].Trim(), System.Globalization.NumberStyles.HexNumber);
                if (size == 0) { ReadLine(s); break; }   // 末块后的空行
                var chunk = ReadExact(s, size, timeoutMs);
                ms.Write(chunk, 0, chunk.Length);
                ReadLine(s);   // 块尾 CRLF
            }
            return ms.ToArray();
        }

        static byte[] ReadToEnd(Stream s)
        {
            var ms = new MemoryStream();
            var buf = new byte[8192];
            int r;
            try { while ((r = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, r); }
            catch (IOException) { }   // EOF/超时均视为结束
            catch (SocketException) { }
            return ms.ToArray();
        }

        // 非 2xx 时从响应体里挖可读信息（{"error":{"message":...}}）
        static string ExplainStatus(int status, string detail)
        {
            if (!string.IsNullOrEmpty(detail))
            {
                try
                {
                    var j = Json.Parse(detail);
                    string m = Json.Str(Json.Obj(j["error"]), "message");
                    if (!string.IsNullOrEmpty(m))
                    {
                        if (m.Length > 200) m = m.Substring(0, 200) + "...";
                        if (status == 401)
                            return "API Key 无效或已过期（401），请到 设置 → 启动器设置 检查。";
                        return "AI 服务返回错误（HTTP " + status + "）：" + m;
                    }
                }
                catch { }
            }
            return "AI 服务返回错误（HTTP " + status + "），请稍后再试。";
        }

        static AiResponse Parse(string text)
        {
            var root = Json.Parse(text);
            object choicesObj, msgObj, tcObj;
            if (!root.TryGetValue("choices", out choicesObj)) throw new Exception("响应里没有 choices");
            var choices = Json.Arr(choicesObj);
            if (choices == null || choices.Count == 0) throw new Exception("响应里没有 choices");
            var first = Json.Obj(choices[0]);
            if (first == null || !first.TryGetValue("message", out msgObj)) throw new Exception("响应里没有 message");
            var msg = Json.Obj(msgObj);
            if (msg == null) throw new Exception("响应里没有 message");
            var r = new AiResponse();
            r.Content = Json.Str(msg, "content");
            // 纯文本回复时 DeepSeek 直接不返回 tool_calls 键，必须 TryGetValue（直接索引会抛 KeyNotFound）
            var calls = msg.TryGetValue("tool_calls", out tcObj) ? Json.Arr(tcObj) : null;
            if (calls != null)
                foreach (var o in calls)
                {
                    var c = Json.Obj(o);
                    object fnObj;
                    if (c == null || !c.TryGetValue("function", out fnObj)) continue;
                    var fn = Json.Obj(fnObj);
                    if (fn == null) continue;
                    r.ToolCalls.Add(new AiToolCall
                    {
                        Id = Json.Str(c, "id"),
                        Name = Json.Str(fn, "name"),
                        Arguments = Json.Str(fn, "arguments") ?? "{}",
                    });
                }
            return r;
        }
    }
}
