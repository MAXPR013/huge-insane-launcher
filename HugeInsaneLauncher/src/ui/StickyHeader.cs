// HugeLauncher.Ui - 卡片标题吸顶 + 实时磨砂：标题层从内容流里独立出来，
// 吸顶时用 VisualBrush(正文) + BlurEffect 做实时模糊，卡片在标题处以圆角裁切。
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace HugeLauncher.Ui
{
    class StickyCard
    {
        public Border Card;
        public Border Header;
        public FrameworkElement Body;
        public TranslateTransform Tf;
        public Rectangle BlurLayer;
        public VisualBrush BlurBrush;
        public Rectangle MaskLayer;
        public LinearGradientBrush FadeMask;
        public Brush TintBrush;
        public RectangleGeometry CardClip;
        public RectangleGeometry BodyClip;
        public double LastTf = -1;
    }

    static class StickyHeaders
    {
        static Color CardBaseColor()
        {
            Color tc = Palette.Tile;
            if (tc.A >= 255) return tc;
            // 霜面/流光/史莱姆的 Tile 是半透明覆盖色：按当前底色合成成不透明色，
            // 否则暗色模式把 alpha 拉到 255 会得到纯白
            Color bg = Palette.Panel;
            double a = tc.A / 255.0;
            return Color.FromRgb(
                (byte)Math.Round(tc.R * a + bg.R * (1 - a)),
                (byte)Math.Round(tc.G * a + bg.G * (1 - a)),
                (byte)Math.Round(tc.B * a + bg.B * (1 - a)));
        }

        public static StickyCard Attach(ScrollViewer sv, Border card, Border header, FrameworkElement body)
        {
            var s = new StickyCard();
            s.Card = card;
            s.Header = header;
            s.Body = body;
            s.Tf = new TranslateTransform(0, 0);
            s.CardClip = new RectangleGeometry();
            s.CardClip.RadiusX = card.CornerRadius.TopLeft;
            s.CardClip.RadiusY = card.CornerRadius.TopLeft;
            s.BodyClip = new RectangleGeometry();

            // Header 原来靠 Padding 留白；现在模糊层要铺满整宽，把 Padding 挪给标题内容
            Thickness pad = header.Padding;
            UIElement oldChild = header.Child;
            header.Padding = new Thickness(0);
            header.Child = null;
            var hg = new Grid();
            s.BlurLayer = new Rectangle();
            s.BlurBrush = new VisualBrush(body);
            s.BlurBrush.ViewboxUnits = BrushMappingMode.Absolute;
            s.BlurBrush.Stretch = Stretch.Fill;
            s.BlurLayer.Fill = s.BlurBrush;
            s.BlurLayer.Effect = new BlurEffect { Radius = 20, KernelType = KernelType.Gaussian };
            s.BlurLayer.Opacity = 0.92;
            s.BlurLayer.IsHitTestVisible = false;
            s.BlurLayer.Visibility = Visibility.Collapsed;
            hg.Children.Add(s.BlurLayer);

            // 标题遮罩：顶部不透明、底部透明；正文从下面淡出，不是硬切
            s.MaskLayer = new Rectangle();
            // 不做“猜一个卡片色”：直接复用卡片自己的 Background 画刷，
            // 再用透明度渐变实现“上不透、下透明”，三皮肤/亮暗都不会偏成黑或白。
            s.MaskLayer.Fill = card.Background != null ? card.Background : Palette.B(Palette.Tile);
            var maskFade = new LinearGradientBrush();
            maskFade.StartPoint = new Point(0, 0);
            maskFade.EndPoint = new Point(0, 1);
            maskFade.GradientStops.Add(new GradientStop(Colors.White, 0));
            maskFade.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            s.MaskLayer.OpacityMask = maskFade;
            s.MaskLayer.IsHitTestVisible = false;
            s.MaskLayer.Visibility = Visibility.Collapsed;
            hg.Children.Add(s.MaskLayer);

            // 正文自身做渐隐：不画任何假色遮罩，卡片真实底色自然露出来
            s.FadeMask = new LinearGradientBrush();
            s.FadeMask.MappingMode = BrushMappingMode.RelativeToBoundingBox;
            s.FadeMask.StartPoint = new Point(0, 0);
            s.FadeMask.EndPoint = new Point(0, 1);
            s.FadeMask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            s.FadeMask.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            s.FadeMask.GradientStops.Add(new GradientStop(Colors.White, 0));
            s.FadeMask.GradientStops.Add(new GradientStop(Colors.White, 1));
            if (oldChild != null)
            {
                var fe = oldChild as FrameworkElement;
                if (fe != null) fe.Margin = pad;
                hg.Children.Add(oldChild);
            }
            header.Child = hg;
            header.Background = null;
            header.RenderTransform = s.Tf;
            Panel.SetZIndex(header, 10);
            header.CornerRadius = new CornerRadius(card.CornerRadius.TopLeft, card.CornerRadius.TopRight, 0, 0);
            s.TintBrush = new SolidColorBrush(Palette.Light
                ? Color.FromArgb(55, 255, 255, 255)
                : Color.FromArgb(55, 0, 0, 0));
            header.Clip = null;
            header.SizeChanged += delegate
            {
                double w = header.ActualWidth, h = header.ActualHeight;
                if (w > 0 && h > 0)
                    header.Clip = new RectangleGeometry(new Rect(0, 0, w, h),
                        header.CornerRadius.TopLeft, header.CornerRadius.TopLeft);
            };
            card.UseLayoutRounding = true;
            header.UseLayoutRounding = true;
            body.UseLayoutRounding = true;
            card.SizeChanged += delegate { UpdateOne(sv, s); };
            body.SizeChanged += delegate { UpdateOne(sv, s); };
            return s;
        }

        public static void Update(ScrollViewer sv, IList<StickyCard> cards)
        {
            if (sv == null || cards == null) return;
            for (int i = 0; i < cards.Count; i++) UpdateOne(sv, cards[i]);
        }

        static void Reset(StickyCard s)
        {
            if (s == null) return;
            if (s.Tf != null) s.Tf.Y = 0;
            if (s.BlurLayer != null) s.BlurLayer.Visibility = Visibility.Collapsed;
            if (s.Body != null) { s.Body.Clip = null; s.Body.OpacityMask = null; }
            if (s.Header != null) s.Header.Background = null;
            if (s.Card != null) s.Card.Clip = null;
            s.LastTf = -1;
        }

        static void UpdateOne(ScrollViewer sv, StickyCard s)
        {
            if (sv == null || s == null || s.Card == null || s.Header == null || s.Body == null) return;
            try
            {
                if (!s.Card.IsArrangeValid || s.Header.ActualHeight <= 0 || s.Body.ActualHeight <= 0)
                {
                    Reset(s);
                    return;
                }
                double cardH = s.Card.ActualHeight;
                double headerH = s.Header.ActualHeight;
                double bottomR = Math.Max(s.Card.CornerRadius.BottomLeft, s.Card.CornerRadius.BottomRight);
                if (cardH < headerH + bottomR + 8)
                {
                    Reset(s);
                    return;
                }

                Point p;
                UIElement content = sv.Content as UIElement;
                FrameworkElement contentEl = sv.Content as FrameworkElement;
                if (content != null)
                    p = s.Card.TranslatePoint(new Point(0, 0), content);
                else
                    p = s.Card.TranslatePoint(new Point(0, 0), sv);
                double contentMarginTop = contentEl != null ? contentEl.Margin.Top : 0;
                double vy = contentMarginTop + p.Y - sv.VerticalOffset;
                double by = vy + cardH;
                double offH = s.Card.BorderThickness.Top;
                double top = Math.Max(vy + offH, Math.Min(0, by - headerH - bottomR - 1));
                double tf = top - vy - offH;
                if (tf < 0) tf = 0;
                double bodyH = s.Body.ActualHeight;
                if (tf > bodyH) tf = bodyH;

                if (Math.Abs(tf - s.LastTf) >= 0.01)
                {
                    s.LastTf = tf;
                    s.Tf.Y = tf;
                }

                if (tf <= 0.01)
                {
                    s.BlurLayer.Visibility = Visibility.Collapsed;
                    s.MaskLayer.Visibility = Visibility.Collapsed;
                    s.Card.Clip = null;
                    s.Header.Background = null;
                    s.Body.Clip = null;
                    s.Body.OpacityMask = null;
                }
                else
                {
                    double cardW = s.Card.ActualWidth;
                    double clipTop = offH + tf;
                    s.CardClip.Rect = new Rect(0, clipTop, cardW, Math.Max(0, cardH - clipTop));
                    s.Card.Clip = s.CardClip;   // 卡片背景/正文一起随标题圆角裁切
                    s.Header.Background = null;
                    s.BlurLayer.Visibility = Visibility.Collapsed;
                    s.MaskLayer.Visibility = Visibility.Collapsed;

                    // 标题下沿以下 16px 淡入：tf 处透明，tf+16 恢复实心
                    double bodyW = s.Body.ActualWidth;
                    double fadeStart = tf;
                    double fadeEnd = Math.Min(bodyH, tf + 16);
                    s.BodyClip.Rect = new Rect(0, tf, bodyW, Math.Max(0, bodyH - tf));
                    if (s.Body.Clip == null) s.Body.Clip = s.BodyClip;
                    double b = Math.Max(1, bodyH);
                    double f0 = Math.Max(0, Math.Min(1, fadeStart / b));
                    double f1 = Math.Max(0, Math.Min(1, fadeEnd / b));
                    s.FadeMask.GradientStops[1].Offset = f0;
                    s.FadeMask.GradientStops[2].Offset = f1;
                    s.FadeMask.GradientStops[3].Offset = Math.Min(1, f1 + 0.002);
                    s.Body.OpacityMask = s.FadeMask;
                }
            }
            catch
            {
                // 卡片已脱树（重建/切页）时丢弃这一帧
            }
        }
    }
}
