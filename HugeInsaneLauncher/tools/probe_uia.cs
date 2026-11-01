// probe_uia - 用 UI Automation 在启动器窗口里按文本找控件，打印坐标或点击
// 用法: probe_uia <进程名> <文本> [click]
using System;
using System.Diagnostics;
using System.Threading;
using System.Windows.Automation;

class ProbeUia
{
    static int Main(string[] args)
    {
        if (args.Length < 2) { Console.WriteLine("usage: probe_uia <proc> <text> [click] | <proc> clickxy <x> <y>"); return 1; }
        var ps = Process.GetProcessesByName(args[0]);
        if (ps.Length == 0) { Console.WriteLine("no process"); return 1; }
        IntPtr hwnd = FindHwnd(ps[0]);
        if (hwnd == IntPtr.Zero) hwnd = ps[0].MainWindowHandle;
        if (hwnd == IntPtr.Zero) { Console.WriteLine("no window"); return 1; }
        if (args[1] == "clickxy" && args.Length > 3)
        {
            new ProbeClick2().Click(hwnd, int.Parse(args[2]), int.Parse(args[3]));
            Console.WriteLine("clicked " + args[2] + "," + args[3]);
            return 0;
        }
        if (args[1] == "wheel" && args.Length > 4)
        {
            // wheel <x> <y> <格数>：负=向下滚
            ProbeClick2.Wheel(hwnd, int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
            Console.WriteLine("wheeled " + args[4]);
            return 0;
        }
        if (args[1] == "vscroll" && args.Length > 2)
        {
            // vscroll <次数>：UIA ScrollPattern 按页下滚（滚轮事件被自定义滚动条吃掉时用）
            var rootS = AutomationElement.FromHandle(hwnd);
            var svs = rootS.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "ScrollViewer"));
            int times = int.Parse(args[2]);
            bool done = false;
            foreach (AutomationElement sv in svs)
            {
                object spObj;
                if (!sv.TryGetCurrentPattern(ScrollPattern.Pattern, out spObj)) continue;
                var sp = (ScrollPattern)spObj;
                if (!sp.Current.VerticallyScrollable) continue;
                var amt = times < 0 ? ScrollAmount.LargeDecrement : ScrollAmount.LargeIncrement;
                for (int i = 0; i < Math.Abs(times); i++)
                {
                    sp.Scroll(ScrollAmount.NoAmount, amt);
                    Thread.Sleep(500);
                }
                Console.WriteLine("vscrolled x" + times + " on " + sv.Current.BoundingRectangle);
                done = true;
                break;
            }
            if (!done) { Console.WriteLine("no scrollable ScrollViewer"); return 1; }
            return 0;
        }
        if (args[1] == "wheelmsg" && args.Length > 4)
        {
            // wheelmsg <x> <y> <delta>：PostMessage WM_MOUSEWHEEL（mouse_event 的轮消息到不了 WPF）
            ProbeClick2.WheelMsg(hwnd, int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]));
            Console.WriteLine("wheelmsg " + args[4]);
            return 0;
        }
        if (args[1] == "vscrollpct" && args.Length > 2)
        {
            // vscrollpct <0-100>：直接 SetScrollPercent 精确定位
            var rootP = AutomationElement.FromHandle(hwnd);
            var svsP = rootP.FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "ScrollViewer"));
            double pct = double.Parse(args[2]);
            bool doneP = false;
            foreach (AutomationElement sv in svsP)
            {
                object spObj;
                if (!sv.TryGetCurrentPattern(ScrollPattern.Pattern, out spObj)) continue;
                var sp = (ScrollPattern)spObj;
                if (!sp.Current.VerticallyScrollable) continue;
                sp.SetScrollPercent(ScrollPattern.NoScroll, pct);
                Console.WriteLine("scrollpct=" + pct + " on " + sv.Current.BoundingRectangle);
                doneP = true;
                break;
            }
            if (!doneP) { Console.WriteLine("no scrollable ScrollViewer"); return 1; }
            return 0;
        }
        if (args[1] == "dump" && args.Length > 5)
        {
            int x0 = int.Parse(args[2]), y0 = int.Parse(args[3]), x1 = int.Parse(args[4]), y1 = int.Parse(args[5]);
            var rootD = AutomationElement.FromHandle(hwnd);
            var all = rootD.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            foreach (AutomationElement e in all)
            {
                var rr = e.Current.BoundingRectangle;
                if (rr.Width <= 0 || rr.Height <= 0) continue;
                if (rr.X < x1 && rr.X + rr.Width > x0 && rr.Y < y1 && rr.Y + rr.Height > y0)
                    Console.WriteLine(e.Current.ControlType.ProgrammaticName + " | " + e.Current.ClassName + " | \"" + e.Current.Name + "\" | " + rr.X + "," + rr.Y + " " + rr.Width + "x" + rr.Height);
            }
            return 0;
        }
        var root = AutomationElement.FromHandle(hwnd);
        if (root == null) { Console.WriteLine("no window element"); return 1; }
        var cond = new PropertyCondition(AutomationElement.NameProperty, args[1]);
        var el = root.FindFirst(TreeScope.Descendants, cond);
        if (el == null) { Console.WriteLine("not found: " + args[1]); return 1; }
        var r = el.Current.BoundingRectangle;
        Console.WriteLine(args[1] + " rect: " + r.X + "," + r.Y + " " + r.Width + "x" + r.Height);
        if (args.Length > 2 && args[2] == "click")
        {
            object pat;
            if (el.TryGetCurrentPattern(InvokePattern.Pattern, out pat))
            {
                ((InvokePattern)pat).Invoke();
                Console.WriteLine("invoked");
                return 0;
            }
            // 没 InvokePattern 就在中心点一发
            int cx = (int)(r.X + r.Width / 2), cy = (int)(r.Y + r.Height / 2);
            var pc = new ProbeClick2();
            pc.Click(hwnd, cx, cy);
            Console.WriteLine("clicked center " + cx + "," + cy);
        }
        return 0;
    }

    // MainWindowHandle 可能被 IME 组合窗抢走，改为枚举同进程可见且标题含 "Huge" 的窗口
    delegate bool EnumProc(IntPtr h, IntPtr lp);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lp);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder sb, int max);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    static IntPtr FindHwnd(Process p)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate(IntPtr h, IntPtr lp)
        {
            uint pid;
            GetWindowThreadProcessId(h, out pid);
            if (pid != p.Id || !IsWindowVisible(h)) return true;
            int len = GetWindowTextLength(h);
            if (len <= 0) return true;
            var sb = new System.Text.StringBuilder(len + 1);
            GetWindowText(h, sb, sb.Capacity);
            if (sb.ToString().IndexOf("Huge", StringComparison.OrdinalIgnoreCase) < 0) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}

