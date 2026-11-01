// HugeLauncher.Ui - UI 卡顿哨兵：后台线程监测 UI 心跳，卡死 >500ms 自动记录到 hil.log
// 只观测，不改变任何现有行为。日志用于事后定位卡顿真凶（页面上下文 + 卡顿时长）
using System;
using System.Threading;
using System.Windows.Threading;
using HugeLauncher;

namespace HugeLauncher.Ui
{
    static class UiWatchdog
    {
        // 当前页面 key，主窗口 Navigate 时更新；哨兵日志里带上它
        public static string Context = "";

        static DateTime lastPong = DateTime.UtcNow;
        static bool started, stalled;
        static DateTime stallStart;

        const double StallMs = 500;     // 超过即判定卡顿
        const int BeatMs = 100;         // UI 心跳间隔
        const int PatrolMs = 200;       // 哨兵巡查间隔

        public static void Start(Dispatcher uiDispatcher)
        {
            if (started) return;
            started = true;

            // 心跳：Background 优先级，UI 线程被占死或长期繁忙都会体现为心跳中断
            var timer = new DispatcherTimer(DispatcherPriority.Background, uiDispatcher);
            timer.Interval = TimeSpan.FromMilliseconds(BeatMs);
            timer.Tick += delegate { lastPong = DateTime.UtcNow; };
            timer.Start();

            var th = new Thread(new ThreadStart(delegate
            {
                while (true)
                {
                    Thread.Sleep(PatrolMs);
                    var gap = DateTime.UtcNow - lastPong;
                    if (!stalled && gap.TotalMilliseconds > StallMs)
                    {
                        stalled = true;
                        stallStart = lastPong;
                        HilLog.Write("[哨兵] UI 无响应开始（页面: " + Context + "）");
                    }
                    else if (stalled && gap.TotalMilliseconds <= StallMs)
                    {
                        stalled = false;
                        HilLog.Write("[哨兵] UI 恢复，本次卡顿 " +
                            (DateTime.UtcNow - stallStart).TotalSeconds.ToString("0.0") + " 秒（页面: " + Context + "）");
                    }
                }
            }));
            th.IsBackground = true;
            th.Start();
        }
    }
}
