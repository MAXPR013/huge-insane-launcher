// HugeLauncher.Ui - 功能帮助目录：所有「?」小圆钮的内容源 + H史气泡出口
// 文案集中在本文件 Entries 里，改文案只动这一个文件。
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace HugeLauncher.Ui
{
    class HelpEntry
    {
        public string Id;
        public string Title;
        public string Body;
    }

    static class HelpCatalog
    {
        // 主窗口注册：收到条目就弹 H史气泡（内置完整回答，不走 AI）
        public static Action<HelpEntry> BubbleHandler;

        static readonly Dictionary<string, HelpEntry> Entries = new Dictionary<string, HelpEntry>();

        static HelpCatalog()
        {
            // ==================== 一键开服 ====================
            Add("server:guide", "一键开服",
                "把你的电脑变成一台 MC 服务器，全程在这里完成。\n\n" +
                "1. 左上选游戏版本（只列出已安装的，没有就先去「下载」页装）；\n" +
                "2. 填内存、核心、端口，点「开服！！」；\n" +
                "3. 开好后控制台头部会亮出地址，点复制发给同一网络的朋友就能进。\n\n" +
                "开过服的版本会存成「实例」，下次在右上实例下拉框选它，一键启动 / 关闭。\n" +
                "第一次开服会自动下载服务端核心并同意 EULA，属于正常现象。");
            Add("server:memory", "内存",
                "分给这台服务器的内存（MB）。\n\n" +
                "参考：\n" +
                "· 纯原版 2-5 人：2048 足够；\n" +
                "· 10 人左右或挂了数据包：4096；\n" +
                "· 大型整合包开服：8192 起。\n\n" +
                "别超过物理内存的一半，否则服务器和系统会一起卡。");
            Add("server:core", "核心类型",
                "服务端核心 = 服务器的操作系统。\n\n" +
                "· Vanilla：官方原版核心，最纯净，不能装插件；\n" +
                "· Paper：高优化核心，支持插件，大多数人选它。\n\n" +
                "Forge / Fabric / NeoForge 服务端正在开发中。");
            Add("server:port", "端口",
                "服务器的门牌号，MC 默认 25565。\n\n" +
                "留空 = 自动挑一个没被占用的端口，一般不用管。\n" +
                "只有端口冲突、或你想让地址固定不变时才手填。\n\n" +
                "朋友连接时地址要写成 IP:端口，例如 192.168.1.5:25565。");
            Add("server:maxplayers", "可容人数",
                "服务器最多进多少人，按内存自动算出（每 100MB ≈ 1 人，2048MB 就是 20 人）。\n\n" +
                "想提高上限就加内存；人不多但卡顿，优先降视距而不是加人。");
            Add("server:tunnel", "Huge 隧道",
                "朋友和你在不同网络（比如 TA 在另一个城市）、而你家又没有公网 IPv6 时，打开它。\n\n" +
                "开启后 HIL 会经 Huge 中继服务器打通隧道，生成 6 位房间码。\n" +
                "把房间码发给朋友，TA 在「联机大厅」输入即可加入。\n\n" +
                "连接优先尝试 P2P 打洞直连（延迟低），打不通才走中继转发（稍慢但稳）。\n" +
                "同一网络的朋友用不上它——直接给局域网地址就行。");
            Add("server:console", "控制台",
                "服务器的后台日志 + 操作台。\n\n" +
                "上面滚动的是服务端日志；下面输入框直接发命令（不用打 /）：\n" +
                "· op 玩家名 —— 给管理员权限；\n" +
                "· say 大家好 —— 全服广播；\n" +
                "· stop —— 保存并关服（也可以直接点上面的按钮）。");

            // ==================== 联机大厅 ====================
            Add("hall:join", "加入房间",
                "朋友（腐竹）开好房间后会把 6 位房间码发给你，填进来点「加入」。\n\n" +
                "成功后打开 MC → 多人游戏：\n" +
                "· 房间一般会自动出现在「局域网游戏」列表，双击就进；\n" +
                "· 没冒出来就点「添加服务器」，把页面上那串大字地址原样填进去。\n\n" +
                "注意：游戏必须是从 HIL 启动的，否则会被「无效会话」拦在门外（HIL 会给它启动的游戏注入联机补丁）。");
            Add("hall:joined", "加入成功后",
                "房间会自动经局域网广播，出现在 MC 多人游戏列表。\n\n" +
                "等几秒没冒出来（多半是系统防火墙拦了广播）：\n" +
                "点「多人游戏 → 添加服务器」，地址照抄页面上这串大字。\n\n" +
                "房间码只对本次连接有效；掉线后重新输入通常还能进，不行就让腐竹重开一次。");
            Add("hall:lan", "局域网创建房间",
                "把你正在玩的单人世界开放给朋友。\n\n" +
                "1. MC 里按 Esc →「对局域网开放」，聊天栏会提示一个端口号；\n" +
                "2. 把端口号填到这里，点「创建房间」；\n" +
                "3. 把生成的房间码发给朋友，TA 在本页上方输入即可加入。\n\n" +
                "注意：你的世界必须是从 HIL 启动的（已注入联机补丁，离线登录的朋友才进得来）。");

            // ==================== 设置（全局） ====================
            Add("setup:memory", "内存分配",
                "给游戏分配的内存（MB），是所有版本的默认值（实例详情的设置页可按版本单独覆盖）。\n\n" +
                "参考：\n" +
                "· 原版：2048 足够；\n" +
                "· 中型模组包：4096；\n" +
                "· 大型整合包：8192。\n\n" +
                "别开太大：超过物理内存一半反而会卡。");
            Add("setup:priority", "进程优先级",
                "游戏进程的 CPU 调度优先级。\n\n" +
                "· 正常：推荐，系统调度最稳；\n" +
                "· 高于正常 / 高：低配机可以试，帧率可能略升，但会拖慢后台程序。\n\n" +
                "不确定就保持「正常」。");
            Add("setup:gc", "垃圾回收（GC）优化",
                "给游戏 JVM 附加 HIL 调好的垃圾回收参数，专治「每隔几秒卡一下」的卡顿尖峰。\n\n" +
                "Java 21 的版本自动用分代 ZGC，老版本用调优 G1GC。\n\n" +
                "推荐开启；实例详情的设置页可按版本单独关。");
            Add("setup:gpu", "显卡调度",
                "双显卡笔记本专用：让 Windows 把游戏调度到独立显卡跑，帧率明显提升。\n\n" +
                "点「指定独显运行」后下次启动游戏生效；「恢复默认」还给系统调度。\n" +
                "只有一块显卡的机器不用管。");
            Add("setup:anim", "动画速度",
                "启动器界面动画的播放速度：\n\n" +
                "· 0 = 关闭动画（最跟手）；\n" +
                "· 1x = 默认；\n" +
                "· 2x = 慢放（欣赏用）。\n\n" +
                "只影响界面，不影响游戏帧率。");
            Add("setup:glass", "液态玻璃",
                "iOS 风格的材质皮肤：按钮、开关、滑条、指示条盖上液态玻璃质感。\n\n" +
                "关掉 = 工业风（硬朗方块、零玻璃动画），低配机器更省。\n" +
                "这个开关本身就是玻璃做的，所见即所得。");
            Add("setup:theme_skin", "主题",
                "三套材质皮肤，随时可切，不丢当前页面。\n\n" +
                "· 流光：液态玻璃，导航 pill 悬浮在文字上方，透镜会弯曲文字；默认。\n" +
                "· 霜面：取桌面壁纸模糊 40px 做磨砂底，控件半透明，文字背后有磨砂 pill。\n" +
                "· 素底：纯色工业风，零玻璃动画，性能最稳，老机器首选。\n\n" +
                "霜面会读取当前桌面壁纸；换壁纸后重新打开启动器生效。");
            Add("setup:layout", "布局",
                "经典横排：左侧史蒂夫 + 顶部一级标签 + 118px 二级边栏 + 内容。\n\n" +
                "纵向舞台：史蒂夫独立成左卡，中间 10px 分缝，右侧 72px 竖轨一级 + 横向二级 + 内容。\n\n" +
                "切换会重建界面，但会回到当前页面，不丢下载/实例状态。");
            Add("setup:floatball", "悬浮球",
                "桌面上的史蒂夫小球：主窗最小化、或游戏运行时出现。\n\n" +
                "· 单击展开极简面板（H史聊天 / 下载进度 / 服务器控制台 / 快速启动）；\n" +
                "· 双击切回全窗；\n" +
                "· 按住拖动换位置（靠边自动镜像）。");
            Add("setup:mirror", "Modrinth 镜像",
                "模组 / 资源包 / 整合包的下载源。\n\n" +
                "留空 = 官方直连。如果你的网络访问 Modrinth 慢或失败，\n" +
                "可以填自建反代地址（教程在 HugeInsaneLauncher/tools/modrinth-proxy-worker.js）。");
            Add("setup:lightmode", "亮色模式",
                "把整套界面换成白底深字（包括输入框、下拉框、滑条）。\n\n" +
                "切换带渐变过渡动画：罩子盖下、换肤、揭开，一气呵成。\n" +
                "习惯深色就关掉它，随时切回来。");
            Add("setup:daynight", "自动调色",
                "根据时间自动切换亮/暗色：白天（7:00 - 19:00）亮色，其余时间暗色。\n\n" +
                "开着它时，上面的「亮色模式」手动开关会被托管（变灰），交给我每 5 分钟看一眼天色。\n" +
                "切换同样是渐变过渡，不会闪你一下。");
            Add("setup:slime", "流动史莱姆",
                "背景是一团会动的色块：不停互相挤压、融合，颜色取自你的桌面壁纸。\n\n" +
                "「背景模糊」滑条调色块本身的柔化程度。\n\n" +
                "史蒂夫和液态玻璃不参与。动画速度为 0 时色块静止，当磨砂壁纸用。");
            Add("setup:autoupdate", "自动更新",
                "启动器每次开张时，去官网悄悄比对一下版本号。\n\n" +
                "发现新版会先弹气泡问你，不会自作主张；同意后自动下载、自动替换自己并重启。\n\n" +
                "关掉它也可以随时点旁边的「检查更新」手动查。");
            Add("setup:ai", "AI 史蒂夫",
                "H史 = HIL 内置的 AI 助手，能聊天、看崩溃日志、帮你修游戏。\n\n" +
                "· 不填 API Key：免费用 HIL 提供的公共通道，开箱即用；\n" +
                "· 填自己的 DeepSeek Key：优先走你的 Key，额度和速度自己掌控；\n" +
                "· API 端点：仅自有 Key 时生效，免费通道用不到。\n\n" +
                "拟人动画：开了之后 H史 操作时，史蒂夫会在界面上实时演戏。");

            // ==================== 账号 ====================
            Add("acct:external", "外置登录（authlib-injector）",
                "外置登录 = authlib-injector：皮肤站/私服认证服务器签发的账号，皮肤与联机档案由该服务器提供。\n\n" +
                "我们自己的 Huge 账号将来也会以同样方式入住这里。");

            // ==================== 版本设置（每版本覆盖） ====================
            Add("ver:memory", "游戏内存（本版本）",
                "只给当前这个版本单独设内存，保存后覆盖全局默认值。\n\n" +
                "「智能」按钮按你的物理内存和这个版本的模组数量自动估算（给系统预留 3GB）。\n\n" +
                "参考：原版 2048 / 中型模组包 4096 / 大型整合包 8192。");
            Add("ver:gc", "性能优化（本版本）",
                "这个版本独立的 GC 优化开关，覆盖全局设置。\n\n" +
                "Java 21 的版本用分代 ZGC，老版本用调优 G1GC；推荐开启。");
            Add("ver:perf", "写入性能优先画面设置",
                "把低视距 / 关垂直同步 / 关实体阴影等性能向选项，合并写进这个版本的 options.txt。\n\n" +
                "已有的按键和键位设置不受影响，下次启动游戏生效。\n\n" +
                "适合进游戏就卡、又懒得一项项调画面的人。");
        }

        static void Add(string id, string title, string body)
        {
            Entries[id] = new HelpEntry { Id = id, Title = title, Body = body };
        }

        public static void Show(string id)
        {
            HelpEntry e;
            if (!Entries.TryGetValue(id, out e)) return;
            var h = BubbleHandler;
            if (h != null) h(e);
        }

        // 「?」小圆钮：贴在控件后面，点了弹 H史气泡讲这条
        public static FrameworkElement Dot(string id)
        {
            var b = new Border();
            b.Width = 17;
            b.Height = 17;
            b.CornerRadius = new CornerRadius(9);
            b.Background = Palette.B(Palette.GlassA(26));
            b.BorderBrush = Palette.B(Palette.GlassA(80));
            b.BorderThickness = new Thickness(1);
            b.Cursor = Cursors.Hand;
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Margin = new Thickness(6, 0, 0, 0);
            var t = new TextBlock();
            t.Text = "?";
            t.FontSize = 10.5;
            t.Foreground = Palette.B(Palette.GlassA(200));
            t.HorizontalAlignment = HorizontalAlignment.Center;
            t.VerticalAlignment = VerticalAlignment.Center;
            b.Child = t;
            HelpEntry e;
            b.ToolTip = Entries.TryGetValue(id, out e) ? e.Title : "这是什么？";
            b.MouseEnter += delegate { b.Background = Palette.B(Palette.GlassA(64)); };
            b.MouseLeave += delegate { b.Background = Palette.B(Palette.GlassA(26)); };
            b.MouseLeftButtonUp += delegate(object s, MouseButtonEventArgs ev) { ev.Handled = true; Show(id); };
            return b;
        }
    }
}
