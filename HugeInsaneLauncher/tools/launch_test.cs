// 反射调用 GameCore.Launch 真实启动一个版本（验证多运行时自动选择）
using System;
using System.Reflection;

class LaunchTest
{
    static int Main(string[] args)
    {
        string id = args.Length > 0 ? args[0] : "惊变100天";
        try
        {
            var asm = Assembly.LoadFrom("HugeInsaneLauncher.exe");
            var gc = asm.GetType("HugeLauncher.GameCore");
            var cfgT = asm.GetType("HugeLauncher.Config");
            object cfg = Activator.CreateInstance(cfgT);   // 字段默认值（2048MB 等）随构造生效
            var launch = gc.GetMethod("Launch", BindingFlags.Public | BindingFlags.Static);
            Action<string> log = delegate(string s) { Console.WriteLine("[LOG] " + s); };
            object proc = launch.Invoke(null, new object[] { id, "TestPlayer", cfg, log, null });
            var p = (System.Diagnostics.Process)proc;
            Console.WriteLine("=== 进程已启动 PID=" + p.Id);
            // 观察 75 秒：老 Forge 启动慢，撑过早期初始化即算过
            for (int i = 0; i < 15; i++)
            {
                System.Threading.Thread.Sleep(5000);
                if (p.HasExited)
                {
                    Console.WriteLine("=== 进程已退出，退出码 " + p.ExitCode + "（" + (i + 1) * 5 + " 秒）");
                    return p.ExitCode == 0 ? 0 : 2;
                }
                Console.WriteLine("...存活 " + (i + 1) * 5 + "s");
            }
            Console.WriteLine("=== 75 秒仍存活，判定启动成功");
            return 0;
        }
        catch (Exception ex)
        {
            var e = ex;
            while (e.InnerException != null) e = e.InnerException;
            Console.WriteLine("失败: " + e.Message);
            return 1;
        }
    }
}
