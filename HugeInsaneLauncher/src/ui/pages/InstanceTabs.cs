// HugeLauncher.Ui.Pages - 实例详情页族的三级标签条（概览/Mod/存档/设置）
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    // 详情页族顶部横排三级标签。current 传 "overall"/"mod"/"saves"/"setup"，
    // 命中的项用主题色 + 半粗 + 2px 主题色小条，点击其余项跳对应页面。
    static class InstanceTabs
    {
        public static UIElement Build(string current, string versionId)
        {
            var panel = new StackPanel();
            panel.Orientation = Orientation.Horizontal;
            panel.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(Item("概览", "overall", "instance:overall", current, versionId));
            panel.Children.Add(Item("Mod", "mod", "instance:mod", current, versionId));
            panel.Children.Add(Item("存档", "saves", "instance:saves", current, versionId));
            panel.Children.Add(Item("设置", "setup", "instance:setup", current, versionId));
            return panel;
        }

        static UIElement Item(string text, string key, string navKey, string current, string versionId)
        {
            bool sel = key == current;
            var wrap = new StackPanel();
            wrap.Margin = new Thickness(0, 0, 18, 0);
            wrap.Cursor = Cursors.Hand;
            var t = new TextBlock();
            t.Text = text;
            t.FontSize = 13;
            t.Foreground = Palette.B(sel ? ThemeManager.CurrentColor : Palette.Dim);
            if (sel) t.FontWeight = FontWeights.SemiBold;
            wrap.Children.Add(t);
            if (sel)
            {
                // 选中项底下贴一条 2px 主题色小条
                var bar = new Border();
                bar.Height = 2;
                bar.Margin = new Thickness(0, 3, 0, 0);
                bar.Background = Palette.B(ThemeManager.CurrentColor);
                wrap.Children.Add(bar);
            }
            wrap.MouseLeftButtonUp += delegate
            {
                if (!sel) Navigator.Go(navKey, versionId);
            };
            return wrap;
        }
    }
}
