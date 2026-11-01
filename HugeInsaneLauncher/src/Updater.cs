// HugeLauncher - 自动更新：官网版本号比对 → 气泡确认 → 下载 → bat 接力自替换重启
// 官网（Cloudflare Pages）是唯一的版本事实源：version.txt 一行版本号 + HugeInsaneLauncher.exe 本体
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;

namespace HugeLauncher
{
    static class Updater
    {
        const string VersionUrl = "https://hugeinsane.pages.dev/version.txt";
        const string ExeUrl = "https://hugeinsane.pages.dev/HugeInsaneLauncher.exe";

        // MainWindow 注册：发现新版时弹气泡确认（参数 = 新版本号）；此处回调已在 UI 线程
        public static Action<string> NewVersionHandler;

        public static void NotifyNewVersion(string newVer)
        {
            var h = NewVersionHandler;
            if (h != null) h(newVer);
        }

        // 后台拉 version.txt 比对；三个回调都在后台线程，UI 请自行 Dispatcher
        public static void CheckAsync(string currentVer, Action<string> onNew, Action onLatest, Action<string> onFail)
        {
            var th = new Thread(new ThreadStart(delegate
            {
                try
                {
                    string txt = Net.GetString(VersionUrl + "?t=" + DateTime.Now.Ticks).Trim();
                    // 只取第一行，防部署方尾部杂讯
                    int nl = txt.IndexOfAny(new char[] { '\r', '\n' });
                    if (nl >= 0) txt = txt.Substring(0, nl).Trim();
                    if (txt.Length == 0)
                    {
                        if (onFail != null) onFail("官网版本号为空");
                        return;
                    }
                    if (IsNewer(txt, currentVer)) { if (onNew != null) onNew(txt); }
                    else if (onLatest != null) onLatest();
                }
                catch (Exception ex)
                {
                    if (onFail != null) onFail(ex.Message);
                }
            }));
            th.IsBackground = true;
            th.Start();
        }

        // 版本号比对：remote 比 current 新（按 . 拆数值逐段比，缺段按 0；非数字段截断）
        public static bool IsNewer(string remote, string current)
        {
            var r = (remote ?? "").Split('.');
            var c = (current ?? "").Split('.');
            for (int i = 0; i < Math.Max(r.Length, c.Length); i++)
            {
                int rv = i < r.Length ? ParseNum(r[i]) : 0;
                int cv = i < c.Length ? ParseNum(c[i]) : 0;
                if (rv != cv) return rv > cv;
            }
            return false;
        }

        static int ParseNum(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length && char.IsDigit(s[i]); i++)
                n = n * 10 + (s[i] - '0');
            return n;
        }

        // 下载新版 exe 到 update 目录 → 写接力 bat（等本进程退出 → 覆盖自身 → 启动新版 → 自删）→ 退出
        // bytesDown：下载字节进度（可为 null）；onFail/onReady 在后台线程
        public static void DownloadAndSwapAsync(Action<long> bytesDown, Action<string> onFail, Action onReady)
        {
            var th = new Thread(new ThreadStart(delegate
            {
                try
                {
                    string exePath = Assembly.GetExecutingAssembly().Location;
                    string updDir = Path.Combine(Paths.LauncherDir, "update");
                    Directory.CreateDirectory(updDir);
                    string newExe = Path.Combine(updDir, "HugeInsaneLauncher.new.exe");
                    try { if (File.Exists(newExe)) File.Delete(newExe); } catch { }
                    long r = Net.DownloadFile(new DownloadTask(ExeUrl + "?t=" + DateTime.Now.Ticks, newExe, null, 0), bytesDown);
                    if (r < 0 || !File.Exists(newExe) || new FileInfo(newExe).Length == 0)
                    {
                        if (onFail != null) onFail("下载失败，请稍后再试");
                        return;
                    }
                    int pid = Process.GetCurrentProcess().Id;
                    string bat = Path.Combine(updDir, "hil_update.bat");
                    File.WriteAllText(bat,
                        "@echo off\r\n" +
                        ":wait\r\n" +
                        "tasklist /FI \"PID eq " + pid + "\" | find \"" + pid + "\" >nul\r\n" +
                        "if %errorlevel%==0 (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                        "move /y \"" + newExe + "\" \"" + exePath + "\" >nul\r\n" +
                        "start \"\" \"" + exePath + "\"\r\n" +
                        "del \"%~f0\"\r\n",
                        Encoding.ASCII);
                    if (onReady != null) onReady();   // 同步清理完再放行 bat
                    var psi = new ProcessStartInfo("cmd.exe", "/c \"" + bat + "\"");
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    if (onFail != null) onFail(ex.Message);
                }
            }));
            th.IsBackground = true;
            th.Start();
        }
    }
}
