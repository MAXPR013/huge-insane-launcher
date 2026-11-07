// HugeLauncher - 今日玩什么：点子库（内置）+ 收藏/已完成（本地）
// 远程词库以后由 ContentHub 拉取合并；这里先保证离线可用。
using System;
using System.Collections.Generic;
using System.IO;

namespace HugeLauncher
{
    class Idea
    {
        public string Text;
        public string Tag;
        public string Level;
        public Idea(string text, string tag, string level) { Text = text; Tag = tag; Level = level; }
    }

    static class Ideas
    {
        public static readonly List<Idea> All = new List<Idea>();
        static readonly HashSet<string> favs = new HashSet<string>();
        static readonly HashSet<string> done = new HashSet<string>();
        static int cursor = -1;
        static string storePath;

        static Ideas() { Build(); }

        static void Add(string text, string tag, string level) { All.Add(new Idea(text, tag, level)); }

        static void Build()
        {
            // 建筑
            Add("用草方块搭一栋房子", "建筑", "中等");
            Add("盖一座横跨峡谷的桥", "建筑", "费时");
            Add("在沙漠里建一座带圆顶的神殿", "建筑", "费时");
            Add("用石砖和玻璃搭一间地下书房", "建筑", "中等");
            Add("在树上建一个树屋营地", "建筑", "中等");
            Add("盖一座有灯塔的海边小屋", "建筑", "费时");
            Add("修一条石头路，然后骑马在上面跑一趟", "建筑", "简单");
            Add("给村庄加一圈围墙和路灯", "建筑", "中等");
            Add("建一个三层带地窖的农场主屋", "建筑", "费时");
            Add("用不同木头拼一面彩色墙", "建筑", "简单");
            Add("在雪原盖一间烟囱冒烟的小木屋", "建筑", "中等");
            Add("挖空一座山，做一间山体别墅", "建筑", "费时");
            Add("盖一座能看见海的高塔", "建筑", "中等");
            Add("用羊毛搭一个巨型蘑菇屋", "建筑", "中等");
            Add("给家修一条夜景灯光大道", "建筑", "简单");
            Add("建一座圆形斗兽场", "建筑", "费时");
            Add("在沼泽上搭一座吊脚楼", "建筑", "中等");
            Add("搭一个能自动收放的城门", "建筑", "费时");

            // 探索
            Add("找到海底神殿并拍张照", "探索", "费时");
            Add("徒步走到 5000 格外再回来", "探索", "费时");
            Add("找到一座林地府邸", "探索", "费时");
            Add("下到基岩层，带回一块下界合金", "探索", "费时");
            Add("在末地找到一座末地城", "探索", "费时");
            Add("找到雪原村庄，买点东西", "探索", "中等");
            Add("骑马横穿一次大草原", "探索", "简单");
            Add("沿着河一直走到它的入海口", "探索", "中等");
            Add("去一次蘑菇岛", "探索", "费时");
            Add("找到一个废弃矿井并走到底", "探索", "中等");
            Add("在海底找一处沉船", "探索", "中等");
            Add("爬到本地最高的一座山上看日出", "探索", "简单");
            Add("找一处竹林，做点竹子家具", "探索", "简单");
            Add("去恶地看看层叠的陶瓦", "探索", "中等");
            Add("找到一处远古城市，别被监守者抓住", "探索", "费时");
            Add("探索三处不同的洞穴生物群系", "探索", "中等");
            Add("划船沿河走一夜", "探索", "简单");
            Add("在沙漠里找到一口水井", "探索", "简单");

            // 红石
            Add("做一个自动甘蔗机", "红石", "中等");
            Add("做一个自动烤鸡机", "红石", "中等");
            Add("搭一条能自动分拣的仓库", "红石", "费时");
            Add("做一个隐藏的活塞门", "红石", "中等");
            Add("做一个可升降的电梯", "红石", "中等");
            Add("做一个自动农场并接上收集", "红石", "费时");
            Add("做一个红石灯时钟", "红石", "简单");
            Add("做一个自动门，走近自动开", "红石", "简单");
            Add("做一个矿车运输线", "红石", "中等");
            Add("做一个能自动补货的熔炉组", "红石", "费时");
            Add("做一个音乐盒演奏机", "红石", "费时");
            Add("做一个能自动收割的南瓜机", "红石", "中等");
            Add("做一个活塞电梯上二楼", "红石", "中等");
            Add("做一个刷怪笼改良塔", "红石", "费时");
            Add("做一个自动剪羊毛机", "红石", "中等");
            Add("做一个能自动喂食的动物圈", "红石", "中等");

            // 生存
            Add("活过第一个夜晚，不许挖洞", "生存", "简单");
            Add("只用木制工具活三天", "生存", "中等");
            Add("建一个完全自给自足的基地", "生存", "费时");
            Add("驯服一匹马并给它命名", "生存", "简单");
            Add("养一群羊，做一张自己的床", "生存", "简单");
            Add("钓到一条附魔书", "生存", "中等");
            Add("用村民交易凑齐一套钻石装备", "生存", "费时");
            Add("在困难难度下活过一夜", "生存", "中等");
            Add("驯服一只狼，带它去打猎", "生存", "简单");
            Add("种一片小麦田并装满一箱面包", "生存", "中等");
            Add("做一个属于自己的信标", "生存", "费时");
            Add("杀死一次凋灵", "生存", "费时");
            Add("带着猫去村庄交易", "生存", "中等");
            Add("在沙漠里靠仙人掌活三天", "生存", "中等");
            Add("只用钓鱼获取食物过一天", "生存", "中等");
            Add("给自己做一把满附魔的剑", "生存", "费时");
            Add("驯服一只马并骑马射箭", "生存", "中等");
            Add("在雪原里盖屋并活过暴雪夜", "生存", "中等");

            // 整活
            Add("把所有村民的房子涂成同一个颜色", "整活", "简单");
            Add("给每只羊起一个名字", "整活", "简单");
            Add("用 64 个南瓜堆一座塔", "整活", "简单");
            Add("在朋友家门口放满告示牌", "整活", "简单");
            Add("做一个全是门的迷宫", "整活", "中等");
            Add("用 TNT 挖一个矿坑（小心）", "整活", "中等");
            Add("把所有箱子命名成同一个", "整活", "简单");
            Add("给村庄修一条地铁", "整活", "费时");
            Add("在末地搭一个自拍台", "整活", "中等");
            Add("用冰和船做一条滑道", "整活", "中等");
            Add("把家搬到海底", "整活", "费时");
            Add("造一个巨型苦力怕雕像", "整活", "中等");
            Add("让一群鸡站在你屋顶上", "整活", "简单");
            Add("堆一座直达云层的方块塔", "整活", "中等");
            Add("给整片草地铺上路径", "整活", "费时");
            Add("用萤石在水下写一个字", "整活", "中等");

            // 更多建筑/探索补充
            Add("在悬崖边修一处观景平台", "建筑", "中等");
            Add("建一座带钟楼的小镇", "建筑", "费时");
            Add("挖一条通往矿区的隧道并装灯", "建筑", "中等");
            Add("给基地加一个屋顶花园", "建筑", "中等");
            Add("搭一座跨海的吊桥", "建筑", "费时");
            Add("找到一处樱花树林", "探索", "中等");
            Add("去一次下界的灵魂沙峡谷", "探索", "中等");
            Add("在海底找一处珊瑚礁", "探索", "中等");
            Add("找到一处冰刺之地", "探索", "中等");
            Add("在恶地找一处金矿", "探索", "简单");
            Add("做一个自动钓鱼机", "红石", "中等");
            Add("做一个能自动装填的发射器", "红石", "中等");
            Add("养一池热带鱼", "生存", "中等");
            Add("在村庄开一家自己的商店", "整活", "中等");
            Add("给自己盖一座雕像", "整活", "费时");
            Add("建一个观星台，晚上上去看星星", "建筑", "中等");
            Add("收集 16 种不同的花做花田", "生存", "简单");
            Add("用雪块堆一个雪人军团", "整活", "简单");
            Add("在沙漠中建一处绿洲", "建筑", "费时");
            Add("用红石做一条会发光的路", "红石", "中等");
        }

