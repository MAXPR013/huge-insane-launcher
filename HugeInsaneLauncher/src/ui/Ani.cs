// HugeLauncher.Ui - PCL 式逐帧动画引擎
// 特点：delta-time 增量插值（打断时属性停在当前值，新动画从当前值继续追加）、
// 同名动画组互斥、After 顺序回调链、全局速度系数（接 ThemeManager.Speed）。
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace HugeLauncher.Ui
{
    // ===== 缓动曲线（PCL ModAnimation.vb 同款公式）=====
    abstract class AniEase
    {
        public abstract double Get(double t);
        protected static double Clamp(double t) { return t < 0 ? 0 : (t > 1 ? 1 : t); }
    }

    class AniEaseLinear : AniEase
    {
        public override double Get(double t) { return Clamp(t); }
    }

    // 幂次缓动：power 取 Ani.Weak/Middle/Strong/ExtraStrong（2/3/4/5）
    class AniEaseInFluent : AniEase
    {
        readonly double p;
        public AniEaseInFluent(double power) { p = power; }
        public override double Get(double t) { t = Clamp(t); return Math.Pow(t, p); }
    }

    class AniEaseOutFluent : AniEase
    {
        readonly double p;
        public AniEaseOutFluent(double power) { p = power; }
        public override double Get(double t) { t = Clamp(t); return 1 - Math.Pow(1 - t, p); }
    }

    class AniEaseInoutFluent : AniEase
    {
        readonly double p;
        public AniEaseInoutFluent(double power) { p = power; }
        public override double Get(double t)
        {
            t = Clamp(t);
            if (t < 0.5) return Math.Pow(t * 2, p) / 2;
            return 1 - Math.Pow((1 - t) * 2, p) / 2;
        }
    }

    // 回弹：OutBack 过冲后余弦回摆一次（PCL "Q 弹"的核心），InBack 先反向蓄力
    class AniEaseOutBack : AniEase
    {
        readonly double p;
        public AniEaseOutBack(double power) { p = 3 - power * 0.5; }
        public override double Get(double t) { t = Clamp(t); return 1 - Math.Pow(1 - t, p) * Math.Cos(1.5 * Math.PI * t); }
    }

    class AniEaseInBack : AniEase
    {
        readonly double p;
        public AniEaseInBack(double power) { p = 3 - power * 0.5; }
        public override double Get(double t) { t = Clamp(t); return Math.Pow(t, p) * Math.Cos(1.5 * Math.PI * (1 - t)); }
    }

    // ===== 动画数据 =====
    class AniData
    {
        public Action<double> Apply;  // 增量应用（double 类属性）
        public Action OnStart;        // 首帧回调（如颜色动画捕获起始色）
        public Action Code;           // 到点执行的委托（AaCode）
        public double Value;          // 总变化量
        public double Total = 400;    // 总时长 ms
        public double Finished;       // 已执行时间（负数 = 延迟中）
        public double Last;           // 上次缓动进度
        public AniEase Ease;
        public bool After;            // 等组内非 After 动画完成后才开始
        public bool Absolute;         // true = Apply 直接接收缓动进度 0..1（绝对插值，无残值）
        public bool Done;
        public bool Started;
    }

    static class Ani
    {
        // 缓动力度四档（PCL AniEasePower）
        public const double Weak = 2, Middle = 3, Strong = 4, ExtraStrong = 5;

        static readonly Dictionary<string, List<AniData>> groups = new Dictionary<string, List<AniData>>();
        static readonly Stopwatch watch = new Stopwatch();
        static long lastTick;
        static bool running;

        // 开始一组动画；同名组互斥（打断旧组，属性停在当前值）
        public static void Start(string name, params AniData[] list)
        {
            Stop(name);
            var items = new List<AniData>(list);
            if (!ThemeManager.AnimOn)
            {
                // 动画关闭：立即到达终态并执行回调
                foreach (var a in items)
                {
                    try
                    {
                        if (a.OnStart != null) a.OnStart();
                        if (a.Apply != null) a.Apply(a.Value * (1 - a.Last));
                        if (a.Code != null) a.Code();
                    }
                    catch { }
                }
                return;
            }
            groups[name] = items;
            if (!running)
            {
                running = true;
                watch.Restart();
                lastTick = 0;
                CompositionTarget.Rendering += OnRendering;
            }
            // RefreshTime：重置帧计时，防止长操作后动画瞬跳
            lastTick = watch.ElapsedMilliseconds;
        }

        public static void Stop(string name)
        {
            groups.Remove(name);
            if (groups.Count == 0) Unhook();
        }

        public static bool IsRun(string name) { return groups.ContainsKey(name); }

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
            double delta = (now - lastTick) * ThemeManager.Speed;
            lastTick = now;
            if (delta < 2) return;          // 过小的帧跳过
            if (delta > 250) delta = 250;   // 卡顿钳制，防瞬跳
            var dead = new List<string>();
            // 快照遍历：AaCode 回调里可能 Start/Stop 新组
            foreach (var kv in new List<KeyValuePair<string, List<AniData>>>(groups))
            {
                List<AniData> items;
                if (!groups.TryGetValue(kv.Key, out items) || !ReferenceEquals(items, kv.Value)) continue;
                bool afterPhase = true;
                foreach (var a in items)
                    if (!a.After && !a.Done) { afterPhase = false; break; }
                for (int i = items.Count - 1; i >= 0; i--)
                {
                    var a = items[i];
                    if (a.Done) continue;
                    if (a.After && !afterPhase) continue;
                    a.Finished += delta;
                    if (a.Finished < 0) continue;
                    if (a.Code != null)
                    {
                        a.Done = true;
                        try { a.Code(); } catch { }
                        continue;
                    }
                    if (!a.Started)
                    {
                        a.Started = true;
                        if (a.OnStart != null) { try { a.OnStart(); } catch { } }
                    }
                    double t1 = a.Total <= 0 ? 1 : Math.Min(1, a.Finished / a.Total);
                    double eased = a.Ease == null ? t1 : a.Ease.Get(t1);
                    if (a.Absolute)
                    {
                        // 绝对模式：每帧直接给出缓动进度（0..1），末帧必达 1，帧丢失不留残值
                        a.Last = eased;
                        if (a.Apply != null) { try { a.Apply(eased); } catch { } }
                    }
                    else
                    {
                        double d = (eased - a.Last) * a.Value;
                        a.Last = eased;
                        if (a.Apply != null && d != 0) { try { a.Apply(d); } catch { } }
                    }
                    if (t1 >= 1) a.Done = true;
                }
                items.RemoveAll(delegate(AniData x) { return x.Done; });
                if (items.Count == 0) dead.Add(kv.Key);
            }
            foreach (var g in dead) groups.Remove(g);
            if (groups.Count == 0) Unhook();
        }

        // ===== 工厂方法 =====

        // 通用增量动画
        public static AniData Aa(Action<double> apply, double value, int ms, AniEase ease, int delay = 0, bool after = false)
        {
            return new AniData { Apply = apply, Value = value, Total = ms, Finished = -delay, Ease = ease, After = after };
        }

        // 绝对插值动画：Apply 直接接收缓动进度（0..1，OutBack 等可 >1），末帧必达终值
        public static AniData AaProgress(Action<double> applyEased, int ms, AniEase ease, int delay = 0, bool after = false)
        {
            return new AniData { Apply = applyEased, Value = 1, Total = ms, Finished = -delay, Ease = ease, After = after, Absolute = true };
        }

        // 到点执行委托（完成回调链）
        public static AniData AaCode(Action code, int delay = 0, bool after = true)
        {
            return new AniData { Code = code, Total = 1, Finished = -delay, After = after };
        }

        public static AniData AaOpacity(UIElement el, double delta, int ms, AniEase ease = null, int delay = 0, bool after = false)
        {
            return Aa(delegate(double d) { el.Opacity += d; }, delta, ms,
                ease ?? new AniEaseOutFluent(Weak), delay, after);
        }

        // 确保元素有 TranslateTransform（位移走 RenderTransform，不触发布局）
        public static TranslateTransform Ts(UIElement el)
        {
            var t = el.RenderTransform as TranslateTransform;
            if (t == null)
            {
                t = new TranslateTransform();
                el.RenderTransform = t;
            }
            return t;
        }

        // 确保元素有 ScaleTransform（中心缩放）
        public static ScaleTransform Sc(UIElement el)
        {
            var s = el.RenderTransform as ScaleTransform;
            if (s == null)
            {
                s = new ScaleTransform(1, 1);
                el.RenderTransform = s;
                el.RenderTransformOrigin = new Point(0.5, 0.5);
            }
            return s;
        }

        public static AniData AaX(UIElement el, double delta, int ms, AniEase ease, int delay = 0, bool after = false)
        {
            var t = Ts(el);
            return Aa(delegate(double d) { t.X += d; }, delta, ms, ease, delay, after);
        }

        public static AniData AaY(UIElement el, double delta, int ms, AniEase ease, int delay = 0, bool after = false)
        {
            var t = Ts(el);
            return Aa(delegate(double d) { t.Y += d; }, delta, ms, ease, delay, after);
        }

        // 整体缩放（XY 同步）
        public static AniData AaScale(UIElement el, double delta, int ms, AniEase ease, int delay = 0, bool after = false)
        {
            var s = Sc(el);
            return Aa(delegate(double d) { s.ScaleX += d; s.ScaleY += d; }, delta, ms, ease, delay, after);
        }

        // 颜色渐变（绝对目标色；首帧捕获当前色，打断自然续接，末帧必达目标色）
        public static AniData AaColor(SolidColorBrush brush, Color to, int ms, int delay = 0, bool after = false)
        {
            Color from = to;
            var ad = new AniData { Total = ms, Finished = -delay, Ease = new AniEaseOutFluent(Middle), After = after, Value = 1, Absolute = true };
            ad.OnStart = delegate
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null); // 摘掉旧 WPF 动画
                from = brush.Color;
            };
            ad.Apply = delegate(double e)
            {
                if (e > 1) e = 1;
                brush.Color = Color.FromArgb(
                    (byte)(from.A + (to.A - from.A) * e),
                    (byte)(from.R + (to.R - from.R) * e),
                    (byte)(from.G + (to.G - from.G) * e),
                    (byte)(from.B + (to.B - from.B) * e));
            };
            return ad;
        }
    }
}
