// HugeLauncher.Ui - 下载任务中心：跨页面的任务注册与进度状态（禁止修改本文件）
using System;
using System.Collections.Generic;

namespace HugeLauncher.Ui
{
    class TaskStep
    {
        public int Index;
        public string Name = "";
        public long Cur, Total;      // Total=0 表示不确定进度
        public double Mbps;
        public int State;            // 0=等待 1=进行 2=完成 3=失败
        public string Error = "";
    }

    class DownloadTaskItem
    {
        public string Name;
        public List<TaskStep> Steps = new List<TaskStep>();
        public bool Finished;        // 整个任务结束（成功或失败）
        public bool Failed;
        public string FailMessage = "";
        public DateTime StartTime = DateTime.Now;

        // 估算总进度（完成步骤计 1，进行步骤按 Cur/Total）
        public double Progress
        {
            get
            {
                if (Finished) return Failed ? 0 : 1;
                if (Steps.Count == 0) return 0;
                double sum = 0;
                foreach (var s in Steps)
                {
                    if (s.State == 2) sum += 1;
                    else if (s.State == 1 && s.Total > 0) sum += Math.Min(1.0, (double)s.Cur / s.Total);
                }
                return sum / Steps.Count;
            }
        }

        public double TotalMbps
        {
            get { double m = 0; foreach (var s in Steps) if (s.State == 1) m += s.Mbps; return m; }
        }
    }

    static class TaskCenter
    {
        public static List<DownloadTaskItem> Tasks = new List<DownloadTaskItem>();
        public static event Action Changed;   // 任何任务状态变化（可能在后台线程！UI 侧须 Dispatcher.BeginInvoke）

        static void Fire()
        {
            var h = Changed;
            if (h != null) h();
        }

        // 注册任务并返回进度上报器（把它当 Action<StepEvent> 传给核心层 Download/Install 方法）
        public static Action<StepEvent> Register(string name)
        {
            var item = new DownloadTaskItem { Name = name };
            lock (Tasks) Tasks.Insert(0, item);
            Fire();
            return delegate(StepEvent e)
            {
                lock (item)
                {
                    switch (e.EventKind)
                    {
                        case StepEvent.Kind.StepStart:
                            TaskStep st = null;
                            foreach (var s in item.Steps) if (s.Index == e.StepIndex) { st = s; break; }
                            if (st == null) { st = new TaskStep { Index = e.StepIndex }; item.Steps.Add(st); item.Steps.Sort(delegate(TaskStep a, TaskStep b) { return a.Index - b.Index; }); }
                            st.Name = e.StepName; st.State = 1; st.Cur = 0; st.Total = 0; st.Error = "";
                            break;
                        case StepEvent.Kind.StepProgress:
                            foreach (var s in item.Steps) if (s.Index == e.StepIndex) { s.Cur = e.Current; s.Total = e.Total; s.Mbps = e.MbPerSec; }
                            break;
                        case StepEvent.Kind.StepDone:
                            foreach (var s in item.Steps) if (s.Index == e.StepIndex) { s.State = e.Success ? 2 : 3; s.Error = e.Message; if (!e.Success) { item.Failed = true; item.FailMessage = e.Message; } }
                            break;
                    }
                }
                Fire();
            };
        }

        // 任务整体结束（成功/失败由调用方告知）
        public static void Complete(DownloadTaskItem item, bool success, string message)
        {
            lock (item)
            {
                item.Finished = true;
                item.Failed = !success;
                if (!success && !string.IsNullOrEmpty(message)) item.FailMessage = message;
            }
            Fire();
        }

        public static DownloadTaskItem FindByReporter(Action<StepEvent> reporter) { return null; } // 预留

        // 全部任务的平均进度与总速度（下载管理页左栏用）
        public static void Overall(out double progress, out double mbps, out int remainingFiles)
        {
            double p = 0; double m = 0; int files = 0; int n = 0;
            lock (Tasks)
            {
                foreach (var t in Tasks)
                {
                    if (t.Finished) continue;
                    n++;
                    p += t.Progress;
                    m += t.TotalMbps;
                    lock (t) foreach (var s in t.Steps) if (s.State == 1 && s.Total > 0) files += (int)(s.Total - s.Cur);
                }
            }
            progress = n > 0 ? p / n : 1;
            mbps = m;
            remainingFiles = files;
        }
    }
}
