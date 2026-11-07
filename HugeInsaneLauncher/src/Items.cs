// HugeLauncher - 物品贴图：从游戏 jar 抽出的 PNG，构建时打成 items.zip 内嵌进 exe，
// 首次启动释放到 <LauncherDir>\Images\Items\<id>.png，之后直接读文件。
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HugeLauncher
{
    static class Items
    {
        static readonly Dictionary<string, BitmapSource> cache = new Dictionary<string, BitmapSource>();
        static readonly Dictionary<string, string> cn = BuildCn();

        public static string Dir { get { return Path.Combine(Paths.LauncherDir, "Images", "Items"); } }

        public static string FileFor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return Path.Combine(Dir, id + ".png");
        }

        // 取贴图；没有文件返回 null（调用方画占位方块）
        public static BitmapSource Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            BitmapSource b;
            if (cache.TryGetValue(id, out b)) return b;
            try
            {
                string f = FileFor(id);
                if (File.Exists(f))
                {
                    var bi = new BitmapImage();
                    bi.BeginInit();
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.UriSource = new Uri(f, UriKind.Absolute);
                    bi.EndInit();
                    bi.Freeze();
                    cache[id] = bi;
                    return bi;
                }
            }
            catch { }
            cache[id] = null;
            return null;
        }

        // id → 中文名（网格 tooltip 与材料标签用）；没收录的直接显示 id
        public static string Name(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string v;
            return cn.TryGetValue(id, out v) ? v : id;
        }

        // 有些方块在游戏里是 3D 模型（栅栏/台阶/按钮/床…），没有平面贴图。
        // 这些不拿别的贴图顶替（会取错），而是按材质色程序画图标：
        // cube=等距方块 / slab=半高 / stairs=台阶 / fence=栏杆 / plate=薄板 / bed=床 / shield=盾
        static readonly Dictionary<string, string> blockColor = new Dictionary<string, string>();
        static readonly Dictionary<string, string> blockKind = new Dictionary<string, string>();
        static readonly Dictionary<string, BitmapSource> procCache = new Dictionary<string, BitmapSource>();

        static void BC(string id, string color, string kind)
        {
            blockColor[id] = color;
            blockKind[id] = kind;
        }

        static Items()
        {
            BC("oak_fence", "#B08A52", "fence");
            BC("oak_fence_gate", "#B08A52", "fence");
            BC("white_carpet", "#E8E8E8", "slab");
            BC("white_bed", "#E8E8E8", "bed");
            BC("stone_slab", "#8A8A8A", "slab");
            BC("stone_stairs", "#8A8A8A", "stairs");
            BC("stone_button", "#8A8A8A", "plate");
            BC("stone_pressure_plate", "#8A8A8A", "plate");
            BC("stone_brick_slab", "#7A7A7A", "slab");
            BC("sandstone_slab", "#D8CF9A", "slab");
            BC("shield", "#8A6A3A", "shield");
            BC("ender_chest", "#2F4A4A", "cube");
            BC("trapped_chest", "#9C7A4A", "cube");
            BC("sticky_piston", "#8A9A6A", "cube");
        }

        public static BitmapSource Procedural(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            BitmapSource cached;
            if (procCache.TryGetValue(id, out cached)) return cached;
            string hex;
            if (!blockColor.TryGetValue(id, out hex)) { procCache[id] = null; return null; }
            string kind;
            if (!blockKind.TryGetValue(id, out kind)) kind = "cube";
            BitmapSource bmp = null;
            try { bmp = DrawIcon(hex, kind); } catch { }
            procCache[id] = bmp;
            return bmp;
        }

        static Color Shade(Color c, double k)
        {
            return Color.FromRgb((byte)(c.R * k), (byte)(c.G * k), (byte)(c.B * k));
        }

        static BitmapSource DrawIcon(string hex, string kind)
        {
            Color c = (Color)ColorConverter.ConvertFromString(hex);
            Color top = c;
            Color left = Shade(c, 0.72);
            Color right = Shade(c, 0.55);
            var dv = new System.Windows.Media.DrawingVisual();
            using (System.Windows.Media.DrawingContext dc = dv.RenderOpen())
            {
                if (kind == "fence")
                {
                    var rail = new SolidColorBrush(Shade(c, 0.85));
                    var post = new SolidColorBrush(c);
                    dc.DrawRectangle(rail, null, new System.Windows.Rect(3, 18, 58, 7));    // 上横档
                    dc.DrawRectangle(rail, null, new System.Windows.Rect(3, 38, 58, 7));    // 下横档
                    dc.DrawRectangle(post, null, new System.Windows.Rect(6, 6, 9, 52));     // 左柱
                    dc.DrawRectangle(post, null, new System.Windows.Rect(28, 6, 9, 52));    // 中柱
                    dc.DrawRectangle(post, null, new System.Windows.Rect(50, 6, 9, 52));    // 右柱
                }
                else if (kind == "stairs")
                {
                    dc.DrawGeometry(new SolidColorBrush(left), null, Poly(
                        0.05, 0.55, 0.55, 0.55, 0.55, 0.95, 0.05, 0.95));
                    dc.DrawGeometry(new SolidColorBrush(top), null, Poly(
                        0.05, 0.15, 0.55, 0.15, 0.55, 0.55, 0.05, 0.55));
                    dc.DrawGeometry(new SolidColorBrush(right), null, Poly(
                        0.55, 0.55, 0.95, 0.75, 0.95, 0.95, 0.55, 0.95));
                }
                else if (kind == "plate")
                {
                    dc.DrawGeometry(new SolidColorBrush(top), null, Poly(
                        0.15, 0.45, 0.85, 0.45, 0.85, 0.62, 0.15, 0.62));
                    dc.DrawGeometry(new SolidColorBrush(left), null, Poly(
                        0.15, 0.62, 0.85, 0.62, 0.85, 0.72, 0.15, 0.72));
                }
                else if (kind == "bed")
                {
                    dc.DrawGeometry(new SolidColorBrush(Shade(c, 0.75)), null, Poly(
                        0.05, 0.45, 0.95, 0.30, 0.95, 0.62, 0.05, 0.77));
                    dc.DrawGeometry(new SolidColorBrush(top), null, Poly(
                        0.05, 0.30, 0.95, 0.15, 0.95, 0.30, 0.05, 0.45));
                    dc.DrawGeometry(new SolidColorBrush(Colors.White), null, Poly(
                        0.08, 0.26, 0.32, 0.22, 0.32, 0.36, 0.08, 0.40));
                }
                else if (kind == "shield")
                {
                    dc.DrawGeometry(new SolidColorBrush(top), null, Poly(
                        0.5, 0.08, 0.86, 0.24, 0.80, 0.62, 0.5, 0.92, 0.20, 0.62, 0.14, 0.24));
                    dc.DrawGeometry(new SolidColorBrush(Shade(c, 0.6)), null, Poly(
                        0.5, 0.20, 0.70, 0.30, 0.66, 0.56, 0.5, 0.74, 0.34, 0.56, 0.30, 0.30));
                }
                else
                {
                    double e = kind == "slab" ? 0.30 : 0.55;
                    dc.DrawGeometry(new SolidColorBrush(top), null, Poly(
                        0.5, 0.05, 0.95, 0.30, 0.5, 0.55, 0.05, 0.30));
                    dc.DrawGeometry(new SolidColorBrush(left), null, Poly(
                        0.05, 0.30, 0.5, 0.55, 0.5, 0.55 + e, 0.05, 0.30 + e));
                    dc.DrawGeometry(new SolidColorBrush(right), null, Poly(
                        0.5, 0.55, 0.95, 0.30, 0.95, 0.30 + e, 0.5, 0.55 + e));
                }
            }
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(64, 64, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        // 坐标按 0..1 归一化传入，这里按 64px 画布放大
        static System.Windows.Media.Geometry Poly(params double[] xy)
        {
            const double S = 64;
            var g = new System.Windows.Media.StreamGeometry();
            using (System.Windows.Media.StreamGeometryContext ctx = g.Open())
            {
                ctx.BeginFigure(new System.Windows.Point(xy[0] * S, xy[1] * S), true, true);
                for (int i = 2; i + 1 < xy.Length; i += 2)
                    ctx.LineTo(new System.Windows.Point(xy[i] * S, xy[i + 1] * S), true, false);
            }
            g.Freeze();
            return g;
        }

        static Dictionary<string, string> BuildCn()
        {
            var d = new Dictionary<string, string>();
            Action<string, string> A = delegate(string id, string name) { d[id] = name; };
            A("oak_log", "原木"); A("oak_planks", "木板"); A("stick", "木棍");
            A("cobblestone", "圆石"); A("stone", "石头"); A("smooth_stone", "平滑石头");
            A("stone_slab", "石台阶"); A("stone_stairs", "石楼梯"); A("stone_bricks", "石砖");
            A("stone_brick_slab", "石砖台阶"); A("sandstone", "砂岩"); A("sandstone_slab", "砂岩台阶");
            A("oak_slab", "木台阶"); A("crafting_table", "工作台"); A("chest", "箱子");
            A("furnace", "熔炉"); A("torch", "火把"); A("soul_torch", "灵魂火把");
            A("coal", "煤炭"); A("charcoal", "木炭"); A("ladder", "梯子");
            A("oak_boat", "木船"); A("bucket", "桶"); A("shears", "剪刀");
            A("flint_and_steel", "打火石"); A("fishing_rod", "钓鱼竿"); A("bow", "弓");
            A("arrow", "箭"); A("shield", "盾牌"); A("white_bed", "床");
            A("oak_door", "门"); A("oak_trapdoor", "活板门"); A("oak_fence", "栅栏");
            A("oak_fence_gate", "栅栏门"); A("oak_sign", "告示牌"); A("painting", "画");
            A("item_frame", "物品展示框"); A("armor_stand", "盔甲架");
            A("leather", "皮革"); A("iron_ingot", "铁锭"); A("gold_ingot", "金锭");
            A("copper_ingot", "铜锭"); A("diamond", "钻石"); A("netherite_ingot", "下界合金锭");
            A("netherite_scrap", "下界合金碎片"); A("iron_block", "铁块");
            A("wheat", "小麦"); A("sugar", "糖"); A("egg", "鸡蛋"); A("milk_bucket", "牛奶桶");
            A("cocoa_beans", "可可豆"); A("pumpkin", "南瓜"); A("red_mushroom", "红蘑菇");
            A("brown_mushroom", "棕蘑菇"); A("bowl", "碗"); A("potato", "土豆");
            A("beef", "生牛肉"); A("porkchop", "生猪排"); A("chicken", "生鸡肉");
            A("mutton", "生羊肉"); A("cod", "生鳕鱼"); A("baked_potato", "烤土豆");
            A("cooked_beef", "熟牛肉"); A("cooked_porkchop", "熟猪排"); A("cooked_chicken", "熟鸡肉");
            A("cooked_mutton", "熟羊肉"); A("cooked_cod", "熟鳕鱼");
            A("bread", "面包"); A("cake", "蛋糕"); A("cookie", "曲奇"); A("pumpkin_pie", "南瓜派");
            A("mushroom_stew", "蘑菇煲"); A("hay_block", "干草捆");
            A("sugar_cane", "甘蔗"); A("paper", "纸"); A("book", "书"); A("bookshelf", "书架");
            A("obsidian", "黑曜石"); A("blaze_rod", "烈焰棒"); A("ender_eye", "末影之眼");
            A("glass", "玻璃"); A("glass_pane", "玻璃板"); A("nether_star", "下界之星");
            A("brick", "红砖"); A("bricks", "砖块"); A("sand", "沙子");
            A("iron_ore", "铁矿石"); A("gold_ore", "金矿石"); A("copper_ore", "铜矿石");
            A("clay_ball", "黏土球"); A("string", "线"); A("feather", "羽毛");
            A("white_wool", "羊毛"); A("white_carpet", "地毯"); A("flint", "燧石");
            A("redstone", "红石"); A("quartz", "石英"); A("glowstone", "萤石");
            A("gunpowder", "火药"); A("iron_nugget", "铁粒"); A("slime_ball", "黏液球");
            A("amethyst_shard", "紫水晶碎片"); A("chorus_fruit", "紫颂果");
            A("campfire", "营火"); A("lantern", "灯笼"); A("soul_lantern", "灵魂灯笼");
            A("smoker", "烟熏炉"); A("blast_furnace", "高炉"); A("stonecutter", "切石机");
            A("cartography_table", "制图台"); A("fletching_table", "制箭台");
            A("smithing_table", "锻造台"); A("loom", "织布机"); A("barrel", "木桶");
            A("composter", "堆肥桶"); A("compass", "指南针"); A("clock", "时钟");
            A("map", "地图"); A("spyglass", "望远镜");
            A("redstone_torch", "红石火把"); A("repeater", "中继器"); A("comparator", "比较器");
            A("piston", "活塞"); A("sticky_piston", "粘性活塞"); A("observer", "观察者");
            A("hopper", "漏斗"); A("dropper", "投掷器"); A("dispenser", "发射器");
            A("minecart", "矿车"); A("rail", "铁轨"); A("powered_rail", "动力铁轨");
            A("detector_rail", "探测铁轨"); A("tnt", "TNT"); A("lever", "拉杆");
            A("stone_button", "石按钮"); A("stone_pressure_plate", "石压力板");
            A("daylight_detector", "阳光探测器"); A("note_block", "音符盒");
            A("jukebox", "唱片机"); A("redstone_lamp", "红石灯"); A("target", "标靶");
            A("trapped_chest", "陷阱箱"); A("tripwire_hook", "绊线钩");
            A("enchanting_table", "附魔台"); A("anvil", "铁砧"); A("brewing_stand", "酿造台");
            A("cauldron", "炼药锅"); A("ender_chest", "末影箱"); A("beacon", "信标");
            A("iron_bars", "铁栏杆");
            // 工具/防具
            string[] mats = { "wooden:木", "stone:石", "iron:铁", "golden:金", "diamond:钻石" };
            string[] kinds = { "pickaxe:镐", "axe:斧", "shovel:锹", "sword:剑", "hoe:锄" };
            string[] armors = { "helmet:头盔", "chestplate:胸甲", "leggings:护腿", "boots:靴子" };
            for (int i = 0; i < mats.Length; i++)
            {
                string[] mp = mats[i].Split(':');
                for (int j = 0; j < kinds.Length; j++)
                    A(mp[0] + "_" + kinds[j].Split(':')[0], mp[1] + kinds[j].Split(':')[1]);
                if (mp[0] == "leather" || mp[0] == "iron" || mp[0] == "diamond")
                    for (int j = 0; j < armors.Length; j++)
                        A(mp[0] + "_" + armors[j].Split(':')[0], mp[1] + armors[j].Split(':')[1]);
            }
            A("netherite_pickaxe", "下界合金镐");
            return d;
        }
    }
}
