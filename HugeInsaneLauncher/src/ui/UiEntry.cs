// HugeLauncher.Ui - GUI 入口（Program.cs 的 GUI 分支调用）
namespace HugeLauncher.Ui
{
    static class UiEntry
    {
        public static void Run()
        {
            HilLog.Init();
            HilLog.WriteEnvironment();
            var app = new System.Windows.Application();
            // 启动器全局异常兜底：写日志 → 深色错误报告对话框
            app.DispatcherUnhandledException += delegate(object s, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
            {
                string detail = CrashReport.FormatException(e.Exception);
                HilLog.Write("未处理异常(UI): " + detail);
                e.Handled = true;
                try { new CrashDialog(detail, CrashReport.EnvironmentInfo()).ShowDialog(); } catch { }
            };
            Config cfg0 = Config.Load();
            DarkScrollBar.Register(app, cfg0.Skin != "solid");   // 流光/霜面用细滚动条，素底用经典滚动条
            app.Run(new MainWindow());
        }
    }
}
