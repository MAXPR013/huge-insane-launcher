// HugeLauncher.Ai - H史对话管理器（单例）：历史维护 / 状态快照 / 工具调用循环 / 确认流
// 全程后台线程执行，事件回调由订阅方自行封送到 UI 线程。
using System;
using System.Collections.Generic;
using System.Threading;

namespace HugeLauncher.Ai
{
    static class AiSteve
    {
        public static Config Cfg = null;   // 主窗口持有的共享配置实例（启动时接线）

        public static event Action<string> ReplyReceived;            // 拿到纯文本回复
        public static event Action<string, string> ToolStarting;     // (工具名, 参数JSON)
        public static event Action<string, string> ToolFinished;     // (工具名, 结果摘要)
        public static event Action<string> ErrorOccurred;            // 网络/解析失败（中文说明）
        public static event Action<string> ConfirmationRequested;    // 重操作待确认（气泡带 好/算了）
        public static event Action TurnFinished;                     // 一轮对话结束（成功或失败都会触发）
        public static event EventHandler ConfigChanged;              // 设置页改动 AI 配置后广播（聊天框显隐等）

        static readonly List<Dictionary<string, object>> messages = new List<Dictionary<string, object>>();
        static readonly object sync = new object();
        static volatile bool busy;

        // 确认流：UI 点 好/算了 → Confirm() 唤醒等待中的对话线程
        static readonly object confirmSync = new object();
        static bool confirmAnswered, confirmResult;

        public static bool Busy { get { return busy; } }

        public static void NotifyConfigChanged()
        {
            var h = ConfigChanged;
            if (h != null) h(null, EventArgs.Empty);
        }

        public static void Confirm(bool agree)
        {
            lock (confirmSync)
            {
                confirmResult = agree;
                confirmAnswered = true;
                Monitor.PulseAll(confirmSync);
            }
        }

        static bool WaitConfirmation(int timeoutMs)
        {
            lock (confirmSync)
            {
                confirmAnswered = false;
                confirmResult = false;
                // 等用户点气泡按钮；超时按拒绝处理
                if (!confirmAnswered) Monitor.Wait(confirmSync, timeoutMs);
                return confirmAnswered && confirmResult;
            }
        }

        public static void Send(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            HilLog.Write("H史 收到消息: " + text);
            var cfg = Cfg;
            if (cfg == null || !cfg.AiEnabled)
            {
                HilLog.Write("H史 拒绝: AI 未开启");
                Fire(ErrorOccurred, "AI 史蒂夫还没开启，到 设置 → 启动器设置 里打开「开启AI史蒂夫」吧。");
                FireTurnFinished();
                return;
            }
            // 没填 key 走内置免费通道（官网中转），填了走用户自己的 DeepSeek
            bool ownKey = !string.IsNullOrWhiteSpace(cfg.AiApiKey);
            HilLog.Write("H史 通道: " + (ownKey ? "自有 DeepSeek key" : "内置免费通道"));
            lock (sync)
            {
                if (busy)
                {
                    HilLog.Write("H史 拒绝: 上一轮还在进行");
                    Fire(ErrorOccurred, "我还在想上一件事，等我一下下…");
                    FireTurnFinished();
                    return;
                }
                busy = true;
            }
            var th = new Thread(new ThreadStart(delegate { Run(text.Trim()); }));
            th.IsBackground = true;
            th.Start();
        }

        // 供 P4 崩溃流程调用：把崩溃摘要当用户消息喂进来，让 AI 读报告并选工具
        public static void SendSystem(string text) { Send(text); }

