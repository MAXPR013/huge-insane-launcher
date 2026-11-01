// 快速验证：版本号、agent 内嵌资源、Ensure 释放
using System;
using System.IO;
using System.Reflection;

class QuickVerify
{
    static int Main()
    {
        var asm = Assembly.LoadFrom("HugeInsaneLauncher.exe");
        var mw = asm.GetType("HugeLauncher.Ui.MainWindow");
        string ver = (string)mw.GetField("Version", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        Console.WriteLine("版本号: " + ver);
        if (ver != "26.8.10") { Console.WriteLine("[FAIL] 版本号不对"); return 1; }

        string[] res = asm.GetManifestResourceNames();
        bool hasJar = false;
        foreach (var r in res) { Console.WriteLine("内嵌资源: " + r); if (r == "hil-lan-agent.jar") hasJar = true; }
        if (!hasJar) { Console.WriteLine("[FAIL] agent jar 未内嵌"); return 1; }

        var la = asm.GetType("HugeLauncher.LanAgent");
        string jar = (string)la.GetMethod("Ensure", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
        Console.WriteLine("Ensure → " + jar);
        if (jar == null || !File.Exists(jar)) { Console.WriteLine("[FAIL] agent 释放失败"); return 1; }
        var fi = new FileInfo(jar);
        Console.WriteLine("释放大小: " + fi.Length + " 字节");
        if (fi.Length < 100000) { Console.WriteLine("[FAIL] jar 大小异常"); return 1; }
        Console.WriteLine("=== 快速验证通过 ===");
        return 0;
    }
}
