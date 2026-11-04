// HugeLauncher.Ui - 主题管理：全局强调色 + 动画速度系数
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HugeLauncher.Ui
{
    static class ThemeManager
    {
        // 全局共享强调色画刷：各元素直接引用它，换主题时一处改色全局联动
        public static readonly SolidColorBrush Accent = new SolidColorBrush(Palette.Green);
        public static Color CurrentColor = Palette.Green;
        public static string CurrentName = "绿";
        // 动画速度系数（0 = 关闭动画）
        public static double Speed = 1.0;

        // MainWindow 接线：亮/暗色切换（带渐变罩重建）与自动调色求值；设置页只调钩子不碰窗口
        public static Action<bool> LightModeSwitch;
        public static Action EvalAuto;

        // 主题：liquid=流光 / frost=霜面 / solid=素底
        public static string Skin = "liquid";
        public static bool IsLiquid { get { return Skin == "liquid"; } }
        public static bool IsFrost { get { return Skin == "frost"; } }
        public static bool IsSolid { get { return Skin == "solid"; } }
        public static event EventHandler SkinChanged;

        // 布局：classic=经典横排 / vertical=纵向舞台
        public static string Layout = "classic";
        public static bool IsVertical { get { return Layout == "vertical"; } }
        public static event EventHandler LayoutChanged;

        public static void ApplyLayout(string l)
        {
            if (l != "classic" && l != "vertical") l = "classic";
            if (Layout == l) return;
            Layout = l;
            var h = LayoutChanged;
            if (h != null) h(null, EventArgs.Empty);
        }

        public static void ApplySkin(string s)
        {
            if (s != "liquid" && s != "frost" && s != "solid") s = "liquid";
            if (Skin == s)
            {
                bool sameOn = (s == "liquid");
                if (Glass.On != sameOn)
                {
                    Glass.On = sameOn;
                    var g0 = GlassChanged;
                    if (g0 != null) g0(null, EventArgs.Empty);
                }
                return;
            }
            Skin = s;
            Palette.Apply(Palette.Light);
            bool on = (s == "liquid");
            if (Glass.On != on)
            {
                Glass.On = on;
                var gh = GlassChanged;
                if (gh != null) gh(null, EventArgs.Empty);
            }
            var h = SkinChanged;
            if (h != null) h(null, EventArgs.Empty);
        }

        // 液态玻璃总开关（Glass.On 的唯一事实源；切换触发 GlassChanged，控件各自重建玻璃脸）
        public static bool LiquidGlass
        {
            get { return Glass.On; }
        }

        public static event EventHandler GlassChanged;

        public static void ApplyGlass(bool on)
        {
            ApplySkin(on ? "liquid" : "solid");
        }

        // 需要换色回调的元素（如光晕颜色）注册此事件
        public static event EventHandler ColorChanged;

        // 校验 "#RRGGBB" 格式
        public static bool IsValidHex(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length != 7 || s[0] != '#') return false;
            for (int i = 1; i < 7; i++) if (!Uri.IsHexDigit(s[i])) return false;
            return true;
        }

        public static Color ColorFor(string name)
        {
            // H史 set_theme 支持任意十六进制色（同时可持久化到 Config.Theme）
            if (IsValidHex(name))
                return Color.FromRgb(Convert.ToByte(name.Substring(1, 2), 16),
                    Convert.ToByte(name.Substring(3, 2), 16), Convert.ToByte(name.Substring(5, 2), 16));
            switch (name)
            {
                case "蓝": return Color.FromRgb(0x4F, 0x8F, 0xDF);
                case "紫": return Color.FromRgb(0x9B, 0x6B, 0xD3);
                case "橙": return Color.FromRgb(0xE0, 0x8A, 0x3C);
                case "红": return Color.FromRgb(0xD9, 0x53, 0x4F);
                case "白": return Color.FromRgb(0xE8, 0xE8, 0xE8);
                default: return Color.FromRgb(0x55, 0xC4, 0x5A); // 绿
            }
        }

        public static void ApplyTheme(string name)
        {
            CurrentName = name;
            CurrentColor = ColorFor(name);
            if (AnimOn)
                Accent.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(CurrentColor, Dur(300)));
            else
                Accent.Color = CurrentColor;
            var h = ColorChanged;
            if (h != null) h(null, EventArgs.Empty);
        }

        public static bool AnimOn { get { return Speed > 0.01; } }

        // 按速度系数换算动画时长；动画关闭时为 0
        public static Duration Dur(double ms)
        {
            if (!AnimOn) return new Duration(TimeSpan.Zero);
            return new Duration(TimeSpan.FromMilliseconds(ms / Speed));
        }

        // 颜色渐变动画；动画关闭时直接到终态
        public static void AnimateColor(SolidColorBrush brush, Color to, double ms)
        {
            if (brush.IsFrozen) return;
            if (!AnimOn || ms <= 0)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                brush.Color = to;
                return;
            }
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(to, Dur(ms)));
        }

        // double 属性动画；动画关闭时直接到终态
        public static void AnimateDouble(DependencyObject target, DependencyProperty prop, double to, double ms)
        {
            var anim = target as IAnimatable;
            if (anim == null) return;
            if (!AnimOn || ms <= 0)
            {
                anim.BeginAnimation(prop, null);
                target.SetValue(prop, to);
                return;
            }
            anim.BeginAnimation(prop, new DoubleAnimation(to, Dur(ms)));
        }
    }
}
