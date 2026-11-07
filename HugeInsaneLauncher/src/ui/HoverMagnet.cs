// HugeLauncher.Ui - 悬停惯性：鼠标在控件上快速移动时，控件被「拖」着偏一点，
// 停下后弹簧把偏移拉回 0（带过冲回摆）。只做位移，不改缩放——缩放交给各自的按钮弹簧。
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HugeLauncher.Ui
{
    class HoverMagnet
    {
        readonly FrameworkElement host;
        readonly TranslateTransform tf;
        readonly Spring lagX = new Spring(0) { Stiffness = 320, Damping = 18 };
        readonly Spring lagY = new Spring(0) { Stiffness = 320, Damping = 18 };
        readonly DispatcherTimer timer;
        Point last;
        DateTime lastT;
        double vx, vy;
        bool tracking;
        const double MaxOffset = 1.25;   // 位移上限（再减半：大面积控件上 2.5px 显得晃）

        public static void Attach(FrameworkElement host)
        {
            if (host == null) return;
            new HoverMagnet(host);
        }

        // ---------- 全局自动挂载 ----------
        // 规则：除「按钮 / 滑钮 / 文本框 / 滚动条」外，任何可点击区域都自动上磁吸。
        // 判定不看有没有绑事件（反射拿不到），而是看 Cursor=Hand 这个全项目统一的约定：
        // 列表行、卡片、标签、下拉框都设了 Hand。命中后取「最外层的那个 Hand 元素」
        // （Cursor 是继承属性，子元素也会读到 Hand），这样动的是整行/整卡而不是里面的字。
        static readonly ConditionalWeakTable<FrameworkElement, object> attached =
            new ConditionalWeakTable<FrameworkElement, object>();
        static readonly List<FrameworkElement> chain = new List<FrameworkElement>(32);

        public static void AttachSmart(DependencyObject src)
        {
            if (src == null) return;
            chain.Clear();
            DependencyObject d = src;
            while (d != null && chain.Count < 40)
            {
                var fe = d as FrameworkElement;
                if (fe == null) break;
                if (fe is Window) break;
                chain.Add(fe);
                d = VisualTreeHelper.GetParent(fe) ?? (DependencyObject)LogicalTreeHelper.GetParent(fe);
            }
            if (chain.Count == 0) return;

            // 按钮 / 滑钮 / 文本框 / 滚动条：整条链直接放弃（它们有自己的反馈）
            for (int i = 0; i < chain.Count; i++)
            {
                var e = chain[i];
                if (e is FlatButton || e is Slider || e is TextBoxBase ||
                    e is PasswordBox || e is ScrollBar) return;
            }

            // 目标 = 链上最外层的 Hand 元素
            FrameworkElement target = null;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i].Cursor == Cursors.Hand) { target = chain[i]; break; }
            }
            // 下拉框没有 Hand 光标，但显然属于「可点击」
            if (target == null)
            {
                for (int i = 0; i < chain.Count; i++)
                    if (chain[i] is ComboBox) { target = chain[i]; break; }
            }
            if (target == null || target is Window) return;
            // 太大的一块（整页/整栏）不做磁吸，否则一甩鼠标整页跟着抖
            if (target.ActualWidth > 720 && target.ActualHeight > 520) return;
            object dummy;
            if (attached.TryGetValue(target, out dummy)) return;
            attached.Add(target, null);
            Attach(target);
        }

        HoverMagnet(FrameworkElement host)
        {
            this.host = host;
            tf = new TranslateTransform();
            // 已经有变换的控件不抢（避免覆盖别处的动画）
            var tg = host.RenderTransform as TransformGroup;
            if (host.RenderTransform == null || host.RenderTransform is TranslateTransform)
            {
                var g = new TransformGroup();
                g.Children.Add(host.RenderTransform ?? new TranslateTransform());
                g.Children.Add(tf);
                host.RenderTransform = g;
            }
            else if (tg != null)
            {
                tg.Children.Add(tf);
            }
            else
            {
                var g = new TransformGroup();
                g.Children.Add(host.RenderTransform);
                g.Children.Add(tf);
                host.RenderTransform = g;
            }

            timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Tick += OnTick;

            host.MouseEnter += delegate
            {
                tracking = false;
                vx = vy = 0;
            };
            host.MouseMove += delegate(object s, MouseEventArgs e)
            {
                Point p = e.GetPosition(host);
                DateTime now = DateTime.UtcNow;
                if (!tracking)
                {
                    tracking = true;
                    last = p;
                    lastT = now;
                    if (!timer.IsEnabled) timer.Start();
                    return;
                }
                double dt = (now - lastT).TotalSeconds;
                if (dt <= 0.0001) return;
                double nvx = (p.X - last.X) / dt;
                double nvy = (p.Y - last.Y) / dt;
                vx = vx * 0.6 + nvx * 0.4;
                vy = vy * 0.6 + nvy * 0.4;
                last = p;
                lastT = now;
                if (!timer.IsEnabled) timer.Start();
            };
            host.MouseLeave += delegate
            {
                tracking = false;
                vx = vy = 0;
                SpringEngine.Go(lagX, 0, ApplyX);
                SpringEngine.Go(lagY, 0, ApplyY);
            };
        }

        void ApplyX(double v, double velocity) { tf.X = v; }
        void ApplyY(double v, double velocity) { tf.Y = v; }

        void OnTick(object sender, EventArgs e)
        {
            // 速度衰减；位移由速度驱动，鼠标停下时目标自然回到 0
            vx *= 0.70;
            vy *= 0.70;
            double tx = vx * 0.01375;   // 速度→位移系数同步减半
            double ty = vy * 0.01375;
            if (tx > MaxOffset) tx = MaxOffset;
            if (tx < -MaxOffset) tx = -MaxOffset;
            if (ty > MaxOffset) ty = MaxOffset;
            if (ty < -MaxOffset) ty = -MaxOffset;
            SpringEngine.Go(lagX, tx, ApplyX);
            SpringEngine.Go(lagY, ty, ApplyY);

            bool calm = Math.Abs(vx) < 6 && Math.Abs(vy) < 6 && Math.Abs(tf.X) < 0.15 && Math.Abs(tf.Y) < 0.15;
            if (!tracking && calm)
            {
                tf.X = 0;
                tf.Y = 0;
                timer.Stop();
            }
        }
    }
}