        static void Run(string userText)
        {
            try
            {
                lock (messages)
                {
                    messages.Add(Msg("user", userText));
                    TrimHistory();
                }
                // 工具调用循环：有 tool_calls 就逐个执行回填，直到拿到纯文本（上限 6 轮防失控）
                for (int round = 0; round < 6; round++)
                {
                    HilLog.Write("H史 第 " + (round + 1) + " 轮请求开始");
                    // 通道选择：自有 key → DeepSeek 直连；空 key → 内置免费中转（GLM）
                    bool own = !string.IsNullOrWhiteSpace(Cfg.AiApiKey);
                    string ep = own ? Cfg.AiEndpoint : AiClient.BuiltinRelay;
                    string key = own ? Cfg.AiApiKey : "";
                    string model = own ? AiClient.OwnKeyModel : AiClient.BuiltinModel;
                    AiResponse resp = AiClient.Chat(ep, key, model, BuildMessages(), AiTools.SchemasForApi());
                    HilLog.Write("H史 第 " + (round + 1) + " 轮响应: 文本 " +
                        (resp.Content == null ? 0 : resp.Content.Length) + " 字，工具调用 " + resp.ToolCalls.Count + " 个");
                    if (resp.ToolCalls.Count == 0)
                    {
                        string content = (resp.Content ?? "").Trim();
                        if (content.Length == 0) content = "（H史走神了，什么也没说）";
                        lock (messages)
                        {
                            messages.Add(Msg("assistant", content));
                            TrimHistory();
                        }
                        Fire(ReplyReceived, content);
                        return;
                    }
                    // 回填 assistant 消息（含 tool_calls，DeepSeek 要求成对出现）
                    var am = new Dictionary<string, object>();
                    am["role"] = "assistant";
                    am["content"] = resp.Content ?? "";
                    var tcs = new List<Dictionary<string, object>>();
                    foreach (var call in resp.ToolCalls)
                    {
                        var fn = new Dictionary<string, object>();
                        fn["name"] = call.Name;
                        fn["arguments"] = call.Arguments;
                        var tc = new Dictionary<string, object>();
                        tc["id"] = call.Id;
                        tc["type"] = "function";
                        tc["function"] = fn;
                        tcs.Add(tc);
                    }
                    am["tool_calls"] = tcs;
                    lock (messages)
                    {
                        messages.Add(am);
                        foreach (var call in resp.ToolCalls)
                        {
                            string result = ExecuteTool(call);
                            var tm = Msg("tool", result);
                            tm["tool_call_id"] = call.Id;
                            messages.Add(tm);
                        }
                        TrimHistory();
                    }
                }
                Fire(ReplyReceived, "这件事步骤有点多，我先做到这里。你接着说，我接着来。");
            }
            catch (Exception ex)
            {
                HilLog.Write("H史 对话异常: " + ex);
                Fire(ErrorOccurred, ex.Message);
            }
            finally
            {
                busy = false;
                FireTurnFinished();
            }
        }

        // 执行一次工具调用：重操作先走气泡确认；剧本进 SteveDirector 队列串行演戏
        static string ExecuteTool(AiToolCall call)
        {
            HilLog.Write("H史 工具调用: " + call.Name + " 参数 " + call.Arguments);
            Fire2(ToolStarting, call.Name, call.Arguments);
            string result = null;
            var tool = AiTools.Find(call.Name);
            if (tool == null)
            {
                result = "没有叫 " + call.Name + " 的工具";
            }
            else
            {
                Dictionary<string, object> args = null;
                try { args = Json.Parse(call.Arguments); }
                catch { result = "工具参数无法解析"; }
                if (args == null && result == null) result = "工具参数为空";
                if (args != null)
                {
                    // NeedConfirm 恒确认；NeedConfirmIf 按参数动态判定（危险命令等），两个都认
                    bool need = tool.NeedConfirm;
                    if (!need && tool.NeedConfirmIf != null)
                    {
                        try { need = tool.NeedConfirmIf(args); } catch { need = true; }
                    }
                    if (need)
                    {
                        string desc = tool.ConfirmText != null ? tool.ConfirmText(args) : ("我要执行「" + tool.Name + "」，可以吗？");
                        Fire(ConfirmationRequested, desc);
                        if (!WaitConfirmation(120000))
                            result = "用户拒绝了这次操作";
                        else
                            result = PlayToolScript(tool, args);
                    }
                    else
                    {
                        result = PlayToolScript(tool, args);
                    }
                }
            }
            Fire2(ToolFinished, call.Name, result);
            return result;
        }

        // 把工具剧本交给导演排队执行，等它演完回收 Run 步骤的结果
        static string PlayToolScript(AiTool tool, Dictionary<string, object> args)
        {
            List<ScriptStep> steps;
            try { steps = tool.BuildScript(args); }
            catch (Exception ex) { return "准备操作失败：" + ex.Message; }
            if (steps == null || steps.Count == 0) return "该工具没有可执行的步骤";

            var done = new ManualResetEvent(false);
            bool cancelled = false;
            SteveDirector.Play(steps, delegate(bool c) { cancelled = c; try { done.Set(); } catch { } });
            done.WaitOne();

            var results = new List<string>();
            foreach (var s in steps)
                if (s.StepKind == ScriptStep.Kind.Run && s.Result != null)
                    results.Add(s.Result);
            string r = results.Count > 0 ? string.Join("\n", results.ToArray()) : "操作没有产出结果";
            if (cancelled) r = "操作被用户打断。" + (results.Count > 0 ? "已完成的部分：" + r : "");
            return r;
        }

