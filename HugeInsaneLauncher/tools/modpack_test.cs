// 反射调用 HugeInsaneLauncher.exe 的 Modrinth.InstallModpack，实测整合包一键安装链路
using System;
using System.Reflection;

class ModpackTest
{
    static int Main(string[] args)
    {
        try
        {
            var asm = Assembly.LoadFrom("HugeInsaneLauncher.exe");
            var t = asm.GetType("HugeLauncher.Modrinth");
            var m = t.GetMethod("InstallModpack", BindingFlags.Public | BindingFlags.Static);
            if (m == null) { Console.WriteLine("找不到 InstallModpack"); return 1; }

            // report 委托：StepEvent 是程序集内部类型，用 Expression 树把 Action<StepEvent> 绑到 Report(object)
            var stType = asm.GetType("HugeLauncher.StepEvent");
            var param = System.Linq.Expressions.Expression.Parameter(stType, "e");
            var call = System.Linq.Expressions.Expression.Call(
                typeof(ModpackTest).GetMethod("Report", BindingFlags.Public | BindingFlags.Static),
                param);
            var dlg = System.Linq.Expressions.Expression.Lambda(
                typeof(Action<>).MakeGenericType(stType), call, param).Compile();

            object result = m.Invoke(null, new object[] { args.Length >= 1 ? args[0] : "zombie-invade-100-days", args.Length >= 2 ? args[1] : "惊变100天", dlg });
            Console.WriteLine("=== 安装完成，实例名: " + result);

            // 验证：版本 json 存在 + Resolve 能解析（= 可启动）
            var gc = asm.GetType("HugeLauncher.GameCore");
            var ready = gc.GetMethod("IsVersionReady", BindingFlags.Public | BindingFlags.Static);
            Console.WriteLine("IsVersionReady: " + ready.Invoke(null, new object[] { result }));
            var resolve = gc.GetMethod("Resolve", BindingFlags.Public | BindingFlags.Static);
            var rv = resolve.Invoke(null, new object[] { result });
            var rvT = rv.GetType();
            Console.WriteLine("RootId=" + rvT.GetField("RootId").GetValue(rv)
                + " MainClass=" + rvT.GetField("MainClass").GetValue(rv)
                + " Libs=" + ((System.Collections.ICollection)rvT.GetField("Libraries").GetValue(rv)).Count);
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

    // 打印 StepEvent（反射读字段，避免编译期类型依赖）
    public static void Report(object e)
    {
        var t = e.GetType();
        string kind = t.GetField("EventKind").GetValue(e).ToString();
        string name = t.GetField("StepName").GetValue(e).ToString();
        string msg = t.GetField("Message").GetValue(e).ToString();
        long cur = (long)t.GetField("Current").GetValue(e);
        long tot = (long)t.GetField("Total").GetValue(e);
        int idx = (int)t.GetField("StepIndex").GetValue(e);
        if (kind == "StepStart") Console.WriteLine("[步骤" + idx + "] " + name);
        else if (kind == "StepDone") Console.WriteLine("[步骤" + idx + "] 完成 " + msg);
        else if (kind == "Log") Console.WriteLine("  " + msg);
        else if (tot > 0 && cur % Math.Max(1, tot / 5) == 0) Console.WriteLine("  进度 " + cur + "/" + tot);
    }
}