        static string StorePath
        {
            get
            {
                if (storePath == null) storePath = Path.Combine(Paths.LauncherDir, "ideas.dat");
                return storePath;
            }
        }

        public static void Load()
        {
            favs.Clear();
            done.Clear();
            string today = TodayKey();
            loadedDay = today;
            try
            {
                if (File.Exists(StorePath))
                {
                    string[] lines = File.ReadAllLines(StorePath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string ln = lines[i];
                        if (ln.StartsWith("fav|")) { favs.Add(ln.Substring(4)); continue; }
                        if (!ln.StartsWith("done|")) continue;
                        string rest = ln.Substring(5);
                        // 记录格式：done|yyyy-MM-dd|点子。老格式没有日期，按今天算（明天自然清掉）
                        int bar = rest.IndexOf('|');
                        if (bar > 0 && rest.Length > bar + 1)
                        {
                            string day = rest.Substring(0, bar);
                            if (day == today) done.Add(rest.Substring(bar + 1));
                        }
                        else
                        {
                            done.Add(rest);
                        }
                    }
                }
            }
            catch { }
            if (cursor < 0)
            {
                // 首次打开固定在「今日」那条，之后再换
                Idea t = Today();
                cursor = t == null ? FirstUnfinished() : All.IndexOf(t);
            }
        }

