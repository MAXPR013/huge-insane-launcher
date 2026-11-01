// HugeLauncher.Ui - 阻尼弹簧引擎：iOS 式粘滞/过冲/果冻形变的物理核心
// 阻尼谐振子半隐式欧拉积分，挂在 CompositionTarget.Rendering 逐帧推进；静止自动脱钩。
// 速度量外露给控件做果冻形变（运动方向拉伸、垂直方向挤压）。
// 尊重 ThemeManager.Speed：动画关闭时 Go() 直达目标，零弹簧开销。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Media;

namespace HugeLauncher.Ui
{
    class Spring
    {
        public double Value;
        public double Velocity;
        public double Target;
        public double Stiffness = 170;   // 刚度：越大越「跟手」
        public double Damping = 15;      // 阻尼：越小越过冲（Q 弹）
        public double Snap = 0.0008;     // 静止判定（相对量）

        public Spring(double v) { Value = v; Target = v; }

        public bool Settled
        {
            get
            {
                double scale = Math.Max(1.0, Math.Abs(Target));
                return Math.Abs(Target - Value) < Snap * scale && Math.Abs(Velocity) < Snap * scale * 4;
            }
        }

        public void SnapTo(double v) { Value = v; Target = v; Velocity = 0; }

        // 积分一步（dt 秒）
        public void Step(double dt)
        {
            double f = -Stiffness * (Value - Target) - Damping * Velocity;
            Velocity += f * dt;
            Value += Velocity * dt;
        }
    }

    static class SpringEngine
    {
        class Entry { public Spring S; public Action<double, double> Apply; } // 回调（当前值, 当前速度）
        static readonly List<Entry> active = new List<Entry>();
        static readonly Stopwatch watch = new Stopwatch();
        static long lastTick;
        static bool running;

        // 目标值变化时调用：动画关闭直达终态；否则挂弹簧逐帧逼近
        public static void Go(Spring s, double target, Action<double, double> apply)
        {
            s.Target = target;
            if (!ThemeManager.AnimOn)
            {
                bool moved = !s.Settled || s.Value != target;
                s.SnapTo(target);
                if (moved && apply != null) { try { apply(s.Value, 0); } catch { } }
                return;
            }
            Attach(s, apply);
        }

        public static void Attach(Spring s, Action<double, double> apply)
        {
            foreach (var e in active)
                if (ReferenceEquals(e.S, s)) { e.Apply = apply; return; }
            active.Add(new Entry { S = s, Apply = apply });
            if (!running)
            {
                running = true;
                watch.Restart();
                lastTick = 0;
                CompositionTarget.Rendering += OnRendering;
            }
            // 重置帧计时，防止长操作后弹簧瞬跳
            lastTick = watch.ElapsedMilliseconds;
        }

        public static void Detach(Spring s)
        {
            active.RemoveAll(delegate(Entry e) { return ReferenceEquals(e.S, s); });
            if (active.Count == 0) Unhook();
        }

        static void Unhook()
        {
            if (!running) return;
            running = false;
            CompositionTarget.Rendering -= OnRendering;
            watch.Reset();
        }

        static void OnRendering(object sender, EventArgs e)
        {
            long now = watch.ElapsedMilliseconds;
            double ms = now - lastTick;
            lastTick = now;
            if (ms < 2) return;
            if (ms > 100) ms = 100;   // 卡顿钳制，防瞬跳
            double dt = ms / 1000.0 * Math.Max(0.25, ThemeManager.Speed);
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var en = active[i];
                // 子步进积分：半隐式欧拉在大步长下发散（刚度300、阻尼13时帧间隔>87ms即指数放大——
                // 低配机/软件渲染必中，症状是悬停控件无限放大直至卡死），拆成 ≤20ms 小步保收敛
                double remain = dt;
                while (remain > 0.0001 && !en.S.Settled)
                {
                    double h = Math.Min(remain, 0.02);
                    en.S.Step(h);
                    remain -= h;
                }
                if (en.S.Settled)
                {
                    en.S.SnapTo(en.S.Target);
                    active.RemoveAt(i);
                    try { en.Apply(en.S.Value, 0); } catch { }
                    continue;
                }
                try { en.Apply(en.S.Value, en.S.Velocity); } catch { }
            }
            if (active.Count == 0) Unhook();
        }

        // 果冻形变：由速度算 XY 缩放（运动轴拉伸、垂直轴挤压），x 为位移速度（px/秒）
        public static void Jelly(double velocity, double maxStretch, ScaleTransform st)
        {
            double k = Math.Min(maxStretch, Math.Abs(velocity) * 0.00035);
            st.ScaleX = 1 + k;
            st.ScaleY = 1 - k * 0.6;
        }
    }
}