class ProbeClick2
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    public void Click(IntPtr h, int x, int y)
    {
        // 先置顶目标窗口再点击，否则点击会被挡在上面的窗口吃掉；点完撤销置顶
        ShowWindow(h, 9);
        SetForegroundWindow(h);
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002);
        Thread.Sleep(400);
        SetCursorPos(x, y);
        Thread.Sleep(150);
        mouse_event(0x0002, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(60);
        mouse_event(0x0004, 0, 0, 0, IntPtr.Zero);
        Thread.Sleep(200);
        SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, uint msg, IntPtr wp, IntPtr lp);
    // WM_MOUSEWHEEL：wParam 高字=带符号 delta，lParam=屏幕坐标（WPF HwndSource 直接收）
    public static void WheelMsg(IntPtr h, int x, int y, int delta)
    {
        ShowWindow(h, 9);
        SetForegroundWindow(h);
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002);
        Thread.Sleep(300);
        IntPtr wp = new IntPtr((delta << 16));
        IntPtr lp = new IntPtr(((y & 0xFFFF) << 16) | (x & 0xFFFF));
        PostMessage(h, 0x020A, wp, lp);
        Thread.Sleep(120);
        SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002);
    }
    public static void Wheel(IntPtr h, int x, int y, int notches)
    {
        ShowWindow(h, 9);
        SetForegroundWindow(h);
        SetWindowPos(h, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002);
        Thread.Sleep(300);
        SetCursorPos(x, y);
        Thread.Sleep(150);
        mouse_event(0x0800, 0, 0, (uint)(notches * 120), IntPtr.Zero); // MOUSEEVENTF_WHEEL
        Thread.Sleep(200);
        SetWindowPos(h, new IntPtr(-2), 0, 0, 0, 0, 0x0001 | 0x0002);
    }
}
