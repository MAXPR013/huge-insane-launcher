// HugeLauncher.Ui.Pages - 更多·关于：版本信息 / 致谢 / 官网 / 开源许可 / 重新查看引导
// 导航 key: more:about
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using HugeLauncher.Ui;

namespace HugeLauncher.Ui.Pages
{
    class PageAbout : PageBase
    {
        public PageAbout()
        {
            var scroll = new SmoothScrollViewer();
            scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            var stack = new StackPanel();
            stack.Margin = new Thickness(16, 14, 16, 14);
            scroll.Content = stack;
            Children.Add(scroll);

            var title = new TextBlock();
            title.Text = "关于";
            title.FontSize = 17;
            title.FontWeight = FontWeights.SemiBold;
            title.Foreground = Palette.B(Palette.Text);
            title.Margin = new Thickness(0, 0, 0, 10);
            stack.Children.Add(title);

            var ver = new TextBlock();
            ver.Text = "HIL  " + MainWindow.Version + " " + MainWindow.Channel;
            ver.FontSize = 13;
            ver.Foreground = Palette.B(Palette.Text);
            stack.Children.Add(ver);

            var slogan = new TextBlock();
            slogan.Text = "下载、启动、工具，揉在一个窗口里。";
            slogan.FontSize = 12;
            slogan.Margin = new Thickness(0, 4, 0, 14);
            slogan.Foreground = Palette.B(Palette.Dim);
            stack.Children.Add(slogan);

            stack.Children.Add(Section("链接", new string[]
            {
                "官网  https://hugeinsane.pages.dev",
                "反馈  官网最下面的反馈页，把日志贴进去即可",
                "源码  github.com/MAXPR013/huge-insane-launcher"
            }));

            stack.Children.Add(Section("致谢与许可", new string[]
            {
                "cubiomes（Cubitect / xpple，MIT）—— 种子地图的世界生成",
                "Mojang —— Minecraft 相关素材版权归其所有，本项目与 Mojang 无关",
                "所有在 B 站、苦力怕论坛帮忙测试和反馈的朋友"
            }));

            var btn = new FlatButton("重新查看新手引导");
            btn.SetFontSize(12);
            btn.SetPadding(new Thickness(14, 5, 14, 5));
            btn.SetCornerRadius(new CornerRadius(6));
            btn.Margin = new Thickness(0, 16, 0, 0);
            btn.HorizontalAlignment = HorizontalAlignment.Left;
            btn.Click += delegate { RequestRestartOnboard(); Navigator.Go("home:dashboard"); };
            stack.Children.Add(btn);
        }

        UIElement Section(string head, string[] lines)
        {
            var card = new Border();
            card.Background = Palette.B(Palette.Panel);
            card.CornerRadius = new CornerRadius(8);
            card.Padding = new Thickness(14, 10, 14, 12);
            card.Margin = new Thickness(0, 0, 0, 10);
            var sp = new StackPanel();
            card.Child = sp;
            var t = new TextBlock();
            t.Text = head;
            t.FontSize = 12;
            t.FontWeight = FontWeights.SemiBold;
            t.Foreground = Palette.B(Palette.Text);
            t.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(t);
            for (int i = 0; i < lines.Length; i++)
            {
                var l = new TextBlock();
                l.Text = lines[i];
                l.FontSize = 11.5;
                l.TextWrapping = TextWrapping.Wrap;
                l.Margin = new Thickness(0, 0, 0, 5);
                l.Foreground = Palette.B(Palette.Dim);
                sp.Children.Add(l);
            }
            return card;
        }
    }
}
