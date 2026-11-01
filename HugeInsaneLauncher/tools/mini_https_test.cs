// 通过反射直接调用 HugeInsaneLauncher.exe 里 AiClient.MiniHttpsPost，打真实中转接口
// 用法: mini_https_test.exe [exe路径]
using System;
using System.Reflection;
using System.Text;

class MiniHttpsTest
{
    static int Main(string[] args)
    {
        string exe = args.Length > 0 ? args[0] : "HugeInsaneLauncher.exe";
        try
        {
            var asm = Assembly.LoadFrom(exe);
            var t = asm.GetType("HugeLauncher.Ai.AiClient");
            if (t == null) { Console.WriteLine("找不到 AiClient 类型"); return 1; }
            var m = t.GetMethod("MiniHttpsPost", BindingFlags.NonPublic | BindingFlags.Static);
            if (m == null) { Console.WriteLine("找不到 MiniHttpsPost 方法"); return 1; }

            byte[] body = Encoding.UTF8.GetBytes(
                "{\"model\":\"glm-4-flash\",\"messages\":[{\"role\":\"user\",\"content\":\"用五个字打个招呼\"}]}");
            object[] p = { "hugegoing.pages.dev", "/api/hshi", body, null, 0, 45000 };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            string text = (string)m.Invoke(null, p);
            sw.Stop();
            int status = (int)p[4];
            Console.WriteLine("HTTP " + status + "  耗时 " + sw.ElapsedMilliseconds + "ms");
            Console.WriteLine(text.Length > 300 ? text.Substring(0, 300) : text);
            return status == 200 ? 0 : 2;
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
