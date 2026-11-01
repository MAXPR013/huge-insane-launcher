// probe_click - 给指定进程的窗口发一次鼠标左键点击（probe_shot 的拍档）
// 用法: probe_click <进程名> <x> <y>   （屏幕绝对坐标）
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

class ProbeClick
{
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SetActiveWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, IntPtr p);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    static int Main(string[] args)
    {
        if (args.Length < 3) { Console.WriteLine("usage: probe_click <proc> <x> <y>"); return 1; }
        var ps = Process.GetProcessesByName(args[0]);
        if (ps.Length == 0) { Console.WriteLine("no process"); return 1; }
        IntPtr h = ps[0].MainWindowHandle;
        if (h == IntPtr.Zero) { Console.WriteLine("no window"); return 1; }

        // 借前台线程的输入队列把窗口顶上来
        IntPtr fg = GetForegroundWindow();
        uint fgTid = GetWindowThreadProcessId(fg, IntPtr.Zero);
        uint myTid = GetCurrentThreadId();
        AttachThreadInput(myTid, fgTid, true);
        ShowWindow(h, 9); // SW_RESTORE
        BringWindowToTop(h);
        SetActiveWindow(h);
        SetForegroundWindow(h);
        AttachThreadInput(myTid, fgTid, false);
        // 临时置顶，防止别的窗口盖住目标吃掉点击（点完撤销）
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002); // HWND_TOPMOST|NOSIZE|NOMOVE
        Thread.Sleep(500);

        int x = int.Parse(args[1]), y = int.Parse(args[2]);
        SetCursorPos(x, y);
        Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); // LEFTDOWN
        Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); // LEFTUP
        Thread.Sleep(300);
        SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002); // HWND_NOTOPMOST
        Console.WriteLine("clicked " + x + "," + y + " fg=" + (GetForegroundWindow() == h));
        return 0;
    }
}
