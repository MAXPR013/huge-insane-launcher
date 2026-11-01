// HugeLauncher.Ai - 剧本引擎：AI 选完工具后，由剧本把"界面上的演戏"与"后台的真实执行"串起来
// 原则：AI 只做听懂+选工具，界面点击/切页是演戏，数据与操作由 Run 步骤真实发生。
// 串行队列（绝不并行）；try/finally 硬保证结束后归还 Idle 并撤操作锁；AiAnim=false 时只跑 Run/Say。
using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using HugeLauncher.Ui;
using HugeLauncher.Ui.Pages;

namespace HugeLauncher.Ai
{
    // 剧本的一步。TextFunc 在执行时求值（可先跑 Run 再汇报其结果）
    class ScriptStep
    {
        public enum Kind { Navigate, LookAt, Swing, Say, WaitFor, WatchProgress, Run }

        public Kind StepKind;
        public string PageKey;                  // Navigate
        public object NavParam;                 // Navigate 可选参数
        public Func<FrameworkElement> Resolver; // LookAt / WatchProgress 目标（执行时解析）
        public Func<bool> Pred;                 // WaitFor / WatchProgress 条件
        public int TimeoutMs;                   // WaitFor / WatchProgress 超时
        public Func<string> Work;               // Run：真实逻辑（后台线程执行）
        public string Text;                     // Say：静态文案
        public Func<string> TextFunc;           // Say：执行时求值（可引用 Run 的结果）
        public string Result;                   // Run 的输出（喂回 AI 的结果摘要）
        public bool Failed;                     // Run 抛异常

        public static ScriptStep Navigate(string key) { return new ScriptStep { StepKind = Kind.Navigate, PageKey = key }; }
        public static ScriptStep Navigate(string key, object param) { return new ScriptStep { StepKind = Kind.Navigate, PageKey = key, NavParam = param }; }
        public static ScriptStep LookAt(Func<FrameworkElement> resolver) { return new ScriptStep { StepKind = Kind.LookAt, Resolver = resolver }; }
        public static ScriptStep DoSwing() { return new ScriptStep { StepKind = Kind.Swing }; }
        public static ScriptStep Say(string text) { return new ScriptStep { StepKind = Kind.Say, Text = text }; }
        public static ScriptStep SayFunc(Func<string> textFunc) { return new ScriptStep { StepKind = Kind.Say, TextFunc = textFunc }; }
        public static ScriptStep Wait(Func<bool> pred, int timeoutMs) { return new ScriptStep { StepKind = Kind.WaitFor, Pred = pred, TimeoutMs = timeoutMs }; }
        public static ScriptStep Watch(Func<FrameworkElement> resolver, Func<bool> pred, int timeoutMs)
        { return new ScriptStep { StepKind = Kind.WatchProgress, Resolver = resolver, Pred = pred, TimeoutMs = timeoutMs }; }
        public static ScriptStep Run(Func<string> work) { return new ScriptStep { StepKind = Kind.Run, Work = work }; }
    }

    static class SteveDirector
    {
        // ---- 由主窗口在启动时接线 ----
        public static SteveView Steve = null;                    // 左栏的史蒂夫
        public static Func<string, FrameworkElement> PageFinder = null; // pageKey → 页面元素（不存在则现场创建）
        public static Func<FrameworkElement> LaunchButton = null;       // 「启动！！」按钮
        public static Func<FrameworkElement> FloatBall = null;          // 下载管理悬浮球
        public static Action<string> SayBubble = null;               // 弹气泡（UI 线程由主窗口保证）
        public static event Action<bool> ActingChanged;             // true=进入 ACTING（盖操作锁），false=归还

        class Job
        {
            public List<ScriptStep> Steps;
            public Action<bool> OnDone;   // bool = 是否被用户打断
        }

        static readonly Queue<Job> queue = new Queue<Job>();
        static readonly object sync = new object();
        static bool workerRunning;
        static volatile bool cancelRequested;
        static volatile bool lastCancelled;

        // 拟人动画总开关（AiAnim=false 时只执行 Run/Say）
        public static bool AnimEnabled
        {
            get { var c = AiSteve.Cfg; return c == null || c.AiAnim; }
        }

        public static bool LastCancelled { get { return lastCancelled; } }

        // 剧本排队执行；onDone 在后台线程回调（bool=是否被打断）
        public static void Play(List<ScriptStep> steps, Action<bool> onDone)
        {
            if (steps == null || steps.Count == 0)
            {
                if (onDone != null) onDone(false);
                return;
            }
            lock (sync)
            {
                queue.Enqueue(new Job { Steps = steps, OnDone = onDone });
                if (workerRunning) return;
                workerRunning = true;
            }
            var th = new Thread(new ThreadStart(Worker));
            th.IsBackground = true;
            th.Start();
        }

        // 中断当前剧本（队列里还没开始的直接丢弃）
        public static void Cancel()
        {
            cancelRequested = true;
            lock (sync) queue.Clear();
        }

        static void Worker()
        {
            while (true)
            {
                Job job;
                lock (sync)
                {
                    if (queue.Count == 0) { workerRunning = false; return; }
                    job = queue.Dequeue();
                }
                cancelRequested = false;
                lastCancelled = false;
                RunJob(job);
            }
        }

