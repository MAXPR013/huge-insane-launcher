// HugeLauncher - 入口：GUI / CLI 测试模式
using System;
using System.IO;
using System.Text;

namespace HugeLauncher
{
    static class Program
    {
        static void CliLog(string msg)
        {
            string line = "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + msg;
            try { File.AppendAllText(Paths.CliLogFile, line + "\r\n", new UTF8Encoding(false)); } catch { }
        }

        static void CliStep(StepEvent e)
        {
            switch (e.EventKind)
            {
                case StepEvent.Kind.StepStart: CliLog(">> 步骤" + e.StepIndex + ": " + e.StepName); break;
                case StepEvent.Kind.StepProgress:
                    if (e.Total > 0) CliLog("   " + e.Current + "/" + e.Total + "  " + e.MbPerSec.ToString("F1") + " MB/s");
                    break;
                case StepEvent.Kind.StepDone: CliLog("   " + (e.Success ? "完成" : "失败: " + e.Message)); break;
                case StepEvent.Kind.Log: CliLog(e.Message); break;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Paths.EnsureDirs();
            HilLog.Init();

            // 非 UI 线程异常兜底：先落日志再退出
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs ea)
            {
                try { HilLog.Write("未处理异常(后台): " + CrashReport.FormatException(ea.ExceptionObject as Exception)); } catch { }
            };
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += delegate(object s, System.Threading.Tasks.UnobservedTaskExceptionEventArgs ea)
            {
                try { HilLog.Write("未观察任务异常: " + CrashReport.FormatException(ea.Exception)); } catch { }
                ea.SetObserved();
            };

            // 旧格式加载器版本统一改名为 <mc>-<loader>-<ver>（GUI/CLI 共用；无事发生时为零开销）
            try
            {
                var migrated = GameCore.MigrateLoaderVersionIds();
                if (migrated.Count > 0)
                {
                    var c0 = Config.Load();
                    string mappedId;
                    if (c0.VersionId != null && migrated.TryGetValue(c0.VersionId, out mappedId))
                    {
                        c0.VersionId = mappedId;
                        c0.Save();
                    }
                }
            }
            catch { }

            bool cli = false, doDownload = false, doLaunch = false, doListVersions = false;
            string version = null, user = "Player", fabric = null, forge = null, neoforge = null;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--cli": cli = true; break;
                    case "--download": doDownload = true; break;
                    case "--launch": doLaunch = true; break;
                    case "--list-versions": doListVersions = true; break;
                    case "--version": if (i + 1 < args.Length) version = args[++i]; break;
                    case "--user": if (i + 1 < args.Length) user = args[++i]; break;
                    case "--fabric": if (i + 1 < args.Length) fabric = args[++i]; break;
                    case "--forge": if (i + 1 < args.Length) forge = args[++i]; break;
                    case "--neoforge": if (i + 1 < args.Length) neoforge = args[++i]; break;
                }
            }

            if (cli)
            {
                try
                {
                    File.WriteAllText(Paths.CliLogFile, "", new UTF8Encoding(false));
                    if (doListVersions)
                    {
                        foreach (var v in GameCore.GetInstalledVersions()) CliLog("已安装: " + v);
                        return 0;
                    }
                    if (string.IsNullOrEmpty(version)) { CliLog("缺少 --version"); return 2; }

                    string finalId = version;
                    if (doDownload)
                    {
                        CliLog("查找版本 " + version + " ...");
                        GameCore.VersionEntry found = null;
                        foreach (var v in GameCore.GetVersionList())
                            if (v.Id == version) { found = v; break; }
                        if (found == null) { CliLog("版本不存在"); return 3; }
                        GameCore.EnsureVanilla(found, CliStep);

                        if (fabric != null)
                            finalId = Loaders.InstallFabric(version, fabric, CliStep);
                        else if (forge != null)
                            finalId = Loaders.InstallForge(version, forge, CliStep);
                        else if (neoforge != null)
                            finalId = Loaders.InstallNeoForge(version, neoforge, CliStep);
                        CliLog("最终版本 id: " + finalId);
                    }
                    if (doLaunch)
                    {
                        var cfg = Config.Load();
                        GameCore.Launch(finalId, user, cfg, CliLog);
                    }
                    CliLog("CLI 完成");
                    return 0;
                }
                catch (Exception ex)
                {
                    CliLog("CLI 错误: " + ex);
                    return 1;
                }
            }

            HugeLauncher.Ui.UiEntry.Run();
            return 0;
        }
    }
}