        static string loadedDay;
        static string TodayKey() { return DateTime.Now.ToString("yyyy-MM-dd"); }

        // 已完成的点子按天算：跨过 0 点就清空。返回 true 表示刚发生过跨天刷新
        public static bool CheckDaily()
        {
            string today = TodayKey();
            if (loadedDay == today) return false;
            Load();
            return true;
        }

        public static void Save()
        {
            try
            {
                string today = TodayKey();
                var sb = new System.Text.StringBuilder();
                foreach (string s in favs) sb.Append("fav|").Append(s).Append('\n');
                foreach (string s in done) sb.Append("done|").Append(today).Append('|').Append(s).Append('\n');
                File.WriteAllText(StorePath, sb.ToString());
            }
            catch { }
        }

        static int FirstUnfinished()
        {
            for (int i = 0; i < All.Count; i++) if (!done.Contains(All[i].Text)) return i;
            return 0;
        }

        // 每天固定一条：用日期做种子，同一天开两次看到同一条
        public static Idea Today()
        {
            if (All.Count == 0) return null;
            int seed = DateTime.Now.Year * 10000 + DateTime.Now.Month * 100 + DateTime.Now.Day;
            var rnd = new Random(seed);
            int idx = rnd.Next(All.Count);
            return All[idx];
        }

        public static Idea Current()
        {
            if (All.Count == 0) return null;
            if (cursor < 0 || cursor >= All.Count) cursor = FirstUnfinished();
            return All[cursor];
        }

        public static Idea Next()
        {
            if (All.Count == 0) return null;
            for (int i = 1; i <= All.Count; i++)
            {
                int idx = ((cursor < 0 ? 0 : cursor) + i) % All.Count;
                if (!done.Contains(All[idx].Text)) { cursor = idx; return All[idx]; }
            }
            cursor = (cursor + 1) % All.Count;
            return All[cursor];
        }

        public static bool IsFav(Idea i) { return i != null && favs.Contains(i.Text); }
        public static bool IsDone(Idea i) { return i != null && done.Contains(i.Text); }

        public static void ToggleFav(Idea i)
        {
            if (i == null) return;
            if (favs.Contains(i.Text)) favs.Remove(i.Text); else favs.Add(i.Text);
            Save();
        }

        public static void MarkDone(Idea i)
        {
            if (i == null) return;
            done.Add(i.Text);
            Save();
        }

        public static void UnmarkDone(Idea i)
        {
            if (i == null) return;
            done.Remove(i.Text);
            Save();
        }

        public static int DoneCount { get { return done.Count; } }
        public static int FavCount { get { return favs.Count; } }
    }
}