        static void RunJob(Job job)
        {
            // 有可视步骤且动画开着才进入 ACTING（否则静默执行，不盖操作锁）
            bool acting = AnimEnabled && HasVisualStep(job.Steps);
            HilLog.Write("剧本 开始：acting=" + acting + "，步骤数=" + job.Steps.Count);
            if (acting)
            {
                OnUi(delegate { if (Steve != null) Steve.Mode = SteveMode.Acting; });
                FireActing(true);
            }
            bool cancelled = false;
            try
            {
                foreach (var step in job.Steps)
                {
                    if (cancelRequested) { cancelled = true; break; }
                    // AiAnim=false：跳过全部演戏步骤，只留 Run / Say
                    if (!acting && step.StepKind != ScriptStep.Kind.Run && step.StepKind != ScriptStep.Kind.Say)
                        continue;
                    Exec(step);
                    if (step.Failed) break;   // Run 失败：结果已进 Result，后续步骤不再演
                }
            }
            catch (Exception ex)
            {
                HilLog.Write("剧本执行异常: " + ex.Message);
            }
            finally
            {
                // 硬保证：无论成功/失败/取消，必须归还 Idle 并撤操作锁；
                // 窗口关闭中 Dispatcher.Invoke 可能抛异常，逐条兜底，绝不让锁挂着
                if (acting)
                {
                    try { OnUi(delegate { if (Steve != null) Steve.Mode = SteveMode.Idle; }); }
                    catch (Exception exIdle) { HilLog.Write("剧本 归还Idle异常: " + exIdle.Message); }
                    try { FireActing(false); } catch { }
                }
                lastCancelled = cancelled;
            }
            if (job.OnDone != null)
            {
                try { job.OnDone(cancelled); } catch { }
            }
        }

        static bool HasVisualStep(List<ScriptStep> steps)
        {
            foreach (var s in steps)
                if (s.StepKind == ScriptStep.Kind.Navigate || s.StepKind == ScriptStep.Kind.LookAt
                    || s.StepKind == ScriptStep.Kind.Swing || s.StepKind == ScriptStep.Kind.WatchProgress)
                    return true;
            return false;
        }

        static void Exec(ScriptStep step)
        {
            HilLog.Write("剧本 步骤 " + step.StepKind +
                (step.StepKind == ScriptStep.Kind.Navigate ? " → " + step.PageKey : ""));
            switch (step.StepKind)
            {
                case ScriptStep.Kind.Navigate:
                    OnUi(delegate { Navigator.Go(step.PageKey, step.NavParam); });
                    Sleep(480);   // 等换页动画跑完
                    break;
                case ScriptStep.Kind.LookAt:
                    OnUi(delegate
                    {
                        var el = step.Resolver != null ? step.Resolver() : null;
                        HilLog.Write("剧本 LookAt：" + (el == null ? "没找到目标" : "找到 " + el.GetType().Name));
                        if (Steve != null && el != null) Steve.AiGazeToControlPoint(el);
                    });
                    Sleep(400);
                    break;
                case ScriptStep.Kind.Swing:
                    OnUi(delegate { if (Steve != null) Steve.Swing(); });
                    Sleep(380);
                    break;
                case ScriptStep.Kind.Say:
                    string text = step.TextFunc != null ? step.TextFunc() : step.Text;
                    if (!string.IsNullOrEmpty(text))
                        OnUi(delegate { var h = SayBubble; if (h != null) h(text); });
                    Sleep(650);
                    break;
                case ScriptStep.Kind.WaitFor:
                    WaitUntil(step.Pred, step.TimeoutMs);
                    break;
                case ScriptStep.Kind.WatchProgress:
                    // 长任务：条件满足前视线一直停在进度控件上
                    var until = DateTime.Now.AddMilliseconds(step.TimeoutMs <= 0 ? 600000 : step.TimeoutMs);
                    while (!cancelRequested)
                    {
                        if (step.Pred != null && step.Pred()) break;
                        if (DateTime.Now > until) break;
                        OnUi(delegate
                        {
                            var el = step.Resolver != null ? step.Resolver() : null;
                            if (Steve != null && el != null) Steve.AiGazeToControlPoint(el);
                        });
                        Sleep(350);
                    }
                    break;
                case ScriptStep.Kind.Run:
                    try
                    {
                        step.Result = step.Work != null ? step.Work() : "";
                        string r = step.Result ?? "";
                        HilLog.Write("剧本 Run 结果: " + (r.Length > 100 ? r.Substring(0, 100) : r));
                    }
                    catch (Exception ex)
                    {
                        step.Failed = true;
                        step.Result = "执行失败：" + ex.Message;
                        HilLog.Write("剧本 Run 异常: " + ex.Message);
                    }
                    break;
            }
        }

        static void WaitUntil(Func<bool> pred, int timeoutMs)
        {
            var until = DateTime.Now.AddMilliseconds(timeoutMs <= 0 ? 30000 : timeoutMs);
            while (!cancelRequested)
            {
                if (pred != null && pred()) return;
                if (DateTime.Now > until) return;
                Thread.Sleep(100);
            }
        }

        static void Sleep(int ms)
        {
            // 分片睡眠，取消时快速响应
            int left = ms;
            while (left > 0 && !cancelRequested)
            {
                int slice = Math.Min(left, 60);
                Thread.Sleep(slice);
                left -= slice;
            }
        }

        static void OnUi(Action a)
        {
            var app = Application.Current;
            if (app == null) { a(); return; }
            if (app.Dispatcher.CheckAccess()) a();
            else app.Dispatcher.Invoke(a);
        }

        static void FireActing(bool acting)
        {
            HilLog.Write("剧本 Acting=" + acting);
            var h = ActingChanged;
            if (h != null) h(acting);
        }
    }
}