        // ---------- 消息维护 ----------

        static Dictionary<string, object> Msg(string role, string content)
        {
            var m = new Dictionary<string, object>();
            m["role"] = role;
            m["content"] = content;
            return m;
        }

        // system 不进历史，每轮重建（状态快照保持新鲜）
        static List<Dictionary<string, object>> BuildMessages()
        {
            var list = new List<Dictionary<string, object>>();
            list.Add(Msg("system", SystemPrompt()));
            lock (messages) list.AddRange(messages);
            return list;
        }

        // 上限保留最近 20 条；开头不能落在 tool/带 tool_calls 的 assistant 上（DeepSeek 要求成对）
        static void TrimHistory()
        {
            while (messages.Count > 20)
            {
                messages.RemoveAt(0);
                while (messages.Count > 0)
                {
                    var first = messages[0];
                    string role = Json.Str(first, "role");
                    bool isTool = role == "tool" || (role == "assistant" && first.ContainsKey("tool_calls"));
                    if (!isTool) break;
                    messages.RemoveAt(0);
                }
            }
        }

        // 人设 + 状态快照
        static string SystemPrompt()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("你是 H史，驻在 Huge Insane Launcher（HIL，一个 Minecraft 启动器）底层的 AI 助手史蒂夫。")
                .Append("语气简洁友好，中文回复。硬性要求：回复不超过 100 字；推荐/列表最多 5 项，每项只报名字；")
                .Append("详情不要一次倒出来，让用户追问再分段给。")
                .Append("你能通过工具真实操作启动器：搜索/安装模组资源、下载和启动游戏、开关服务器、调整内存与主题色、读取并修复崩溃。")
                .Append("下载/安装等重操作工具会先向用户确认，你只需正常调用。")
                .Append("不要假装执行了没有工具支持的事情；做不到的事直接说明。")
                .Append("工具报告找不到资源或版本时，照实说 HIL 没查到，不要猜原因或找借口。")
                // 以下四条经 GLM-4-Flash 基准验证（tools/glm_bench.py，通过率 81.8%→95.5%）
                .Append("规则一：只服务 Minecraft 与 HIL 启动器相关的请求；写作业等无关请求礼貌拒绝。")
                .Append("规则二：搜索 Modrinth 时 query 必须用英文关键词（如：小地图→minimap、AI伙伴→AI companion）。")
                .Append("规则三：用户要下载/安装某个 MC 游戏版本（可带 forge/fabric/neoforge 加载器）时用 download_game；")
                .Append("install_resource 只用于 Modrinth 社区资源，projectId 必须来自 search_resources 的真实结果，禁止编造。")
                .Append("规则四：在服务器里向玩家说话，命令必须加 say 前缀（如 say 大家好）。")
                .Append("\n\n当前启动器状态：");
            var cfg = Cfg;
            if (cfg != null)
            {
                sb.Append("\n玩家名：").Append(cfg.Username);
                sb.Append("\n游戏内存：").Append(cfg.MemoryMb).Append(" MB");
                sb.Append("\n主题色：").Append(cfg.Theme);
                sb.Append("\n当前选中版本：").Append(string.IsNullOrEmpty(cfg.VersionId) ? "（未选择）" : cfg.VersionId);
            }
            try
            {
                var versions = GameCore.GetInstalledVersions();
                sb.Append("\n已安装版本：").Append(versions.Count == 0 ? "（无）" : string.Join("、", versions.ToArray()));
            }
            catch { }
            try
            {
                var servers = ServerCore.GetInstances();
                if (servers.Count == 0) sb.Append("\n服务器实例：（无）");
                else
                {
                    var parts = new List<string>();
                    foreach (var n in servers)
                        parts.Add(n + (HugeLauncher.Ui.ServerManager.IsRunning(n) ? "(运行中)" : "(已停止)"));
                    sb.Append("\n服务器实例：").Append(string.Join("、", parts.ToArray()));
                }
            }
            catch { }
            return sb.ToString();
        }

        // ---------- 事件触发 ----------

        static void Fire(Action<string> h, string s) { if (h != null) h(s); }
        static void Fire2(Action<string, string> h, string a, string b) { if (h != null) h(a, b); }
        static void FireTurnFinished() { var h = TurnFinished; if (h != null) h(); }
    }
}
