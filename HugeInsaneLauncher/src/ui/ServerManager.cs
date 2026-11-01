// HugeLauncher.Ui - 服务器进程管理：启动/停止 + 控制台输出环形缓冲（跨页面存活）
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace HugeLauncher.Ui
{
    static class ServerManager
    {
        class RunningServer
        {
            public Process Proc;
            public bool Stopping;
            public bool UserStop;   // 用户主动 Stop/StopAll 置位（崩溃关怀据此排除）
        }

        // 最近一次进程退出信息（崩溃关怀用；取走即清，避免重复弹泡）
        class ExitInfo { public int Code; public bool UserStop; }
        static readonly Dictionary<string, ExitInfo> lastExit = new Dictionary<string, ExitInfo>();

        static readonly Dictionary<string, RunningServer> running = new Dictionary<string, RunningServer>();
        static readonly Dictionary<string, List<string>> logBuffers = new Dictionary<string, List<string>>();
        const int LogCap = 500;

        // 事件均可能在后台线程触发，UI 侧须 Dispatcher 封送
        public static event Action<string, string> Output;   // (实例名, 一行日志)
        public static event Action<string> StateChanged;     // (实例名)：启动成功/进程退出/启动失败
        public static event Action<string, int, bool> Exited; // (实例名, 退出码, 用户主动停)：被动通知，不消费 TakeLastExit

        public static bool IsRunning(string name)
        {
            lock (running)
            {
                RunningServer rs;
                if (!running.TryGetValue(name, out rs)) return false;
                try { return !rs.Proc.HasExited; } catch { return false; }
            }
        }

        // 控制台文本（运行中或已停止的实例都保留最近日志）
        public static string GetLogText(string name)
        {
            lock (logBuffers)
            {
                List<string> buf;
                if (!logBuffers.TryGetValue(name, out buf) || buf.Count == 0) return "";
                return string.Join("\n", buf.ToArray());
            }
        }

        // 日志缓冲尾部 N 行（H史 read_server_console 工具用）
        public static string GetLogTail(string name, int lines)
        {
            lock (logBuffers)
            {
                List<string> buf;
                if (!logBuffers.TryGetValue(name, out buf) || buf.Count == 0) return "";
                int n = lines <= 0 ? 50 : Math.Min(lines, buf.Count);
                return string.Join("\n", buf.GetRange(buf.Count - n, n).ToArray());
            }
        }

        // 取最近一次进程退出信息（退出码/是否用户主动停）；没有记录或已取走过返回 false
        public static bool TakeLastExit(string name, out int exitCode, out bool userRequestedStop)
        {
            lock (lastExit)
            {
                ExitInfo info;
                if (!lastExit.TryGetValue(name, out info))
                {
                    exitCode = 0;
                    userRequestedStop = false;
                    return false;
                }
                lastExit.Remove(name);
                exitCode = info.Code;
                userRequestedStop = info.UserStop;
                return true;
            }
        }

        public static void ClearLog(string name)
        {
            lock (logBuffers) logBuffers.Remove(name);
        }

        static void AppendLog(string name, string line)
        {
            lock (logBuffers)
            {
                List<string> buf;
                if (!logBuffers.TryGetValue(name, out buf))
                {
                    buf = new List<string>();
                    logBuffers[name] = buf;
                }
                buf.Add(line);
                if (buf.Count > LogCap) buf.RemoveRange(0, buf.Count - LogCap);
            }
            var h = Output;
            if (h != null) h(name, line);
        }

        static void FireState(string name)
        {
            var h = StateChanged;
            if (h != null) h(name);
        }

        // 开服！！：下载核心（如无）→ 写基础文件 → 启动（全程后台线程，进度进任务中心）
        public static void CreateAndStart(ServerInstance inst)
        {
            var reporter = TaskCenter.Register("开服 " + inst.Name);
            RunPrepare(inst, reporter, "开服 " + inst.Name);
        }

        // 启动服务器：已有实例（核心缺失也会补下，不覆盖已有配置）
        public static void StartExisting(ServerInstance inst)
        {
            var reporter = TaskCenter.Register("启动服务器 " + inst.Name);
            RunPrepare(inst, reporter, "启动服务器 " + inst.Name);
        }

        static void RunPrepare(ServerInstance inst, Action<StepEvent> reporter, string taskName)
        {
            DownloadTaskItem item = null;
            lock (TaskCenter.Tasks)
                foreach (var t in TaskCenter.Tasks)
                    if (!t.Finished && t.Name == taskName) { item = t; break; }
            var th = new Thread(new ThreadStart(delegate
            {
                try
                {
                    AppendLog(inst.Name, "[HIL] 正在准备服务端核心...");
                    ServerCore.EnsureServerJar(inst, reporter);
                    ServerCore.ScaffoldInstance(inst);
                    AppendLog(inst.Name, "[HIL] 正在启动服务器（内存 " + inst.MemoryMb + " MB，端口 " + inst.Port + "）...");
                    Launch(inst);
                    if (item != null) TaskCenter.Complete(item, true, "");
                }
                catch (Exception ex)
                {
                    AppendLog(inst.Name, "[HIL] 启动失败: " + ex.Message);
                    if (item != null) TaskCenter.Complete(item, false, ex.Message);
                }
                FireState(inst.Name);
            }));
            th.IsBackground = true;
            th.Start();
        }

        // 按服务器实例的游戏版本选对应 Java（老版本吃 Java17，新版本吃更高）；
        // 版本 json 解析失败或未装对应组件时退回旧单一运行时，再退 PATH 里的 java
        static string PickJavaForVersion(string gameVersion)
        {
            try
            {
                if (!string.IsNullOrEmpty(gameVersion) && GameCore.IsVersionReady(gameVersion))
                {
                    var rv = GameCore.Resolve(gameVersion);
                    if (!string.IsNullOrEmpty(rv.JavaComponent))
                    {
                        string p = Paths.JavawExeFor(rv.JavaComponent);
                        string exe = Path.Combine(Path.GetDirectoryName(p), "java.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
            catch { }
            string any = Paths.AnyJavaExe;
            return File.Exists(any) ? any : "java.exe";
        }

        static void Launch(ServerInstance inst)
        {
            if (IsRunning(inst.Name)) return;
            string java = PickJavaForVersion(inst.GameVersion);
            var psi = new ProcessStartInfo(java,
                "-Xmx" + inst.MemoryMb + "M -Xms" + Math.Min(inst.MemoryMb, 1024) + "M -jar server.jar nogui");
            psi.WorkingDirectory = inst.Dir;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardInput = true;
            psi.CreateNoWindow = true;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
            var p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            var rs = new RunningServer { Proc = p };
            string name = inst.Name;
            lock (running) running[name] = rs;
            p.Exited += delegate
            {
                int code = 0;
                try { code = p.ExitCode; } catch { }
                lock (running) running.Remove(name);
                lock (lastExit) lastExit[name] = new ExitInfo { Code = code, UserStop = rs.UserStop };
                AppendLog(name, "[HIL] 服务器进程已退出（代码 " + code + "）");
                var exh = Exited;
                if (exh != null) exh(name, code, rs.UserStop);
                FireState(name);
            };
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null) AppendLog(name, e.Data);
            };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
            {
                if (e.Data != null) AppendLog(name, e.Data);
            };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }

        // 关闭服务器：先发 stop 优雅关闭，10 秒未退出再 Kill
        public static void Stop(string name)
        {
            RunningServer rs;
            lock (running)
            {
                if (!running.TryGetValue(name, out rs) || rs.Stopping) return;
                rs.Stopping = true;
                rs.UserStop = true;   // 主动停服，退出时不触发崩溃关怀
            }
            AppendLog(name, "[HIL] 正在关闭服务器...");
            var th = new Thread(new ThreadStart(delegate
            {
                try
                {
                    try { WriteStdin(rs.Proc, "stop"); } catch { }
                    if (!rs.Proc.WaitForExit(10000))
                    {
                        AppendLog(name, "[HIL] 10 秒未响应，强制结束进程");
                        try { rs.Proc.Kill(); } catch { }
                    }
                }
                catch { }
            }));
            th.IsBackground = true;
            th.Start();
        }

        // 经 stdin 写一行命令：绕开 StandardInput 的默认 ANSI 编码，直接写 UTF-8 字节
        //（.NET 4.0 没有 StandardInputEncoding；Java 18+ 按 UTF-8 读 stdin，不设中文会变 ???）
        static void WriteStdin(Process p, string line)
        {
            var b = Encoding.UTF8.GetBytes(line + "\n");
            p.StandardInput.BaseStream.Write(b, 0, b.Length);
            p.StandardInput.BaseStream.Flush();
        }

        // 向运行中的实例发送控制台命令（自动去斜杠；回显 > cmd 进日志缓冲）
        public static bool SendCommand(string name, string cmd)
        {
            if (cmd == null) return false;
            cmd = cmd.Trim();
            if (cmd.StartsWith("/")) cmd = cmd.Substring(1).Trim();
            if (cmd.Length == 0) return false;
            RunningServer rs;
            lock (running)
            {
                if (!running.TryGetValue(name, out rs)) return false;
            }
            try
            {
                if (rs.Proc.HasExited) return false;
                WriteStdin(rs.Proc, cmd);
                AppendLog(name, "> " + cmd);
                return true;
            }
            catch { return false; }
        }

        // 启动器退出前关闭所有服务器
        public static void StopAll()
        {
            List<string> names;
            lock (running) names = new List<string>(running.Keys);
            foreach (var n in names)
            {
                RunningServer rs;
                lock (running) { if (!running.TryGetValue(n, out rs)) continue; }
                rs.UserStop = true;   // 启动器退出属主动停服，不触发崩溃关怀
                try { WriteStdin(rs.Proc, "stop"); } catch { }
            }
        }
    }
}
