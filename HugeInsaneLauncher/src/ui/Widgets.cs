// HugeLauncher.Ui - 通用控件：配色/扁平按钮/圆形按钮/复选框/深色下拉框/深色列表/页签栏
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace HugeLauncher.Ui
{
    // 全局配色（深/亮双套色板；Palette.Apply 切换，主窗负责重建 UI）
    static class Palette
    {
        public static Color BgTop = Color.FromRgb(0x1B, 0x1E, 0x27);
        public static Color BgBottom = Color.FromRgb(0x14, 0x16, 0x1D);
        public static Color Panel = Color.FromRgb(0x20, 0x23, 0x2D);
        public static Color Input = Color.FromRgb(0x00, 0x00, 0x00);   // 文本框底色（深色=黑底白字，亮色=白底黑字）
        public static Color Btn = Color.FromRgb(0x2A, 0x2E, 0x3A);
        public static Color BtnHover = Color.FromRgb(0x35, 0x3A, 0x49);
        public static Color Text = Color.FromRgb(0xE8, 0xE8, 0xE8);
        public static Color Dim = Color.FromRgb(0x8A, 0x90, 0xA0);
        public static Color Line = Color.FromRgb(0x3A, 0x3F, 0x4E);
        public static Color Green = Color.FromRgb(0x55, 0xC4, 0x5A);
        public static Color Yellow = Color.FromRgb(0xE0, 0xC5, 0x3C);
        public static Color Red = Color.FromRgb(0xD9, 0x53, 0x4F);
        public static Color Dark = Color.FromRgb(0x14, 0x16, 0x1D);   // 主题色按钮上的文字色（两模式都用深色字）
        public static Color Tile = Color.FromRgb(0x24, 0x28, 0x36);   // 大瓷砖底色：深色=略浅于背景，亮色=略深于背景

        public static bool Light;

        // 玻璃基调色：暗模式=白（罩层提亮），亮模式=黑（罩层压暗）——iOS 亮色系玻璃配方，
        // 所有玻璃罩/描边走 GlassA 取色，白底上不再隐形
        public static byte GlassR = 0xFF, GlassG = 0xFF, GlassB = 0xFF;
        public static Color GlassA(byte a) { return Color.FromArgb(a, GlassR, GlassG, GlassB); }

        public static void Apply(bool light)
        {
            Light = light;
            GlassR = GlassG = GlassB = light ? (byte)0x00 : (byte)0xFF;
            // 下拉框/列表框的模板是静态缓存、画刷首次使用即冻结——换色板必须清缓存，下个控件用新色重建
            DarkComboBox.ResetShared();
            DarkListBox.ResetShared();
            if (light)
            {
                BgTop = Color.FromRgb(0xF5, 0xF7, 0xFB);
                BgBottom = Color.FromRgb(0xE9, 0xEC, 0xF3);
                Panel = Color.FromRgb(0xFF, 0xFF, 0xFF);
                Input = Color.FromRgb(0xFF, 0xFF, 0xFF);
                Btn = Color.FromRgb(0xE6, 0xE9, 0xF1);
                BtnHover = Color.FromRgb(0xD7, 0xDC, 0xE8);
                Text = Color.FromRgb(0x23, 0x25, 0x2C);
                Dim = Color.FromRgb(0x6B, 0x72, 0x82);
                Line = Color.FromRgb(0xCB, 0xD0, 0xDC);
                Green = Color.FromRgb(0x3E, 0x9E, 0x4C);   // 白底上语义色压深一档保证可读
                Yellow = Color.FromRgb(0xB8, 0x96, 0x1E);
                Red = Color.FromRgb(0xC8, 0x3E, 0x3A);
                Tile = Color.FromRgb(0xE6, 0xEA, 0xF2);   // 亮模式：白背景上压深一档
            }
            else
            {
                BgTop = Color.FromRgb(0x1B, 0x1E, 0x27);
                BgBottom = Color.FromRgb(0x14, 0x16, 0x1D);
                Panel = Color.FromRgb(0x20, 0x23, 0x2D);
                Input = Color.FromRgb(0x00, 0x00, 0x00);
                Btn = Color.FromRgb(0x2A, 0x2E, 0x3A);
                BtnHover = Color.FromRgb(0x35, 0x3A, 0x49);
                Text = Color.FromRgb(0xE8, 0xE8, 0xE8);
                Dim = Color.FromRgb(0x8A, 0x90, 0xA0);
                Line = Color.FromRgb(0x3A, 0x3F, 0x4E);
                Green = Color.FromRgb(0x55, 0xC4, 0x5A);
                Yellow = Color.FromRgb(0xE0, 0xC5, 0x3C);
                Red = Color.FromRgb(0xD9, 0x53, 0x4F);
                Tile = Color.FromRgb(0x24, 0x28, 0x36);   // 深模式：比背景浅一档
            }
            // 流动史莱姆开启：中层（瓷砖/面板/输入框/按钮）半透明化，透出底下色块 = 磨砂
            if (Slime.On)
            {
                byte a = (byte)Math.Max(40, Math.Min(235, Slime.FrostMid * 255 / 100));
                Tile = Color.FromArgb(a, Tile.R, Tile.G, Tile.B);
                Panel = Color.FromArgb(a, Panel.R, Panel.G, Panel.B);
                Input = Color.FromArgb(a, Input.R, Input.G, Input.B);
                Btn = Color.FromArgb(a, Btn.R, Btn.G, Btn.B);
                BtnHover = Color.FromArgb(a, BtnHover.R, BtnHover.G, BtnHover.B);
            }
            // 流光玻璃：中层卡片/面板也半透明，透出背后的模糊壁纸，才叫「玻璃」而不是实心色块
            if (Glass.On && !Frost.On)
            {
                byte a = Light ? (byte)196 : (byte)146;
                Tile = Color.FromArgb(a, Tile.R, Tile.G, Tile.B);
                Panel = Color.FromArgb((byte)Math.Min(255, a + 24), Panel.R, Panel.G, Panel.B);
                Btn = Color.FromArgb(a, Btn.R, Btn.G, Btn.B);
                BtnHover = Color.FromArgb((byte)Math.Min(255, a + 30), BtnHover.R, BtnHover.G, BtnHover.B);
            }
            // 霜面：面板/输入/按钮统一半透明，透出底下的壁纸模糊层
            if (Frost.On)
            {
                if (Light)
                {
                    Panel = Color.FromArgb(150, 255, 255, 255);
                    Tile = Color.FromArgb(120, 255, 255, 255);
                    Input = Color.FromArgb(170, 255, 255, 255);
                    Btn = Color.FromArgb(105, 255, 255, 255);
                    BtnHover = Color.FromArgb(150, 255, 255, 255);
                    Line = Color.FromArgb(70, 0, 0, 0);
                }
                else
                {
                    Panel = Color.FromArgb(40, 255, 255, 255);
                    Tile = Color.FromArgb(26, 255, 255, 255);
                    Input = Color.FromArgb(90, 8, 10, 14);
                    Btn = Color.FromArgb(22, 255, 255, 255);
                    BtnHover = Color.FromArgb(46, 255, 255, 255);
                    Line = Color.FromArgb(54, 255, 255, 255);
                }
            }
        }

        public static SolidColorBrush B(Color c) { return new SolidColorBrush(c); }

        // 变亮（用于主题色悬停）
        public static Color Lighten(Color c, double f)
        {
            byte r = (byte)Math.Min(255, c.R + (255 - c.R) * f);
            byte g = (byte)Math.Min(255, c.G + (255 - c.G) * f);
            byte b = (byte)Math.Min(255, c.B + (255 - c.B) * f);
            return Color.FromRgb(r, g, b);
        }

        // 窗口背景渐变
        public static LinearGradientBrush WindowBg()
        {
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(0, 1);
            g.GradientStops.Add(new GradientStop(BgTop, 0));
            g.GradientStops.Add(new GradientStop(BgBottom, 1));
            return g;
        }

        // 史莱姆开启时的压色罩：半透明版窗口渐变，盖在色块上保证文字可读（又能看见流动）
        public static LinearGradientBrush WindowBgVeil()
        {
            byte aTop = Light ? (byte)0xB8 : (byte)0x8C;
            byte aBot = Light ? (byte)0xC8 : (byte)0x9E;
            var g = new LinearGradientBrush();
            g.StartPoint = new Point(0, 0);
            g.EndPoint = new Point(0, 1);
            g.GradientStops.Add(new GradientStop(Color.FromArgb(aTop, BgTop.R, BgTop.G, BgTop.B), 0));
            g.GradientStops.Add(new GradientStop(Color.FromArgb(aBot, BgBottom.R, BgBottom.G, BgBottom.B), 1));
            return g;
        }
    }

    // 布局小助手
    static class UiHelpers
    {
        public static RowDefinition AutoRow() { return new RowDefinition { Height = GridLength.Auto }; }
        public static RowDefinition StarRow() { return new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }; }
        public static ColumnDefinition AutoCol() { return new ColumnDefinition { Width = GridLength.Auto }; }
        public static ColumnDefinition StarCol() { return new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }; }
    }

    // 无边框扁平按钮：悬停 200ms 颜色渐变，支持开关（Toggle）模式
    // 液态玻璃开：Squircle 玻璃脸（快照/模拟底 + 颜色罩 + 高光罩 + 边缘受光 + 软阴影），
    // 悬停/按下走弹簧缩放（粘滞+回弹）；关：原工业风一个像素不动
    class FlatButton : UserControl
    {
        public event EventHandler Click;
        public event EventHandler CheckedChanged;

        Decorator face;          // Border（普通）/ SquircleBorder（玻璃）
        TextBlock label;
        SolidColorBrush bgBrush; // 普通=整底；玻璃=颜色罩层
        SolidColorBrush fgBrush;
        ScaleTransform scale;
        Border sheenLayer;       // 玻璃高光罩
        Border padBox;           // 玻璃脸的文字层（padding 直接改它，不挖子元素索引）
        Color normalBg, hoverBg, normalFg, hoverFg;
        bool enabledFlag = true;
        bool toggleMode;
        bool radioMode;   // 单选语义：已选中再点不取消（导航按钮用）
        bool isChecked;
        string aniKey;
        Thickness pad = new Thickness(14, 7, 14, 7);
        CornerRadius radius = new CornerRadius(5);
        Spring pressSpring;      // 玻璃按压缩放弹簧
        public bool GlassFixed;  // 玻璃底用活底透镜（固定铬件）；false=模拟渐变（滚动内容）
        public double HoverScale = 1.035;   // 悬停放大倍数（小标签可以调小）
        Visual glassSource;
        // 活底来源图层（玻璃件正下方的前台层，如左栏史蒂夫层）：赋值即重建脸立刻生效
        public Visual GlassSource
        {
            get { return glassSource; }
            set { glassSource = value; if (face != null && Glass.On) BuildFace(); }
        }
        bool circleInGlass;
        // 玻璃模式下强制正圆（标题栏 min/close 用）；赋值即重建脸，半径立刻生效
        public bool CircleInGlass
        {
            get { return circleInGlass; }
            set { if (circleInGlass == value) return; circleInGlass = value; if (face != null) BuildFace(); }
        }
        // 玻璃模式下当纯文字标签用：不画玻璃脸，选中指示交给外部滑动 pill（三级标签用）
        public bool TabInGlass
        {
            get { return tabInGlass; }
            set { if (tabInGlass == value) return; tabInGlass = value; if (face != null) BuildFace(); }
        }
        bool tabInGlass;
        Color? customBorder;     // SetBorder 存的描边色（玻璃/普通两脸都要用）

        public FlatButton(string text)
        {
            normalBg = Palette.Btn;
            hoverBg = Palette.BtnHover;
            normalFg = Palette.Text;
            hoverFg = Palette.Text;
            bgBrush = Palette.B(normalBg);
            fgBrush = Palette.B(normalFg);
            aniKey = "fb" + GetHashCode();
            pressSpring = new Spring(1) { Stiffness = 300, Damping = 13 };
            label = new TextBlock();
            label.Text = text;
            label.Foreground = fgBrush;
            label.FontSize = 12;
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            scale = new ScaleTransform(1, 1);
            BuildFace();
            Cursor = Cursors.Hand;
            MouseEnter += OnMouseEnter;
            MouseLeave += OnMouseLeave;
            MouseLeftButtonDown += OnDown;
            MouseLeftButtonUp += OnUp;
            HoverMagnet.Attach(this);   // 悬停惯性：跟着鼠标被拖一点，停下弹回
            ThemeManager.GlassChanged += delegate { BuildFace(); };
            ThemeManager.SkinChanged += delegate { BuildFace(); };
        }

        // 玻璃罩层用半透明色，普通模式用原色；全透明底（对话框 ×、描边按钮）玻璃下保持透明，
        // 否则透明底会被强制罩上黑，糊成一块深色方坨；默认深色按钮降到 40/255——玻璃本体就是按钮，
        // 罩色只是一层雾，不再像「旧按钮压在玻璃里」；主题色按钮（启动！！）保持 120 保证辨识度
        Color FaceColor(Color c)
        {
            if (!Glass.On) return c;
            if (c.A == 0) return c;
            byte a = (c.R == Palette.Btn.R && c.G == Palette.Btn.G && c.B == Palette.Btn.B) ? (byte)40 : (byte)120;
            return Color.FromArgb(a, c.R, c.G, c.B);
        }

        void BuildFace()
        {
            // 重建前先把文字从旧脸上摘下：一个元素同时只能有一个逻辑父元素，
            // 否则玻璃开关切换时第二次进这里会抛「已是另一个元素的逻辑子元素」
            var oldPar = label.Parent as Decorator;
            if (oldPar != null) oldPar.Child = null;
            if (Frost.On)
            {
                if (tabInGlass)
                {
                    var tb0 = new Border();
                    tb0.Padding = pad;
                    tb0.Child = label;
                    face = tb0;
                    sheenLayer = null;
                    padBox = tb0;
                    ApplyRadius();
                    face.RenderTransform = scale;
                    face.RenderTransformOrigin = new Point(0.5, 0.5);
                    Content = face;
                    RefreshStateColors();
                    return;
                }
                var b = new Border();
                b.Background = bgBrush;
                b.BorderBrush = customBorder.HasValue ? Palette.B(customBorder.Value) : Frost.Edge;
                b.BorderThickness = new Thickness(1);
                b.Padding = pad;
                b.Child = label;
                face = b;
                sheenLayer = null;
                padBox = b;
                ApplyRadius();
                face.RenderTransform = scale;
                face.RenderTransformOrigin = new Point(0.5, 0.5);
                Content = face;
                RefreshStateColors();
                return;
            }
            if (Glass.On)
            {
                var fr = FaceRadius();
                face = null;
                Grid host = null;
                double innerR = fr.TopLeft;
                if (tabInGlass)
                {
                    // 纯文字标签脸：透明底 + 文字（滑动玻璃 pill 在背后指示选中态）
                    var tb = new Border();
                    tb.Padding = pad;
                    tb.Child = label;
                    face = tb;
                    sheenLayer = null;
                    padBox = tb;
                    ApplyRadius();
                    face.RenderTransform = scale;
                    face.RenderTransformOrigin = new Point(0.5, 0.5);
                    Content = face;
                    RefreshStateColors();
                    return;
                }
                if (GlassFixed && GlassSource != null)
                {
                    // 活底多环折射：底 = 正下方前台图层的实时画面（史蒂夫的脚在按钮底下真的会动），
                    // 折射带连续弯曲无接缝；源图层透明处 = 真透视，静态背景免费透出
                    Grid faceHost;
                    var rings = Glass.LensRings(GlassSource, fr.TopLeft, Glass.BandPx, out faceHost);
                    if (rings != null)
                    {
                        face = rings;   // 边缘受光由 LensRings 顶层 rim 负责（投影已全局撤除：核显帧率）
                        host = faceHost;
                        if (faceHost.Tag is double) innerR = (double)faceHost.Tag;
                    }
                }
                SquircleBorder simRim = null;
                if (face == null)
                {
                    // 滚动内容/无源图层：模拟玻璃夹心（静态底纹，不取景）
                    var sq = new SquircleBorder();
                    sq.Background = Glass.SimulatedLensEdge();
                    face = sq;
                    var g0 = new Grid();
                    // 内板零边距铺满：Margin=2 的旧写法会在罩色淡的按钮（默认色 α40）上
                    // 透出内板边缘 = 「内芯+外圈」两层；铺满后梯度均匀无接缝
                    var lensIn = new SquircleBorder();
                    lensIn.Background = Glass.SimulatedFace(0.05);
                    lensIn.CornerRadius = fr;
                    lensIn.IsHitTestVisible = false;
                    g0.Children.Add(lensIn);
                    sq.Child = g0;
                    host = g0;
                    // 描边不做在 sq 上：Border.BorderThickness 会把内容向内挤 1px，颜色罩比外框小一圈，
                    // 中间露出一条半透明缝 = 肉眼看到的「按钮套按钮」两层；改为叠在最上层的独立 rim（LensRings 同款）
                    simRim = new SquircleBorder();
                    // 自定义描边色（如对话框里主题色描边按钮）玻璃下保留，否则用边缘受光刷
                    simRim.BorderBrush = customBorder.HasValue ? Palette.B(customBorder.Value) : Glass.Edge();
                    simRim.BorderThickness = new Thickness(1);
                    simRim.CornerRadius = fr;
                    simRim.IsHitTestVisible = false;
                }
                // 叠层整脸铺满（颜色罩 bgBrush + 高光罩 + 文字）：与外层同曲率同边界，不再有内层板
                var tint = new SquircleBorder();
                tint.Background = bgBrush;
                tint.CornerRadius = new CornerRadius(innerR);
                sheenLayer = new SquircleBorder();
                sheenLayer.Background = Glass.Sheen();
                sheenLayer.CornerRadius = new CornerRadius(innerR);
                sheenLayer.IsHitTestVisible = false;
                var bd = new Border();
                bd.Padding = pad;
                bd.Child = label;
                padBox = bd;
                host.Children.Add(tint);
                host.Children.Add(sheenLayer);
                host.Children.Add(bd);
                if (simRim != null) host.Children.Add(simRim);   // 描边压最顶，与外框同界无缝
            }
            else
            {
                var b = new Border();
                b.Background = bgBrush;
                b.BorderThickness = new Thickness(0);
                b.Padding = pad;
                if (customBorder.HasValue)
                {
                    b.BorderBrush = Palette.B(customBorder.Value);
                    b.BorderThickness = new Thickness(1);
                }
                b.Child = label;
                face = b;
                sheenLayer = null;
                padBox = b;
            }
            ApplyRadius();
            face.RenderTransform = scale;
            face.RenderTransformOrigin = new Point(0.5, 0.5);
            Content = face;
            RefreshStateColors();
        }

        // 玻璃模式一律全胶囊：巨大半径交给几何钳到 min(w,h)/2（iOS 大胶囊）；
        // CircleInGlass（标题栏 min/close）宽高相等 → 钳出来正好正圆
        CornerRadius FaceRadius()
        {
            if (Frost.On) return circleInGlass ? new CornerRadius(10) : radius;
            if (!Glass.On) return radius;
            return new CornerRadius(1000);
        }

        void ApplyRadius()
        {
            var b = face as Border;
            if (b != null) b.CornerRadius = FaceRadius();
        }

        // 按当前开关/选中态刷新颜色（普通整底 / 玻璃罩层半透明）
        void RefreshStateColors()
        {
            if (Frost.On)
            {
                Color acc = ThemeManager.CurrentColor;
                bool accent = normalBg.R == acc.R && normalBg.G == acc.G && normalBg.B == acc.B;
                Color fb = accent
                    ? Color.FromArgb(150, acc.R, acc.G, acc.B)
                    : ((SolidColorBrush)Frost.Button).Color;
                bgBrush.Color = isChecked ? Color.FromArgb(180, acc.R, acc.G, acc.B) : fb;
                fgBrush.Color = accent ? Colors.White : normalFg;
                return;
            }
            if (isChecked)
            {
                bgBrush.Color = Glass.On ? Color.FromArgb(190, ThemeManager.CurrentColor.R, ThemeManager.CurrentColor.G, ThemeManager.CurrentColor.B) : ThemeManager.CurrentColor;
                // 玻璃选中态文字：暗模式白字，亮模式罩层变浅必须深色字（否则白字糊进浅主题色）
                fgBrush.Color = Glass.On && !Palette.Light ? Colors.White : Palette.Dark;
            }
            else
            {
                bgBrush.Color = FaceColor(normalBg);
                fgBrush.Color = normalFg;
            }
        }

        public string Text { get { return label.Text; } set { label.Text = value; } }
        public bool IsChecked { get { return isChecked; } }

        public void SetColors(Color bg, Color hover, Color fg, Color hfg)
        {
            normalBg = bg;
            hoverBg = hover;
            normalFg = fg;
            hoverFg = hfg;
            if (Frost.On) { RefreshStateColors(); return; }
            if (!isChecked)
            {
                bgBrush.Color = FaceColor(normalBg);
                fgBrush.Color = normalFg;
            }
        }

        public void SetBorder(Color c)
        {
            customBorder = c;   // 存下来：玻璃脸重建（含模式切换）时也要用
            var b = face as Border;
            if (b != null && !Glass.On)
            {
                b.BorderBrush = Palette.B(c);
                b.BorderThickness = new Thickness(1);
            }
        }

        public void SetPadding(Thickness t) { pad = t; ReapplyPadding(); }
        void ReapplyPadding()
        {
            // 玻璃/普通两脸都在 BuildFace 里把文字层记进 padBox，直接改它——
            // 旧写法按子元素索引挖（玻璃脸结构一变就静默失效，启动按钮内边距一直没生效）
            if (padBox != null) padBox.Padding = pad;
        }
        public void SetCornerRadius(CornerRadius r) { radius = r; ApplyRadius(); }
        public void SetFontSize(double s) { label.FontSize = s; }
        public void SetBold(bool b) { label.FontWeight = b ? FontWeights.Bold : FontWeights.Normal; }
        public void SetToggle(bool t) { toggleMode = t; }
        public void SetRadio(bool r) { radioMode = r; }

        // 主题换色时的平滑过渡（启动按钮跟随主题用）
        public void AnimateColors(Color bg, Color hover, Color fg, Color hfg, int ms)
        {
            normalBg = bg;
            hoverBg = hover;
            normalFg = fg;
            hoverFg = hfg;
            if (Frost.On) { RefreshStateColors(); return; }
            if (!isChecked)
                Ani.Start(aniKey + "c", Ani.AaColor(bgBrush, FaceColor(bg), ms), Ani.AaColor(fgBrush, fg, ms));
            else
                RefreshStateColors();
        }

        public void SetChecked(bool v, bool raise)
        {
            if (isChecked == v) return;
            isChecked = v;
            RefreshStateColors();
            if (raise)
            {
                var h = CheckedChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetEnabled(bool v)
        {
            enabledFlag = v;
            Opacity = v ? 1.0 : 0.4;
            Cursor = v ? Cursors.Hand : Cursors.Arrow;
        }

        // 玻璃弹簧缩放应用（按下 0.955 / 悬停 1.03 / 松开回 1 带过冲）
        void ApplyPressSpring(double v, double velocity)
        {
            scale.ScaleX = v;
            scale.ScaleY = v;
        }

        void OnMouseEnter(object sender, MouseEventArgs e)
        {
            if (!enabledFlag || isChecked) return;
            Color to = Frost.On ? ((SolidColorBrush)Frost.ButtonHover).Color : FaceColor(hoverBg);
            Ani.Start(aniKey + "c", Ani.AaColor(bgBrush, to, 100), Ani.AaColor(fgBrush, hoverFg, 100));
            SpringEngine.Go(pressSpring, HoverScale, ApplyPressSpring);
        }

        void OnMouseLeave(object sender, MouseEventArgs e)
        {
            if (!enabledFlag || isChecked) return;
            Color to = Frost.On ? ((SolidColorBrush)Frost.Button).Color : FaceColor(normalBg);
            Ani.Start(aniKey + "c", Ani.AaColor(bgBrush, to, 200), Ani.AaColor(fgBrush, normalFg, 200));
            SpringEngine.Go(pressSpring, 1.0, ApplyPressSpring);
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            if (!enabledFlag) return;
            CaptureMouse();
            // 两种皮肤统一走同一根弹簧：按下 0.955，松手回 1（阻尼小，自然过冲）
            SpringEngine.Go(pressSpring, 0.955, ApplyPressSpring);
            e.Handled = true;
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!enabledFlag) return;
            if (IsMouseCaptured) ReleaseMouseCapture();
            SpringEngine.Go(pressSpring, IsMouseOver ? HoverScale : 1.0, ApplyPressSpring);
            if (IsMouseOver)
            {
                if (toggleMode)
                {
                    // 单选模式：已选中再点不取消；普通开关：取反
                    if (!(radioMode && isChecked)) SetChecked(!isChecked, true);
                }
                var h = Click;
                if (h != null) h(this, EventArgs.Empty);
            }
        }
    }

    // 小型复选框（深色方框 + 主题色勾）
    // 液态玻璃开（或 ForceGlass=true，如「液态玻璃」总开关自身）：换装 iOS 开关——
    // 46×28 胶囊轨道（玻璃底+边缘受光），白色圆钮带阴影，弹簧滑动+果冻形变，可拖钮过中线翻转
    class MiniCheckBox : UserControl
    {
        public event EventHandler Changed;

        bool forceGlass;
        // 任何模式都用玻璃开关样式（液态玻璃总开关用）；切换立即重建脸
        public bool ForceGlass
        {
            get { return forceGlass; }
            set { if (forceGlass == value) return; forceGlass = value; BuildFace(); }
        }

        Border box;               // 普通模式：方框
        TextBlock tick;
        bool isChecked;
        bool enabledFlag = true;

        // 玻璃开关脸
        Grid switchRoot;
        Grid trackLayer;          // 轨道层：圆钮活底透镜的来源（绝不含圆钮，否则循环=黑洞）
        SquircleBorder track;
        Border knobRings;         // 圆钮活底多环透镜（常驻；按压只动白雾罩，不换底）
        SolidColorBrush trackBrush;
        Border knob;
        SolidColorBrush knobBrush;   // 圆钮填充（按压玻璃态做 alpha 动画）
        TranslateTransform knobTf;
        ScaleTransform knobScale;
        Spring knobSpring;
        Spring knobSizeSpring;    // 按压放大弹簧（iOS 捏起感）
        double sizeK = 1.0, jellyKx, jellyKy;  // 放大倍率+果冻分量（合成写 knobScale，互不覆盖）
        bool faceBuilt;           // 首次建脸=直接落位；重建=从弹簧当前值续走
        bool knobDragging;
        double knobDragStart;     // 按下时的指针 X（控件事例坐标系）
        const double SwW = 46, SwH = 28, KnobD = 22, KnobPad = 3; // iOS 51:31 比例缩放
        static double KnobMax { get { return SwW - KnobD - KnobPad * 2; } }

        bool SwitchMode { get { return ForceGlass || Glass.On || Frost.On; } }

        public MiniCheckBox()
        {
            knobSpring = new Spring(0) { Stiffness = 260, Damping = 13.5 };
            knobSizeSpring = new Spring(1) { Stiffness = 320, Damping = 13 };
            BuildFace();
            Cursor = Cursors.Hand;
            MouseLeftButtonUp += OnClick;
            ThemeManager.GlassChanged += delegate { BuildFace(); };
            ThemeManager.SkinChanged += delegate { BuildFace(); };
        }

        void BuildFace()
        {
            if (SwitchMode)
            {
                // 轨道
                track = new SquircleBorder();
                track.Width = SwW;
                track.Height = SwH;
                track.CornerRadius = new CornerRadius(SwH / 2);
                // 重建时从旧轨色起步（之后 RefreshTrackColor(true) 平滑过渡到新态，切换不瞬跳）
                Color trackFrom = trackBrush != null ? trackBrush.Color : Palette.GlassA(56);
                trackBrush = Palette.B(trackFrom);
                track.Background = trackBrush;
                track.BorderBrush = Frost.On ? Frost.Edge : (Glass.Edge() ?? Palette.B(Palette.Line));
                track.BorderThickness = new Thickness(1);
                trackLayer = new Grid();          // 轨道层：圆钮活底透镜的来源（绝不含圆钮）
                trackLayer.Children.Add(track);
                // 圆钮（活底夹心：多环透镜常驻 + 白雾罩 knobBrush；按压=白雾变淡露活透镜，不换底不丢玻璃）
                knob = new Border();
                knob.Width = KnobD;
                knob.Height = KnobD;
                knob.CornerRadius = new CornerRadius(KnobD / 2);
                knobBrush = Palette.B(Colors.White);
                knob.Background = null;   // 底交给夹心层
                if (Frost.On)
                {
                    knobBrush.Color = ((SolidColorBrush)Frost.TileStrong).Color;
                    var kh = new SquircleBorder();
                    kh.CornerRadius = new CornerRadius(100);
                    kh.Background = knobBrush;
                    kh.BorderBrush = Frost.Edge;
                    kh.BorderThickness = new Thickness(1);
                    kh.IsHitTestVisible = false;
                    knob.Child = kh;
                    knobRings = null;
                }
                else
                {
                    var knobHaze = new SquircleBorder();
                    knobHaze.CornerRadius = new CornerRadius(100);
                    knobHaze.Background = knobBrush;
                    knobHaze.IsHitTestVisible = false;
                    Grid knobHost;
                    knobRings = Glass.LensRings(trackLayer, 100, 4, out knobHost);
                    if (knobRings != null)
                    {
                        knobHost.Children.Add(knobHaze);
                        knob.Child = knobRings;
                    }
                    else
                    {
                        var kg = new Grid();
                        kg.Children.Add(knobHaze);
                        knob.Child = kg;
                    }
                }
                knob.HorizontalAlignment = HorizontalAlignment.Left;
                knob.VerticalAlignment = VerticalAlignment.Center;
                knob.Margin = new Thickness(KnobPad, 0, 0, 0);
                // 无投影：DropShadowEffect 已全局撤除（核显帧率杀手）
                // 重建不掐断进行中的弹簧：新钮从弹簧当前值续走（液态玻璃总开关切换不再瞬跳）
                knobTf = new TranslateTransform(faceBuilt ? knobSpring.Value : (isChecked ? KnobMax : 0), 0);
                knobScale = new ScaleTransform(1, 1);
                var tg = new TransformGroup();
                tg.Children.Add(knobScale);
                tg.Children.Add(knobTf);
                knob.RenderTransform = tg;
                knob.RenderTransformOrigin = new Point(0.5, 0.5);
                switchRoot = new Grid();
                switchRoot.Width = SwW;
                switchRoot.Height = SwH;
                switchRoot.Children.Add(trackLayer);
                switchRoot.Children.Add(knob);
                Content = switchRoot;
                knob.MouseLeftButtonDown += OnKnobDown;
                knob.MouseMove += OnKnobMove;
                knob.MouseLeftButtonUp += OnKnobUp;
                if (faceBuilt)
                {
                    // 重建：弹簧续走到目标位 + 轨色动画过渡
                    SpringEngine.Go(knobSpring, isChecked ? KnobMax : 0, ApplyKnob);
                    RefreshTrackColor(true);
                }
                else
                {
                    faceBuilt = true;
                    knobSpring.SnapTo(isChecked ? KnobMax : 0);
                    RefreshTrackColor(false);
                }
            }
            else
            {
                tick = new TextBlock();
                tick.Text = "✓";
                tick.Foreground = ThemeManager.Accent;
                tick.FontSize = 11;
                tick.FontWeight = FontWeights.Bold;
                tick.HorizontalAlignment = HorizontalAlignment.Center;
                tick.VerticalAlignment = VerticalAlignment.Center;
                tick.Visibility = isChecked ? Visibility.Visible : Visibility.Collapsed;
                box = new Border();
                box.Width = 16;
                box.Height = 16;
                box.CornerRadius = new CornerRadius(3);
                box.BorderBrush = isChecked ? ThemeManager.Accent : Palette.B(Palette.Line);
                box.BorderThickness = new Thickness(1.5);
                box.Background = Palette.B(Palette.Input);
                box.Child = tick;
                Content = box;
            }
        }

        void RefreshTrackColor(bool animate)
        {
            if (!SwitchMode) return;
            var to = isChecked
                ? Color.FromArgb(170, ThemeManager.CurrentColor.R, ThemeManager.CurrentColor.G, ThemeManager.CurrentColor.B)
                : (Frost.On ? Color.FromArgb(90, 255, 255, 255) : Palette.GlassA(56));
            if (animate) Ani.Start("mck" + GetHashCode(), Ani.AaColor(trackBrush, to, 180));
            else trackBrush.Color = to;
        }

        // 圆钮位置弹簧应用：位移 + 速度果冻（与按压放大合成，互不覆盖）；活底透镜由逐帧对齐跟手
        void ApplyKnob(double v, double velocity)
        {
            knobTf.X = v;
            double k = Math.Min(0.22, Math.Abs(velocity) * 0.00035);
            jellyKx = k; jellyKy = -k * 0.6;
            ComposeKnobScale();
        }

        void ComposeKnobScale()
        {
            if (knobScale == null) return;
            knobScale.ScaleX = sizeK + jellyKx;
            knobScale.ScaleY = sizeK + jellyKy;
        }

        void ApplyKnobSize(double v, double velocity)
        {
            sizeK = v;
            ComposeKnobScale();
        }

        void OnClick(object sender, MouseButtonEventArgs e)
        {
            if (!enabledFlag || knobDragging) return;
            SetChecked(!isChecked, true);
        }

        // 拖钮：按下记录起点，拖动 1:1 跟手（不粘），松手过中线吸附
        void OnKnobDown(object sender, MouseButtonEventArgs e)
        {
            if (!enabledFlag) return;
            knobDragging = true;
            knobDragStart = e.GetPosition(this).X - knobTf.X;
            knob.CaptureMouse();
            SetKnobGlass(true);       // 按住=捏起磨砂玻璃
            SpringEngine.Go(knobSizeSpring, 1.22, ApplyKnobSize);  // 捏起放大
            e.Handled = true;
        }

        void OnKnobMove(object sender, MouseEventArgs e)
        {
            if (!knobDragging) return;
            double x = e.GetPosition(this).X - knobDragStart;
            if (x < 0) x *= 0.3;            // 越界阻力
            if (x > KnobMax) x = KnobMax + (x - KnobMax) * 0.3;
            knobSpring.SnapTo(x);           // 拖动直达，松手才上弹簧
            knobTf.X = x;
            e.Handled = true;
        }

        void OnKnobUp(object sender, MouseButtonEventArgs e)
        {
            if (!knobDragging) return;
            knobDragging = false;
            knob.ReleaseMouseCapture();
            SetKnobGlass(false);      // 松手恢复不透明白圆
            SpringEngine.Go(knobSizeSpring, 1.0, ApplyKnobSize);   // 回弹（阻尼小→可见 Q 弹）
            double x = knobTf.X;
            bool shouldCheck;
            if (Math.Abs(x - (isChecked ? KnobMax : 0)) < 4) shouldCheck = !isChecked; // 没拖动=点击
            else shouldCheck = x > KnobMax / 2;
            SetChecked(shouldCheck, true);
            e.Handled = true;
        }

        // 按压圆钮=捏起一块玻璃：白雾罩渐隐成 35% 透明露出底下常驻活透镜 + 受光描边浮现；松开还原
        void SetKnobGlass(bool on)
        {
            if (!SwitchMode || knobBrush == null) return;
            Ani.Start("mkg" + GetHashCode(), Ani.AaColor(knobBrush,
                on ? Color.FromArgb(92, 0xFF, 0xFF, 0xFF) : Colors.White, 120));
            if (knobRings != null)
            {
                knobRings.BorderBrush = on ? Glass.Edge() : null;
                knobRings.BorderThickness = new Thickness(on ? 1 : 0);
            }
        }

        public bool IsChecked { get { return isChecked; } }

        public void SetChecked(bool v, bool raise)
        {
            if (isChecked == v)
            {
                if (SwitchMode) SpringEngine.Go(knobSpring, v ? KnobMax : 0, ApplyKnob); // 拖错方向也弹回
                return;
            }
            isChecked = v;
            if (SwitchMode)
            {
                RefreshTrackColor(true);
                SpringEngine.Go(knobSpring, v ? KnobMax : 0, ApplyKnob);
            }
            else
            {
                tick.Visibility = v ? Visibility.Visible : Visibility.Collapsed;
                box.BorderBrush = v ? ThemeManager.Accent : Palette.B(Palette.Line);
                if (v && ThemeManager.AnimOn)
                {
                    // 勾选弹跳：0.5 → 1 回弹出现
                    var s = Ani.Sc(tick);
                    s.ScaleX = 0.5;
                    s.ScaleY = 0.5;
                    Ani.Start("mc" + GetHashCode(), Ani.AaScale(tick, 0.5, 200, new AniEaseOutBack(Ani.Middle)));
                }
            }
            if (raise)
            {
                var h = Changed;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public void SetEnabled(bool v)
        {
            enabledFlag = v;
            Opacity = v ? 1.0 : 0.4;
            Cursor = v ? Cursors.Hand : Cursors.Arrow;
        }
    }

    // 平滑滚动容器：滚轮改为缓动动画滚动（拖滚动条不受影响；动画关闭时退回原生行为）
    // 液态玻璃开：滚过顶/底触发橡皮筋——内容按 0.35 阻力被拖动，松手弹簧吸回
    class SmoothScrollViewer : ScrollViewer
    {
        double targetOffset = -1;
        System.Windows.Threading.DispatcherTimer timer;
        Spring rubber;                              // 橡皮筋位移弹簧（px）
        TranslateTransform rubberTf;
        System.Windows.Threading.DispatcherTimer rubberIdle;  // 停止滚动 140ms 后吸回

        public SmoothScrollViewer()
        {
            // ScrollViewer 默认 Focusable=true 且自带虚线焦点样式：点击内容时焦点落上来，
            // 瓷砖里就浮出一个大虚线框。滚轮滚动走 MouseWheel 悬停事件，不需要焦点，直接禁掉
            Focusable = false;
            FocusVisualStyle = null;
            rubber = new Spring(0) { Stiffness = 190, Damping = 14 };
            timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(15);
            timer.Tick += OnTick;
            rubberIdle = new System.Windows.Threading.DispatcherTimer();
            rubberIdle.Interval = TimeSpan.FromMilliseconds(140);
            rubberIdle.Tick += delegate { rubberIdle.Stop(); SpringEngine.Go(rubber, 0, ApplyRubber); };
            // 非动画滚动（拖条/代码跳转）时同步目标；内容收缩时钳制目标，防止回跳
            ScrollChanged += delegate
            {
                // 动画途中绝不碰 targetOffset：异步加载/入场动画会让内容瞬时收缩，
                // 此时把目标钳到 0，动画接着就把用户拽回顶部（"滚动自动回顶部"元凶之二）
                if (timer.IsEnabled) return;
                targetOffset = VerticalOffset;
                if (targetOffset > ScrollableHeight) targetOffset = ScrollableHeight;
            };
        }

        void ApplyRubber(double v, double velocity)
        {
            var fe = Content as FrameworkElement;
            if (fe == null) return;
            var tf = fe.RenderTransform as TranslateTransform;
            if (tf == null)
            {
                if (fe.RenderTransform != null) return;   // 内容自带变换则不抢
                tf = new TranslateTransform();
                rubberTf = tf;
                fe.RenderTransform = tf;
            }
            tf.Y = -v;
        }

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            if (!ThemeManager.AnimOn) { base.OnMouseWheel(e); return; }
            e.Handled = true;
            if (!timer.IsEnabled) targetOffset = VerticalOffset;
            double raw = targetOffset - e.Delta * 0.6; // 一格滚轮(120) ≈ 72px，连续滚动会叠加目标
            double clamped = raw;
            if (clamped < 0) clamped = 0;
            if (clamped > ScrollableHeight) clamped = ScrollableHeight;
            targetOffset = clamped;
            // 橡皮筋：越过端点的部分以 0.35 阻力转成内容位移
            if (Glass.On && raw != clamped)
            {
                double over = (raw - clamped) * 0.35;
                double rt = rubber.Target + over;
                if (rt > 48) rt = 48;
                if (rt < -48) rt = -48;
                SpringEngine.Go(rubber, rt, ApplyRubber);
                rubberIdle.Stop();
                rubberIdle.Start();
            }
            timer.Start();
        }

        void OnTick(object sender, EventArgs e)
        {
            // 每 tick 现取上限：ScrollToVerticalOffset 自带钳制，但 diff 收敛判定必须用钳后的目标，
            // 否则目标悬在界外时计时器永远不停（旧做法在 ScrollChanged 里钳目标，才有回顶 bug）
            double tgt = targetOffset > ScrollableHeight ? ScrollableHeight : targetOffset;
            double diff = tgt - VerticalOffset;
            if (Math.Abs(diff) < 0.5)
            {
                ScrollToVerticalOffset(tgt);
                timer.Stop();
                return;
            }
            ScrollToVerticalOffset(VerticalOffset + diff * 0.22); // 缓出插值
        }
    }

    // iOS 式滑条：圆头轨道 + 主题色已走填充 + 白色圆钮（带阴影）
    // 全程弹簧：拖动粘滞跟随、点轨道滑行、到位回弹、移动果冻形变、端点 6px 阻力越界；
    // 液态玻璃关 = 朴素轨道样式，弹簧手感保留（用户点名的平滑滑动两模式共有）
    class GlassSlider : UserControl
    {
        public event EventHandler ValueChanged;

        public double Minimum { get; set; }
        public double Maximum { get; set; }
        public double TickFrequency { get; set; }
        public bool IsSnapToTickEnabled { get; set; }

        double val;
        readonly Canvas canvas;
        readonly SquircleBorder track, fill;
        readonly SolidColorBrush fillBrush;
        readonly Border thumb;
        readonly SolidColorBrush thumbBrush;   // 滑钮白雾罩（按压变淡露出底下常驻活透镜）
        Border thumbRings;                     // 滑钮活底多环透镜（常驻，源=底画布轨道层）
        readonly TranslateTransform thumbTf;
        readonly ScaleTransform thumbScale;
        readonly Spring posSpring;      // 滑钮位置（px）
        readonly Spring sizeSpring;     // 按下放大
        bool dragging;
        bool thumbGlass;                // 按压中=滑钮是透明玻璃态
        const double ThumbD = 22, TrackH = 4;

        public GlassSlider()
        {
            Minimum = 0; Maximum = 100; TickFrequency = 0; IsSnapToTickEnabled = false;
            Height = 28;
            posSpring = new Spring(0) { Stiffness = 210, Damping = 17 };
            sizeSpring = new Spring(1) { Stiffness = 320, Damping = 13 };
            canvas = new Canvas();
            canvas.ClipToBounds = false;

            track = new SquircleBorder();
            track.Height = TrackH;
            track.CornerRadius = new CornerRadius(TrackH / 2);
            fill = new SquircleBorder();
            fill.Height = TrackH;
            fill.CornerRadius = new CornerRadius(TrackH / 2);
            fill.HorizontalAlignment = HorizontalAlignment.Left;
            fillBrush = Palette.B(ThemeManager.CurrentColor);
            ThemeManager.ColorChanged += delegate
            {
                var cc = ThemeManager.CurrentColor;
                fillBrush.Color = Glass.On ? Color.FromArgb(215, cc.R, cc.G, cc.B) : cc;
            };

            thumb = new Border();
            thumb.Width = ThumbD;
            thumb.Height = ThumbD;
            thumb.CornerRadius = new CornerRadius(ThumbD / 2);
            thumbTf = new TranslateTransform(0, 0);
            thumbScale = new ScaleTransform(1, 1);
            var tg = new TransformGroup();
            tg.Children.Add(thumbScale);
            tg.Children.Add(thumbTf);
            thumb.RenderTransform = tg;
            thumb.RenderTransformOrigin = new Point(0.5, 0.5);
            thumb.Cursor = Cursors.Hand;
            thumbBrush = Palette.B(Colors.White);
            thumb.Background = thumbBrush;
            // 点在滑钮上按下：滑钮变透明玻璃（冒泡到 OnDown 开始拖拽，不拦截）
            thumb.MouseLeftButtonDown += delegate { SetThumbGlass(true); };

            BuildFace();
            // 双画布分层：底=轨道+填充（活底来源），顶=滑钮（透镜层绝不进底画布，否则循环=黑洞）
            var fgCanvas = new Canvas();
            fgCanvas.ClipToBounds = false;
            var layers = new Grid();
            layers.Children.Add(canvas);
            layers.Children.Add(fgCanvas);
            canvas.Children.Add(track);
            canvas.Children.Add(fill);
            fgCanvas.Children.Add(thumb);
            Content = layers;

            ThemeManager.GlassChanged += delegate { BuildFace(); };
            ThemeManager.SkinChanged += delegate { BuildFace(); };
            SizeChanged += delegate { LayoutFace(); SnapThumb(); };
            MouseLeftButtonDown += OnDown;
            MouseMove += OnMove;
            MouseLeftButtonUp += OnUp;
            // 不挂 MouseWheel：滑条不吃滚轮，事件自然冒泡给父级滚动容器翻页
        }

        // 玻璃/朴素两张脸（弹簧与事件不变）
        void BuildFace()
        {
            if (Frost.On)
            {
                track.Background = Frost.Input;
                track.BorderBrush = Frost.Edge;
                track.BorderThickness = new Thickness(1);
                var cf = ThemeManager.CurrentColor;
                fillBrush.Color = Color.FromArgb(220, cf.R, cf.G, cf.B);
                thumbBrush.Color = Color.FromArgb(110, 255, 255, 255);
                thumb.Child = null;
                thumbRings = null;
            }
            else if (Glass.On)
            {
                track.Background = Glass.SimulatedFace(0.05);
                track.BorderBrush = Glass.Edge();
                track.BorderThickness = new Thickness(1);
                var c = ThemeManager.CurrentColor;
                fillBrush.Color = Color.FromArgb(215, c.R, c.G, c.B);
                thumbBrush.Color = Colors.White;
                // 滑钮 = 常驻活底多环透镜（源=底画布轨道层）+ 白雾罩：按压只是白雾变淡露出活透镜，
                // 不再按压换底——拖拽全程玻璃都在（逐帧对齐跟手）
                Grid thumbHost;
                thumbRings = Glass.LensRings(canvas, 100, 4, out thumbHost);
                if (thumbRings != null)
                {
                    var thumbHaze = new SquircleBorder();
                    thumbHaze.CornerRadius = new CornerRadius(100);
                    thumbHaze.Background = thumbBrush;
                    thumbHaze.IsHitTestVisible = false;
                    thumbHost.Children.Add(thumbHaze);
                    thumb.Child = thumbRings;
                }
                else thumb.Child = null;
            }
            else
            {
                track.Background = Palette.B(Palette.Line);
                track.BorderThickness = new Thickness(0);
                fillBrush.Color = ThemeManager.CurrentColor;
                thumbBrush.Color = Colors.White;   // 滑钮固定白（iOS 双色模式都是白钮）；不能用 Text——亮模式它是近黑
                thumb.Effect = null;
                thumb.Child = null;
                thumbRings = null;
            }
            thumb.Background = Glass.On ? null : thumbBrush;   // 玻璃模式底交给夹心层
            // 重建脸时复位按压玻璃态，避免切模式中途留下半透明钮
            thumbGlass = false;
            thumb.BorderBrush = null;
            thumb.BorderThickness = new Thickness(0);
            if (Frost.On)
            {
                thumb.Background = thumbBrush;
                thumb.BorderBrush = Frost.Edge;
                thumb.BorderThickness = new Thickness(1);
            }
            if (thumbRings != null) { thumbRings.BorderBrush = null; thumbRings.BorderThickness = new Thickness(0); }
            fill.Background = fillBrush;
            LayoutFace();
        }

        void LayoutFace()
        {
            double w = ActualWidth;
            if (w < 10) return;
            double h = ActualHeight;
            if (h < 4) h = 28;
            track.Width = w;
            fill.Width = ThumbX() + ThumbD / 2;
            // Canvas 不认 VerticalAlignment，三者都要用 Canvas.Top 手动垂直居中，
            // 否则轨道贴在顶部、圆钮相对它往下掉
            Canvas.SetTop(track, (h - TrackH) / 2);
            Canvas.SetTop(fill, (h - TrackH) / 2);
            Canvas.SetLeft(thumb, 0);
            Canvas.SetTop(thumb, (h - ThumbD) / 2);
        }

        double Travel()
        {
            double w = ActualWidth - ThumbD;
            return w > 1 ? w : 1;
        }

        double ValueToX(double v)
        {
            double r = (v - Minimum) / (Maximum - Minimum);
            if (r < 0) r = 0; if (r > 1) r = 1;
            return r * Travel();
        }

        double XToValue(double x)
        {
            double r = x / Travel();
            if (r < 0) r = 0; if (r > 1) r = 1;
            double v = Minimum + r * (Maximum - Minimum);
            if (IsSnapToTickEnabled && TickFrequency > 0)
                v = Math.Round(v / TickFrequency) * TickFrequency;
            if (v < Minimum) v = Minimum;
            if (v > Maximum) v = Maximum;
            return v;
        }

        double ThumbX() { return posSpring.Value; }

        // 滑钮缩放合成：按下放大倍率 + 果冻形变写同一变换，两弹簧各存分量不打架
        double sizeK = 1.0, jellyKx = 0, jellyKy = 0;
        void ComposeThumbScale()
        {
            thumbScale.ScaleX = sizeK + jellyKx;
            thumbScale.ScaleY = sizeK + jellyKy;
        }

        // 滑钮位置弹簧：位移 + 果冻（水平速度拉伸）；活底透镜由逐帧对齐跟手
        void ApplyPos(double v, double velocity)
        {
            thumbTf.X = v;
            fill.Width = Math.Max(0, v + ThumbD / 2);
            double k = Math.Min(0.18, Math.Abs(velocity) * 0.00035);
            jellyKx = k; jellyKy = -k * 0.6;
            ComposeThumbScale();
        }

        void ApplySize(double v, double velocity)
        {
            sizeK = v;
            ComposeThumbScale();
        }

        void SnapThumb()
        {
            posSpring.SnapTo(ValueToX(val));
            thumbTf.X = posSpring.Value;
            fill.Width = posSpring.Value + ThumbD / 2;
        }

        public double Value
        {
            get { return val; }
            set
            {
                double v = value;
                if (IsSnapToTickEnabled && TickFrequency > 0)
                    v = Math.Round(v / TickFrequency) * TickFrequency;
                if (v < Minimum) v = Minimum;
                if (v > Maximum) v = Maximum;
                if (v == val) return;
                val = v;
                if (!dragging) SpringEngine.Go(posSpring, ValueToX(val), ApplyPos);
                var h = ValueChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        void OnDown(object sender, MouseButtonEventArgs e)
        {
            Focus();
            CaptureMouse();
            SpringEngine.Go(sizeSpring, 1.22, ApplySize);
            Point p = e.GetPosition(this);
            double x = p.X - ThumbD / 2;
            // 点在滑钮上=直接拖（跟手微调）；点在轨道上=跳值滑行
            bool onThumb = Math.Abs(x - thumbTf.X) < ThumbD;
            // 先赋值再立拖拽旗：Value setter 在 !dragging 时才挂弹簧，顺序反了点轨道就不滑行
            if (!onThumb) Value = XToValue(x);
            dragging = true;
            e.Handled = true;
        }

        void OnMove(object sender, MouseEventArgs e)
        {
            if (!dragging) return;
            double x = e.GetPosition(this).X - ThumbD / 2;
            // 端点阻力：越界最多 6px、系数 0.3，数值本身钳位
            double tx = x;
            if (tx < 0) tx = Math.Max(-6, tx * 0.3);
            double max = Travel();
            if (tx > max) tx = max + Math.Min(6, (tx - max) * 0.3);
            posSpring.SnapTo(tx);      // 拖动直达位置…
            thumbTf.X = tx;
            fill.Width = Math.Max(0, tx + ThumbD / 2);
            Value = XToValue(x);       // …数值走钳位（弹簧 Go 被 dragging 抑制）
            e.Handled = true;
        }

        void OnUp(object sender, MouseButtonEventArgs e)
        {
            if (!dragging) return;
            dragging = false;
            ReleaseMouseCapture();
            SetThumbGlass(false);      // 松手才变回不透明圆钮
            SpringEngine.Go(sizeSpring, 1.0, ApplySize);
            SpringEngine.Go(posSpring, ValueToX(val), ApplyPos);   // 越界吸回 / 落点回弹
            e.Handled = true;
        }

        // 按压滑钮=捏起一块玻璃：白雾罩渐隐成 35% 透明露出底下常驻活透镜 + 受光描边浮现；松开恢复白圆
        void SetThumbGlass(bool on)
        {
            if (!Glass.On || thumbGlass == on) return;
            thumbGlass = on;
            Ani.Start("gst" + GetHashCode(), Ani.AaColor(thumbBrush,
                on ? Color.FromArgb(92, 0xFF, 0xFF, 0xFF) : Colors.White, 120));
            if (thumbRings != null)
            {
                thumbRings.BorderBrush = on ? Glass.Edge() : null;
                thumbRings.BorderThickness = new Thickness(on ? 1 : 0);
            }
        }
    }

    // 自绘深色页签栏：头部按钮行 + 内容区，切换淡入
    class DarkTabControl : UserControl
    {
        class TabDef
        {
            public TextBlock HeadText;
            public Border Indicator;
            public UIElement Content;
        }

        List<TabDef> tabs = new List<TabDef>();
        StackPanel headRow;
        ContentControl body;
        int selected = -1;

        public DarkTabControl()
        {
            var root = new Grid();
            root.RowDefinitions.Add(UiHelpers.AutoRow());
            root.RowDefinitions.Add(UiHelpers.StarRow());
            headRow = new StackPanel();
            headRow.Orientation = Orientation.Horizontal;
            headRow.Margin = new Thickness(6, 0, 0, 0);
            body = new ContentControl();
            Grid.SetRow(body, 1);
            root.Children.Add(headRow);
            root.Children.Add(body);
            Content = root;
        }

        public void AddTab(string header, UIElement content)
        {
            var def = new TabDef();
            def.Content = content;
            def.HeadText = new TextBlock();
            def.HeadText.Text = header;
            def.HeadText.FontSize = 13;
            def.HeadText.Foreground = Palette.B(Palette.Dim);
            def.Indicator = new Border();
            def.Indicator.Height = 2;
            def.Indicator.Background = ThemeManager.Accent;
            def.Indicator.Margin = new Thickness(0, 4, 0, 0);
            def.Indicator.Visibility = Visibility.Collapsed;
            var stack = new StackPanel();
            stack.Children.Add(def.HeadText);
            stack.Children.Add(def.Indicator);
            var head = new Border();
            head.Padding = new Thickness(14, 8, 14, 6);
            head.Child = stack;
            head.Cursor = Cursors.Hand;
            int idx = tabs.Count;
            head.MouseLeftButtonUp += delegate { Select(idx); };
            head.MouseEnter += delegate { if (idx != selected) def.HeadText.Foreground = Palette.B(Palette.Text); };
            head.MouseLeave += delegate { if (idx != selected) def.HeadText.Foreground = Palette.B(Palette.Dim); };
            headRow.Children.Add(head);
            tabs.Add(def);
            if (tabs.Count == 1) Select(0);
        }

        public void Select(int idx)
        {
            if (idx < 0 || idx >= tabs.Count) return;
            selected = idx;
            for (int i = 0; i < tabs.Count; i++)
            {
                bool on = i == idx;
                tabs[i].HeadText.Foreground = on ? Palette.B(Palette.Text) : Palette.B(Palette.Dim);
                tabs[i].HeadText.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                tabs[i].Indicator.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            }
            body.Content = tabs[idx].Content;
            // 切换：淡入 + 下方 8px 滑入（动画关闭时引擎直达终态）
            body.Opacity = 0;
            Ani.Ts(body).Y = 8;
            Ani.Start("tabbody",
                Ani.AaOpacity(body, 1, 120),
                Ani.AaY(body, -8, 250, new AniEaseOutFluent(Ani.Middle)));
        }
    }

    // 深色下拉框（含可编辑模式，模板纯代码构建）
    // 液态玻璃：框体/弹出层画刷走 ComboPalette 绑定（模板密封会冻住直接塞进去的画刷，
    // 经绑定进活动元素的实例不冻，运行时换色即时生效）；项高亮保持静态色
    class DarkComboBox : ComboBox
    {
        Popup openPopup;
        Border openBorder;
        bool snapBusy;
        DispatcherTimer liveTimer;
        // 可变画刷持有者：IsFrozen 时整个换新实例 + PropertyChanged 让绑定重取
        class ComboPalette : System.ComponentModel.INotifyPropertyChanged
        {
            public static readonly ComboPalette Instance = new ComboPalette();
            public event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
            SolidColorBrush edge = Palette.B(Palette.Line);
            Brush popupBg = Palette.B(Palette.Panel);
            Brush popupEdge = Palette.B(Palette.Line);
            public Brush Edge { get { return edge; } }
            public Brush PopupBg { get { return popupBg; } }
            public Brush PopupEdge { get { return popupEdge; } }

            static SolidColorBrush Swap(SolidColorBrush b, Color c)
            {
                if (b.IsFrozen) return Palette.B(c);
                b.Color = c;
                return b;
            }

            public void Refresh()
            {
                if (Frost.On)
                {
                    edge = Palette.B(Color.FromArgb(70, 255, 255, 255));
                    popupBg = Frost.Popup;
                    popupEdge = Frost.Edge;
                }
                else if (Glass.On)
                {
                    // 玻璃配方分明暗：暗=白描边+深青弹层；亮=黑描边+近白弹层（弹层不透明压住底下内容）
                    edge = Swap(edge, Palette.GlassA(120));
                    popupBg = Palette.B(Palette.Light
                        ? Color.FromArgb(242, 0xFF, 0xFF, 0xFF)
                        : Color.FromArgb(225, 0x1A, 0x1D, 0x26));
                    popupEdge = Palette.B(Palette.GlassA(120));
                }
                else
                {
                    edge = Swap(edge, Palette.Line);
                    popupBg = Palette.B(Palette.Panel);
                    popupEdge = Palette.B(Palette.Line);
                }
                var h = PropertyChanged;
                if (h != null)
                {
                    h(this, new System.ComponentModel.PropertyChangedEventArgs("Edge"));
                    h(this, new System.ComponentModel.PropertyChangedEventArgs("PopupBg"));
                    h(this, new System.ComponentModel.PropertyChangedEventArgs("PopupEdge"));
                }
            }
        }

        static ControlTemplate sharedTemplate;
        static Style sharedItemStyle;
        static bool glassHooked;

        // 换肤清缓存：模板/项样式里的画刷（菜单文字色、高亮色、光标色）首次使用即冻结，
        // 不清掉则新色板永远进不来（ Palette.Apply 调用）
        public static void ResetShared()
        {
            sharedTemplate = null;
            sharedItemStyle = null;
        }

        public DarkComboBox()
        {
            if (sharedTemplate == null)
            {
                sharedTemplate = BuildTemplate();
                sharedItemStyle = BuildItemStyle();
            }
            if (!glassHooked)
            {
                glassHooked = true;
                ThemeManager.GlassChanged += delegate { ComboPalette.Instance.Refresh(); };
                ThemeManager.SkinChanged += delegate { ComboPalette.Instance.Refresh(); };
            }
            ComboPalette.Instance.Refresh();
            Template = sharedTemplate;
            ItemContainerStyle = sharedItemStyle;
            Background = FaceBrushForMode();
            Foreground = Palette.B(Palette.Text);
            FontSize = 12;
            MinHeight = 30;
            MaxDropDownHeight = 320;
            ThemeManager.GlassChanged += delegate { Background = FaceBrushForMode(); };
            DropDownOpened += delegate
            {
                try
                {
                    openPopup = Template.FindName("PART_Popup", this) as Popup;
                    openBorder = openPopup != null ? openPopup.Child as Border : null;
                    if (openPopup == null || openBorder == null) return;
                    ApplyPopupSnapshot(openPopup, openBorder);
                    if (liveTimer == null)
                    {
                        liveTimer = new DispatcherTimer();
                        liveTimer.Interval = TimeSpan.FromMilliseconds(120);
                        liveTimer.Tick += delegate
                        {
                            if (!IsDropDownOpen || openPopup == null || openBorder == null)
                            {
                                liveTimer.Stop();
                                return;
                            }
                            ApplyPopupSnapshot(openPopup, openBorder);
                        };
                    }
                    liveTimer.Start();
                }
                catch { }
            };
            DropDownClosed += delegate
            {
                if (liveTimer != null) liveTimer.Stop();
                openPopup = null;
                openBorder = null;
            };
        }

        // 下拉展开：直接渲染主窗内容根（不含独立 Popup）→ 裁弹层正下方 → 模糊。
        // 不用 PrintWindow：分层窗口可能返回黑图；RTB 渲内容根在 2D/3D 上都已被 SteveView 验证过。
        void ApplyPopupSnapshot(Popup popup, Border bd)
        {
            Window win = Window.GetWindow(this);
            if (win == null || bd == null) return;
            if (snapBusy) return;
            snapBusy = true;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                try
                {
                    FrameworkElement child = popup.Child as FrameworkElement;
                    FrameworkElement rootEl = win.Content as FrameworkElement;
                    Visual root = rootEl;
                    if (child == null || root == null) return;
                    int ww = (int)Math.Round(rootEl.RenderSize.Width);
                    int wh = (int)Math.Round(rootEl.RenderSize.Height);
                    if (ww < 8 || wh < 8) return;
                    // 半分辨率抓图：80px 模糊观感不变，RTB/Blur 成本降到 1/4，避免 UI 线程卡死
                    int rw = Math.Max(4, ww / 2), rh = Math.Max(4, wh / 2);
                    var rtb = new RenderTargetBitmap(rw, rh, 48, 48, PixelFormats.Pbgra32);
                    rtb.Render(root);
                    Point sp = child.PointToScreen(new Point(0, 0));
                    Point wp = rootEl.PointFromScreen(sp);   // 相对内容根，不把 Margin=10 算进去
                    int x = (int)Math.Round(wp.X), y = (int)Math.Round(wp.Y);
                    int w = (int)Math.Round(child.ActualWidth), h = (int)Math.Round(child.ActualHeight);
                    int x0 = Math.Max(0, x / 2), y0 = Math.Max(0, y / 2);
                    int x1 = Math.Min(rw, (x + w) / 2), y1 = Math.Min(rh, (y + h) / 2);
                    if (x1 - x0 < 2 || y1 - y0 < 2) return;
                    // 弹层出窗时裁片会变短，Stretch 拉伸会变形；这种情况直接回退壁纸底，不硬拉
                    int reqW = Math.Max(1, (x + w) / 2 - x / 2);
                    int reqH = Math.Max(1, (y + h) / 2 - y / 2);
                    if (x1 - x0 < reqW - 2 || y1 - y0 < reqH - 2)
                    {
                        bd.SetCurrentValue(Border.BackgroundProperty, Frost.On ? Frost.Popup : Palette.B(Palette.Panel));
                        return;
                    }
                    // 外扩 48px 再模糊：否则 BlurEffect 在裁切边缘采到透明，弹层四周会塌成透明/圆形光斑
                    int pad = 96;   // 半分辨率下 ≥ 2×模糊半径，压住高斯拖尾
                    int ex0 = Math.Max(0, x0 - pad), ey0 = Math.Max(0, y0 - pad);
                    int ex1 = Math.Min(rw, x1 + pad), ey1 = Math.Min(rh, y1 + pad);
                    var crop = new CroppedBitmap(rtb, new Int32Rect(ex0, ey0, ex1 - ex0, ey1 - ey0));
                    var img = new Image();
                    img.Source = crop;
                    img.Stretch = Stretch.Fill;
                    img.Width = crop.PixelWidth;
                    img.Height = crop.PixelHeight;
                    img.Effect = new BlurEffect { Radius = 40, KernelType = KernelType.Gaussian };
                    var host = new Grid();
                    host.Width = crop.PixelWidth;
                    host.Height = crop.PixelHeight;
                    host.ClipToBounds = true;
                    host.Children.Add(img);
                    host.Measure(new Size(crop.PixelWidth, crop.PixelHeight));
                    host.Arrange(new Rect(0, 0, crop.PixelWidth, crop.PixelHeight));
                    host.UpdateLayout();
                    var blurred = new RenderTargetBitmap(crop.PixelWidth, crop.PixelHeight, 96, 96, PixelFormats.Pbgra32);
                    blurred.Render(host);
                    var center = new CroppedBitmap(blurred, new Int32Rect(x0 - ex0, y0 - ey0, x1 - x0, y1 - y0));
                    var brush = new ImageBrush(center);
                    brush.Stretch = Stretch.Fill;
                    bd.SetCurrentValue(Border.BackgroundProperty, brush);
                    bd.SetCurrentValue(Border.BorderBrushProperty, Frost.On ? Frost.Edge : Glass.Edge());
                }
                catch
                {
                    bd.SetCurrentValue(Border.BackgroundProperty, Frost.On ? Frost.Popup : Palette.B(Palette.Panel));
                }
                finally { snapBusy = false; }
            }), DispatcherPriority.Loaded);
        }

        static Brush FaceBrushForMode()
        {
            if (Frost.On) return Frost.Input;
            if (!Glass.On) return Palette.B(Palette.Input);
            // 玻璃框体：暗=深青罩；亮=浅黑雾（白罩叠白底会隐形，iOS 亮色系用压暗罩）
            return Palette.Light
                ? Palette.B(Palette.GlassA(30))
                : Palette.B(Color.FromArgb(150, 0x1E, 0x22, 0x2E));
        }

        static ControlTemplate BuildTemplate()
        {
            var t = new ControlTemplate(typeof(ComboBox));
            var root = new FrameworkElementFactory(typeof(Grid));

            // 底层开关按钮（点击展开下拉）
            var toggle = new FrameworkElementFactory(typeof(ToggleButton));
            toggle.SetValue(UIElement.FocusableProperty, false);
            toggle.SetValue(ToggleButton.ClickModeProperty, ClickMode.Press);
            // 关键：把下拉框自身的 Background 传给内部 ToggleButton，
            // 否则模板内 Border 绑到的是 ToggleButton 默认（白色）背景
            var bgOuter = new Binding();
            bgOuter.Path = new PropertyPath("Background");
            bgOuter.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            toggle.SetBinding(Control.BackgroundProperty, bgOuter);
            var openBind = new Binding();
            openBind.Path = new PropertyPath("IsDropDownOpen");
            openBind.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            openBind.Mode = BindingMode.TwoWay;
            toggle.SetBinding(ToggleButton.IsCheckedProperty, openBind);
            var tt = new ControlTemplate(typeof(ToggleButton));
            var tbd = new FrameworkElementFactory(typeof(Border));
            var bgBind = new Binding();
            bgBind.Path = new PropertyPath("Background");
            bgBind.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            tbd.SetBinding(Border.BackgroundProperty, bgBind);
            tbd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            var edgeBind = new Binding("Edge");
            edgeBind.Source = ComboPalette.Instance;
            tbd.SetBinding(Border.BorderBrushProperty, edgeBind);
            tbd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            tt.VisualTree = tbd;
            toggle.SetValue(Control.TemplateProperty, tt);
            root.AppendChild(toggle);

            // 选中项展示
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.Name = "SelBox";
            var selBind = new Binding();
            selBind.Path = new PropertyPath("SelectionBoxItem");
            selBind.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            cp.SetBinding(ContentPresenter.ContentProperty, selBind);
            cp.SetValue(FrameworkElement.MarginProperty, new Thickness(9, 0, 32, 0));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            cp.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(cp);

            // 可编辑输入框（IsEditable 时显示）
            var et = new FrameworkElementFactory(typeof(TextBox));
            et.Name = "PART_EditableTextBox";
            et.SetValue(Control.BackgroundProperty, new SolidColorBrush(Colors.Transparent));
            et.SetValue(Control.BorderThicknessProperty, new Thickness(0));
            et.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 0, 32, 0));
            et.SetValue(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center);
            et.SetValue(TextBox.CaretBrushProperty, Palette.B(Palette.Text));
            et.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);
            var fgBind = new Binding();
            fgBind.Path = new PropertyPath("Foreground");
            fgBind.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            et.SetBinding(Control.ForegroundProperty, fgBind);
            root.AppendChild(et);

            // 下拉箭头
            var arrow = new FrameworkElementFactory(typeof(TextBlock));
            arrow.SetValue(TextBlock.TextProperty, "▾");
            arrow.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
            arrow.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            arrow.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
            arrow.SetValue(UIElement.IsHitTestVisibleProperty, false);
            var fgBind2 = new Binding();
            fgBind2.Path = new PropertyPath("Foreground");
            fgBind2.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            arrow.SetBinding(TextElement.ForegroundProperty, fgBind2);
            root.AppendChild(arrow);

            // 弹出列表
            var popup = new FrameworkElementFactory(typeof(Popup));
            popup.Name = "PART_Popup";
            popup.SetValue(Popup.AllowsTransparencyProperty, true);
            var popBind = new Binding();
            popBind.Path = new PropertyPath("IsDropDownOpen");
            popBind.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popup.SetBinding(Popup.IsOpenProperty, popBind);
            popup.SetValue(Popup.PlacementProperty, PlacementMode.Bottom);
            var popupWidth = new Binding("ActualWidth");
            popupWidth.RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent);
            popup.SetBinding(FrameworkElement.WidthProperty, popupWidth);
            var pbd = new FrameworkElementFactory(typeof(Border));
            var popBgBind = new Binding("PopupBg");
            popBgBind.Source = ComboPalette.Instance;
            pbd.SetBinding(Border.BackgroundProperty, popBgBind);
            var popEdgeBind = new Binding("PopupEdge");
            popEdgeBind.Source = ComboPalette.Instance;
            pbd.SetBinding(Border.BorderBrushProperty, popEdgeBind);
            pbd.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            pbd.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
            pbd.SetValue(Border.PaddingProperty, new Thickness(3));
            var sv = new FrameworkElementFactory(typeof(ScrollViewer));
            sv.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
            sv.SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            sv.SetValue(FrameworkElement.MaxHeightProperty, 320.0);
            var ip = new FrameworkElementFactory(typeof(ItemsPresenter));
            sv.AppendChild(ip);
            pbd.AppendChild(sv);
            popup.AppendChild(pbd);
            root.AppendChild(popup);

            t.VisualTree = root;

            // IsEditable 时切换输入框/选中项显示
            var trigEdit = new Trigger();
            trigEdit.Property = ComboBox.IsEditableProperty;
            trigEdit.Value = true;
            trigEdit.Setters.Add(new Setter { TargetName = "PART_EditableTextBox", Property = UIElement.VisibilityProperty, Value = Visibility.Visible });
            trigEdit.Setters.Add(new Setter { TargetName = "SelBox", Property = UIElement.VisibilityProperty, Value = Visibility.Collapsed });
            t.Triggers.Add(trigEdit);

            // 禁用半透明
            var trigDis = new Trigger();
            trigDis.Property = UIElement.IsEnabledProperty;
            trigDis.Value = false;
            trigDis.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = 0.45 });
            t.Triggers.Add(trigDis);

            return t;
        }

        static Style BuildItemStyle()
        {
            var st = new Style(typeof(ComboBoxItem));
            st.Setters.Add(new Setter(Control.ForegroundProperty, Palette.B(Palette.Text)));
            st.Setters.Add(new Setter(Control.FontSizeProperty, 12.0));
            var ct = new ControlTemplate(typeof(ComboBoxItem));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Colors.Transparent));
            bd.SetValue(Border.PaddingProperty, new Thickness(9, 6, 9, 6));
            bd.SetValue(Border.CornerRadiusProperty, new CornerRadius(3));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            bd.AppendChild(cp);
            ct.VisualTree = bd;
            var tr = new Trigger();
            tr.Property = ComboBoxItem.IsHighlightedProperty;
            tr.Value = true;
            tr.Setters.Add(new Setter { TargetName = "Bd", Property = Border.BackgroundProperty, Value = Palette.B(Palette.Line) });
            ct.Triggers.Add(tr);
            st.Setters.Add(new Setter(Control.TemplateProperty, ct));
            return st;
        }
    }

    // 深色列表框（项为代码构建的行元素）
    class DarkListBox : ListBox
    {
        static Style itemStyle;

        // 换肤清缓存（同 DarkComboBox.ResetShared；高亮画刷冻结在样式里）
        public static void ResetShared() { itemStyle = null; }

        public DarkListBox()
        {
            if (itemStyle == null) itemStyle = BuildItemStyle();
            Background = new SolidColorBrush(Colors.Transparent);
            BorderThickness = new Thickness(0);
            ItemContainerStyle = itemStyle;
            SetValue(ScrollViewer.HorizontalScrollBarVisibilityProperty, ScrollBarVisibility.Disabled);
            SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty, ScrollBarVisibility.Auto);
        }

        static Style BuildItemStyle()
        {
            var st = new Style(typeof(ListBoxItem));
            st.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            st.Setters.Add(new Setter(Control.MarginProperty, new Thickness(0)));
            st.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            st.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, null));
            var ct = new ControlTemplate(typeof(ListBoxItem));
            var bd = new FrameworkElementFactory(typeof(Border));
            bd.Name = "Bd";
            bd.SetValue(Border.BackgroundProperty, new SolidColorBrush(Colors.Transparent));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            bd.AppendChild(cp);
            ct.VisualTree = bd;
            var tr = new Trigger();
            tr.Property = ListBoxItem.IsMouseOverProperty;
            tr.Value = true;
            tr.Setters.Add(new Setter { TargetName = "Bd", Property = Border.BackgroundProperty, Value = Palette.B(Palette.Btn) });
            ct.Triggers.Add(tr);
            var tr2 = new Trigger();
            tr2.Property = ListBoxItem.IsSelectedProperty;
            tr2.Value = true;
            tr2.Setters.Add(new Setter { TargetName = "Bd", Property = Border.BackgroundProperty, Value = Palette.B(Palette.BtnHover) });
            ct.Triggers.Add(tr2);
            st.Setters.Add(new Setter(Control.TemplateProperty, ct));
            return st;
        }
    }

    // 全局深色滚动条：UiEntry 启动时注册到 Application.Resources，对所有窗口/模板内滚动条生效。
    // 模板用 XamlReader 解析（Track 的 Thumb/RepeatButton 是普通 CLR 属性，纯代码模板设不了）。
    // glass=true 用 iOS 式 5px 半透明胶囊滑块（启动时按 LiquidGlass 选定，运行中切模式下次启动生效）
    static class DarkScrollBar
    {
        public static void Register(System.Windows.Application app) { Register(app, false); }

        public static void Register(System.Windows.Application app, bool glass)
        {
            app.Resources.Remove(typeof(ScrollBar));   // 换肤会重注册：Add 重复 key 会直接炸，先清
            string xaml =
                "<Style xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" " +
                "xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" TargetType=\"{x:Type ScrollBar}\">" +
                "<Setter Property=\"SnapsToDevicePixels\" Value=\"True\"/>" +
                "<Setter Property=\"Template\"><Setter.Value>" + TemplateXaml(true, glass) + "</Setter.Value></Setter>" +
                "<Style.Triggers>" +
                "<Trigger Property=\"Orientation\" Value=\"Horizontal\">" +
                "<Setter Property=\"Template\"><Setter.Value>" + TemplateXaml(false, glass) + "</Setter.Value></Setter>" +
                "</Trigger>" +
                "</Style.Triggers></Style>";
            var style = (Style)System.Windows.Markup.XamlReader.Parse(xaml);
            app.Resources.Add(typeof(ScrollBar), style);
        }

        static string TemplateXaml(bool vertical, bool glass)
        {
            string size = vertical
                ? (glass ? "Width=\"5\"" : "Width=\"8\"")
                : (glass ? "Height=\"5\"" : "Height=\"8\"");
            string margin = glass ? "Margin=\"1\"" : "Margin=\"2\"";
            string thumbBg, thumbHover;
            if (glass)
            {
                thumbBg = Palette.Light ? "#4D000000" : "#59FFFFFF";   // 玻璃滑块随明暗反色
                thumbHover = Palette.Light ? "#73000000" : "#88FFFFFF";
            }
            else
            {
                thumbBg = Palette.Light ? "#A0A6B0" : "#3A3F4E";
                thumbHover = Palette.Light ? "#80868F" : "#353A49";
            }
            string decCmd = vertical ? "ScrollBar.PageUpCommand" : "ScrollBar.PageLeftCommand";
            string incCmd = vertical ? "ScrollBar.PageDownCommand" : "ScrollBar.PageRightCommand";
            // 关键：ScrollBar 模板里的 Track 方向语义与直觉相反——纵向必须 IsDirectionReversed=true，
            // 否则滑块移动方向与滚动方向颠倒（实测验证，与 WPF 默认模板一致）
            string reversed = vertical ? "true" : "false";
            return
                "<ControlTemplate TargetType=\"{x:Type ScrollBar}\">" +
                "<Grid " + size + " " + margin + " SnapsToDevicePixels=\"True\">" +
                "<Track x:Name=\"PART_Track\" IsDirectionReversed=\"" + reversed + "\" Orientation=\"{Binding Orientation, RelativeSource={RelativeSource TemplatedParent}}\">" +
                "<Track.DecreaseRepeatButton>" + RepeatXaml(decCmd) + "</Track.DecreaseRepeatButton>" +
                "<Track.Thumb><Thumb><Thumb.Template>" +
                "<ControlTemplate TargetType=\"{x:Type Thumb}\">" +
                "<Border x:Name=\"Bd\" CornerRadius=\"4\" Background=\"" + thumbBg + "\"/>" +
                "<ControlTemplate.Triggers>" +
                "<Trigger Property=\"IsMouseOver\" Value=\"True\">" +
                "<Setter TargetName=\"Bd\" Property=\"Background\" Value=\"" + thumbHover + "\"/>" +
                "</Trigger>" +
                "</ControlTemplate.Triggers>" +
                "</ControlTemplate>" +
                "</Thumb.Template></Thumb></Track.Thumb>" +
                "<Track.IncreaseRepeatButton>" + RepeatXaml(incCmd) + "</Track.IncreaseRepeatButton>" +
                "</Track></Grid></ControlTemplate>";
        }

        // 翻页区：全透明（只接收命令，不显示系统箭头）
        static string RepeatXaml(string cmd)
        {
            return
                "<RepeatButton Command=\"" + cmd + "\" Focusable=\"False\">" +
                "<RepeatButton.Template>" +
                "<ControlTemplate TargetType=\"{x:Type RepeatButton}\">" +
                "<Border Background=\"Transparent\"/>" +
                "</ControlTemplate>" +
                "</RepeatButton.Template></RepeatButton>";
        }
    }

    // 带占位提示的深色无边框输入框
    class HintTextBox : UserControl
    {
        public readonly TextBox Box;
        TextBlock hint;

        public HintTextBox(string hintText)
        {
            var g = new Grid();
            Box = new TextBox();
            Box.Foreground = Palette.B(Palette.Text);
            Box.BorderThickness = new Thickness(0);
            Box.Padding = new Thickness(9, 6, 9, 6);
            Box.FontSize = 12;
            Box.CaretBrush = Palette.B(Palette.Text);
            Box.VerticalContentAlignment = VerticalAlignment.Center;
            if (Frost.On)
            {
                Box.Background = Brushes.Transparent;
                var bd = new Border();
                bd.Background = Frost.Input;
                bd.BorderBrush = Frost.Edge;
                bd.BorderThickness = new Thickness(1);
                bd.CornerRadius = new CornerRadius(4);
                bd.Child = Box;
                g.Children.Add(bd);
            }
            else
            {
                Box.Background = Palette.B(Palette.Input);
                g.Children.Add(Box);
            }
            hint = new TextBlock();
            hint.Text = hintText;
            hint.Foreground = Palette.B(Palette.Dim);
            hint.FontSize = 12;
            hint.IsHitTestVisible = false;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(10, 0, 0, 0);
            g.Children.Add(hint);
            Box.TextChanged += delegate { hint.Visibility = Box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            Content = g;
        }

        public string Text { get { return Box.Text; } set { Box.Text = value; } }
    }

    // 二级侧边标签项：PCL 左边栏风格（选中竖条两段式生长 + 悬停底色渐变）
    // 液态玻璃开：竖条隐藏，选中态换成玻璃 pill（模拟玻璃底+边缘受光，弹簧放大进场）
    class SideTabItem : UserControl
    {
        public event EventHandler Click;

        SolidColorBrush backBrush;
        SolidColorBrush labelBrush;
        Border indicator;
        Border back;
        TextBlock label;
        bool isSelected;
        string aniKey;
        Spring selectSpring;

        public SideTabItem(string text)
        {
            aniKey = "st" + GetHashCode();
            selectSpring = new Spring(1) { Stiffness = 240, Damping = 13 };
            Height = 34;
            var g = new Grid();
            backBrush = Palette.B(Colors.Transparent);
            back = new Border();
            back.Background = backBrush;
            back.CornerRadius = new CornerRadius(6);
            g.Children.Add(back);
            indicator = new Border();
            indicator.Width = 3;
            indicator.Height = 0;
            indicator.CornerRadius = new CornerRadius(1.5);
            indicator.Background = ThemeManager.Accent;
            indicator.HorizontalAlignment = HorizontalAlignment.Left;
            indicator.VerticalAlignment = VerticalAlignment.Center;
            indicator.Margin = new Thickness(4, 0, 0, 0);
            indicator.IsHitTestVisible = false;
            g.Children.Add(indicator);
            labelBrush = Palette.B(Palette.Dim);
            label = new TextBlock();
            label.Text = text;
            label.Foreground = labelBrush;
            label.FontSize = 12.5;
            label.VerticalAlignment = VerticalAlignment.Center;
            label.Margin = new Thickness(15, 0, 6, 0);
            label.IsHitTestVisible = false;
            g.Children.Add(label);
            Content = g;
            Cursor = Cursors.Hand;
            MouseEnter += delegate { if (!isSelected) Ani.Start(aniKey + "h", Ani.AaColor(backBrush, HoverColor(), 100)); };
            MouseLeave += delegate { if (!isSelected) Ani.Start(aniKey + "h", Ani.AaColor(backBrush, Colors.Transparent, 180)); };
            MouseLeftButtonUp += delegate { var h = Click; if (h != null) h(this, EventArgs.Empty); };
            ThemeManager.GlassChanged += delegate { ApplyModeFace(); };
            ApplyModeFace();
        }

        Color HoverColor()
        {
            return (Glass.On || Frost.On) ? Palette.GlassA(22) : Palette.Btn;
        }

        Color SelectedColor()
        {
            return (Glass.On || Frost.On) ? Palette.GlassA(46) : Palette.Btn;
        }

        // 模式脸：玻璃=圆角加大（选中态由侧栏共享滑动 pill 表达，本项不画底）；普通=原样
        void ApplyModeFace()
        {
            if (Glass.On || Frost.On)
            {
                back.CornerRadius = new CornerRadius(9);
                back.BorderBrush = null;
                back.BorderThickness = new Thickness(0);
                indicator.Visibility = Visibility.Collapsed;
            }
            else
            {
                back.CornerRadius = new CornerRadius(6);
                back.BorderThickness = new Thickness(0);
                indicator.Visibility = Visibility.Visible;
            }
            // 颜色重刷（玻璃下选中项无底：共享 pill 在后面衬着）
            Ani.Start(aniKey + "h", Ani.AaColor(backBrush,
                isSelected && !(Glass.On || Frost.On) ? SelectedColor() : Colors.Transparent, 120));
        }

        public bool Selected { get { return isSelected; } }

        // 选中/取消：底色与文字变色，竖条绝对插值（OutBack 过冲生长 / 消隐归零，末帧必达）
        // 玻璃模式：竖条不动，pill 弹簧 0.96→1 放大进场（粘滞回弹）
        public void SetSelected(bool v)
        {
            if (isSelected == v) return;
            isSelected = v;
            label.FontWeight = v ? FontWeights.SemiBold : FontWeights.Normal;
            Ani.Start(aniKey + "h", Ani.AaColor(backBrush, v && !(Glass.On || Frost.On) ? SelectedColor() : Colors.Transparent, 120));
            Ani.Start(aniKey + "t", Ani.AaColor(labelBrush, v ? Palette.Text : Palette.Dim, 150));
            if (Glass.On || Frost.On) return;   // 玻璃/霜面：选中态由共享滑动 pill 表达
            double from = indicator.Height;
            if (v)
                Ani.Start(aniKey + "i", Ani.AaProgress(delegate(double e) { indicator.Height = from + (16 - from) * e; },
                    300, new AniEaseOutBack(Ani.Weak)));
            else
                Ani.Start(aniKey + "i", Ani.AaProgress(delegate(double e) { indicator.Height = from * (1 - e); },
                    120, new AniEaseInFluent(Ani.Weak)));
        }
    }
    // 游玩统计小部件：近 7 天条形图 + 右下角「清空本周」
    static class PlayStatsWidget
    {
        public static UIElement Build()
        {
            var root = new Grid();
            root.RowDefinitions.Add(UiHelpers.AutoRow());
            root.RowDefinitions.Add(UiHelpers.StarRow());
            root.RowDefinitions.Add(UiHelpers.AutoRow());

            var line = new TextBlock();
            line.FontSize = 12;
            line.Foreground = Palette.B(Palette.Text);
            root.Children.Add(line);

            var chart = new Grid();
            chart.VerticalAlignment = VerticalAlignment.Stretch;
            chart.Margin = new Thickness(0, 4, 0, 3);
            Grid.SetRow(chart, 1);
            var bars = new Border[7];
            var labels = new TextBlock[7];
            for (int i = 0; i < 7; i++)
            {
                chart.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var wrap = new Grid();
                var bar = new Border();
                bar.CornerRadius = new CornerRadius(3);
                bar.VerticalAlignment = VerticalAlignment.Bottom;
                bar.Margin = new Thickness(2, 0, 2, 0);
                bar.Background = Palette.B(ThemeManager.CurrentColor);
                bar.Opacity = 0.30 + 0.70 * (i / 6.0);   // 越靠近今天越亮
                bar.Height = 2;
                wrap.Children.Add(bar);
                Grid.SetColumn(wrap, i);
                chart.Children.Add(wrap);
                bars[i] = bar;
            }
            root.Children.Add(chart);

            var foot = new Grid();
            foot.ColumnDefinitions.Add(UiHelpers.StarCol());
            foot.ColumnDefinitions.Add(UiHelpers.AutoCol());
            var days = new TextBlock();
            days.FontSize = 9.5;
            days.VerticalAlignment = VerticalAlignment.Center;
            days.Foreground = Palette.B(Palette.Dim);
            foot.Children.Add(days);
            var del = new FlatButton("清空本周");
            del.SetFontSize(10);
            del.HoverScale = 1.01;
            del.SetPadding(new Thickness(9, 2, 9, 2));
            del.SetCornerRadius(new CornerRadius(5));
            del.SetColors(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(26, 255, 255, 255), Palette.Dim, Palette.Text);
            Grid.SetColumn(del, 1);
            foot.Children.Add(del);
            Grid.SetRow(foot, 2);
            root.Children.Add(foot);

            Action render = delegate
            {
                int today = 0, week = 0;
                var d = new int[7];
                try { today = Stats.Today(); week = Stats.ThisWeek(); d = Stats.Last7(); } catch { }
                int max = 1;
                for (int i = 0; i < 7; i++) if (d[i] > max) max = d[i];
                line.Text = "今天 " + today + " 次 · 本周 " + week + " 次";
                double hh = chart.ActualHeight > 8 ? chart.ActualHeight - 2 : 30;
                for (int i = 0; i < 7; i++)
                    bars[i].Height = Math.Max(2, hh * d[i] / max);
                DateTime t = DateTime.Now;
                string[] wd = { "日", "一", "二", "三", "四", "五", "六" };
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 7; i++)
                {
                    if (i > 0) sb.Append("  ");
                    sb.Append(wd[(int)t.AddDays(i - 6).DayOfWeek]);
                }
                days.Text = sb.ToString();
            };
            del.Click += delegate
            {
                try { Stats.ClearWeek(); } catch { }
                render();
            };
            render();
            chart.SizeChanged += delegate { render(); };   // 小部件被拉伸时条形图跟着长高
            return root;
        }
    }
}
