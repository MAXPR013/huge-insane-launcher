// HugeLauncher - 合成查询：内置常用合成表（离线可用）
// 每条配方带「产物贴图 id + 3×3 形状（9 格，'.' 为空）」，详情页据此画合成网格；
// 熔炼类没有形状（Grid=null），只列输入材料。
using System;
using System.Collections.Generic;

namespace HugeLauncher
{
    class Recipe
    {
        public string Name;      // 产物
        public int Count;        // 产出数量
        public string Mats;      // 材料（人类可读，搜索用）
        public string Station;   // 制作台：工作台 / 熔炉 / 高炉 / 烟熏炉 / 营火 / 切石机
        public string Out;       // 产物贴图 id（item 或 block）
        public string[] Grid;    // 9 格材料 id，'.' = 空；null = 无形状（熔炼）
        public Recipe(string name, int count, string mats, string station, string outId, string[] grid)
        {
            Name = name; Count = count; Mats = mats; Station = station; Out = outId; Grid = grid;
        }
        public string Line { get { return Count > 1 ? Name + " ×" + Count : Name; } }
    }

    static class Recipes
    {
        public static readonly List<Recipe> All = new List<Recipe>();

        static Recipes() { Build(); }

        // 有形状：grid 为 9 个格子，逗号分隔，'.' 表示空
        static void G(string name, int n, string mats, string outId, string grid)
        {
            All.Add(new Recipe(name, n, mats, "工作台", outId, Split(grid)));
        }
        static void GS(string name, int n, string mats, string station, string outId, string grid)
        {
            All.Add(new Recipe(name, n, mats, station, outId, Split(grid)));
        }
        // 无形状：熔炼/切石这类单输入
        static void S(string name, int n, string mats, string station, string outId)
        {
            All.Add(new Recipe(name, n, mats, station, outId, null));
        }

        static string[] Split(string s)
        {
            string[] a = s.Split(',');
            var r = new string[9];
            for (int i = 0; i < 9; i++)
            {
                string v = i < a.Length ? a[i].Trim() : ".";
                r[i] = (v == "." || v.Length == 0) ? null : v;
            }
            return r;
        }

        // 工具/防具家族：按 3×3 模板批量生成，避免手写几十条
        static void ToolSet(string mat, string matId, string prefix)
        {
            string[] kinds = { "pickaxe", "axe", "shovel", "sword", "hoe" };
            string[] cn = { "镐", "斧", "锹", "剑", "锄" };
            string matName = mat == "木" ? "木板" : (mat == "石" ? "圆石" : (mat == "铁" ? "铁锭" : (mat == "金" ? "金锭" : "钻石")));
            string[] pats =
            {
                "M,M,M,.,S,.,.,S,.",   // 镐
                "M,M,.,M,S,.,.,S,.",   // 斧
                ".,M,.,.,S,.,.,S,.",   // 锹
                ".,M,.,.,M,.,.,S,.",   // 剑
                "M,M,.,.,S,.,.,S,."    // 锄
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                string g = pats[i].Replace("M", matId).Replace("S", "stick");
                string cnMat;
                if (i == 3) cnMat = matName + "×2 + 木棍×1";        // 剑
                else if (i == 2) cnMat = matName + "×1 + 木棍×2";   // 锹
                else cnMat = matName + "×3 + 木棍×2";
                All.Add(new Recipe(mat + cn[i], 1, cnMat, "工作台", prefix + "_" + kinds[i], Split(g)));
            }
        }

        static void ArmorSet(string mat, string matId, string prefix)
        {
            string[] kinds = { "helmet", "chestplate", "leggings", "boots" };
            string[] cn = { "头盔", "胸甲", "护腿", "靴子" };
            int[] cost = { 5, 8, 7, 4 };
            string[] pats =
            {
                "M,M,M,M,.,M,.,.,.",
                "M,.,M,M,M,M,M,M,M",
                "M,M,M,M,.,M,M,.,M",
                "M,.,M,M,.,M,.,.,."
            };
            for (int i = 0; i < kinds.Length; i++)
            {
                string g = pats[i].Replace("M", matId);
                All.Add(new Recipe(mat + cn[i], 1, mat + "×" + cost[i], "工作台",
                    prefix + "_" + kinds[i], Split(g)));
            }
        }

        static void Build()
        {
            // ---- 基础 ----
            G("木板", 4, "原木×1", "oak_planks", "oak_log,.,.,.,.,.,.,.,.");
            G("木棍", 4, "木板×2", "stick", "oak_planks,.,.,oak_planks,.,.,.,.,.");
            G("工作台", 1, "木板×4", "crafting_table", "oak_planks,oak_planks,.,oak_planks,oak_planks,.,.,.,.");
            G("箱子", 1, "木板×8", "chest", "oak_planks,oak_planks,oak_planks,oak_planks,.,oak_planks,oak_planks,oak_planks,oak_planks");
            G("熔炉", 1, "圆石×8", "furnace", "cobblestone,cobblestone,cobblestone,cobblestone,.,cobblestone,cobblestone,cobblestone,cobblestone");
            G("火把", 4, "煤炭×1 + 木棍×1", "torch", ".,coal,.,.,stick,.,.,.,.");
            G("梯子", 3, "木棍×7", "ladder", "stick,.,stick,stick,stick,stick,stick,.,stick");
            G("木船", 1, "木板×5", "oak_boat", "oak_planks,.,oak_planks,oak_planks,oak_planks,oak_planks,.,.,.");
            G("桶", 1, "铁锭×3", "bucket", ".,iron_ingot,.,.,iron_ingot,.,.,iron_ingot,.");
            G("剪刀", 1, "铁锭×2", "shears", ".,iron_ingot,.,iron_ingot,.,.,.,.,.");
            G("打火石", 1, "铁锭×1 + 燧石×1", "flint_and_steel", ".,.,.,iron_ingot,flint,.,.,.,.");
            G("钓鱼竿", 1, "木棍×3 + 线×2", "fishing_rod", ".,.,stick,.,stick,string,stick,.,string");
            G("弓", 1, "木棍×3 + 线×3", "bow", ".,stick,string,stick,.,string,.,stick,string");
            G("箭", 4, "燧石×1 + 木棍×1 + 羽毛×1", "arrow", ".,flint,.,.,stick,.,.,feather,.");
            G("盾牌", 1, "木板×6 + 铁锭×1", "shield", "oak_planks,iron_ingot,oak_planks,oak_planks,oak_planks,oak_planks,.,oak_planks,.");
            G("床", 1, "羊毛×3 + 木板×3", "white_bed", "white_wool,white_wool,white_wool,oak_planks,oak_planks,oak_planks,.,.,.");
            G("门", 3, "木板×6", "oak_door", "oak_planks,oak_planks,.,oak_planks,oak_planks,.,oak_planks,oak_planks,.");
            G("活板门", 2, "木板×6", "oak_trapdoor", "oak_planks,oak_planks,oak_planks,oak_planks,oak_planks,oak_planks,.,.,.");
            G("栅栏", 3, "木棍×4 + 木板×2", "oak_fence", "oak_planks,stick,oak_planks,oak_planks,stick,oak_planks,.,.,.");
            G("栅栏门", 1, "木棍×4 + 木板×2", "oak_fence_gate", "stick,oak_planks,stick,stick,oak_planks,stick,.,.,.");
            G("告示牌", 3, "木板×6 + 木棍×1", "oak_sign", "oak_planks,oak_planks,oak_planks,oak_planks,oak_planks,oak_planks,.,stick,.");
            G("画", 1, "木棍×8 + 羊毛×1", "painting", "stick,stick,stick,stick,white_wool,stick,stick,stick,stick");
            G("物品展示框", 1, "木棍×8 + 皮革×1", "item_frame", "stick,stick,stick,stick,leather,stick,stick,stick,stick");
            G("盔甲架", 1, "木棍×6 + 石台阶×1", "armor_stand", "stick,stick,stick,.,stick,.,.,stone_slab,.");

            // ---- 工具（木/石/铁/金/钻石）----
            ToolSet("木", "oak_planks", "wooden");
            ToolSet("石", "cobblestone", "stone");
            ToolSet("铁", "iron_ingot", "iron");
            ToolSet("金", "gold_ingot", "golden");
            ToolSet("钻石", "diamond", "diamond");
            G("下界合金镐", 1, "下界合金锭×1 + 钻石镐×1", "netherite_pickaxe", ".,.,.,.,netherite_ingot,.,.,diamond_pickaxe,.");

            // ---- 防具 ----
            ArmorSet("皮革", "leather", "leather");
            ArmorSet("铁", "iron_ingot", "iron");
            ArmorSet("钻石", "diamond", "diamond");

            // ---- 食物 ----
            G("面包", 1, "小麦×3", "bread", ".,.,.,wheat,wheat,wheat,.,.,.");
            G("蛋糕", 1, "小麦×3 + 糖×2 + 鸡蛋×1 + 牛奶桶×3", "cake", "milk_bucket,milk_bucket,milk_bucket,sugar,egg,sugar,wheat,wheat,wheat");
            G("曲奇", 8, "小麦×2 + 可可豆×1", "cookie", ".,.,.,wheat,cocoa_beans,wheat,.,.,.");
            G("南瓜派", 1, "南瓜×1 + 糖×1 + 鸡蛋×1", "pumpkin_pie", ".,.,.,pumpkin,sugar,egg,.,.,.");
            G("蘑菇煲", 1, "红蘑菇×1 + 棕蘑菇×1 + 碗×1", "mushroom_stew", ".,.,.,red_mushroom,brown_mushroom,.,.,bowl,.");
            S("烤土豆", 1, "土豆×1", "熔炉", "baked_potato");
            S("熟牛肉", 1, "生牛肉×1", "熔炉", "cooked_beef");
            S("熟猪排", 1, "生猪排×1", "熔炉", "cooked_porkchop");
            S("熟鸡肉", 1, "生鸡肉×1", "熔炉", "cooked_chicken");
            S("熟羊肉", 1, "生羊肉×1", "熔炉", "cooked_mutton");
            S("熟鳕鱼", 1, "生鳕鱼×1", "熔炉", "cooked_cod");
            G("干草捆", 1, "小麦×9", "hay_block", "wheat,wheat,wheat,wheat,wheat,wheat,wheat,wheat,wheat");

            // ---- 杂项 ----
            G("糖", 1, "甘蔗×1", "sugar", ".,.,.,.,sugar_cane,.,.,.,.");
            G("纸", 3, "甘蔗×3", "paper", ".,.,.,sugar_cane,sugar_cane,sugar_cane,.,.,.");
            G("书", 1, "纸×3 + 皮革×1", "book", ".,.,.,paper,paper,paper,.,leather,.");
            G("书架", 1, "木板×6 + 书×3", "bookshelf", "oak_planks,oak_planks,oak_planks,book,book,book,oak_planks,oak_planks,oak_planks");
            G("附魔台", 1, "黑曜石×4 + 钻石×2 + 书×1", "enchanting_table", ".,book,.,diamond,obsidian,diamond,obsidian,obsidian,obsidian");
            G("铁砧", 1, "铁块×3 + 铁锭×4", "anvil", "iron_block,iron_block,iron_block,.,iron_ingot,.,iron_ingot,iron_ingot,iron_ingot");
            G("酿造台", 1, "烈焰棒×1 + 圆石×3", "brewing_stand", ".,.,.,.,blaze_rod,.,cobblestone,cobblestone,cobblestone");
            G("炼药锅", 1, "铁锭×7", "cauldron", "iron_ingot,.,iron_ingot,iron_ingot,.,iron_ingot,iron_ingot,iron_ingot,iron_ingot");
            G("末影箱", 1, "黑曜石×8 + 末影之眼×1", "ender_chest", "obsidian,obsidian,obsidian,obsidian,ender_eye,obsidian,obsidian,obsidian,obsidian");
            G("信标", 1, "玻璃×5 + 下界之星×1 + 黑曜石×3", "beacon", "glass,glass,glass,glass,nether_star,glass,obsidian,obsidian,obsidian");
            G("铁栏杆", 16, "铁锭×6", "iron_bars", "iron_ingot,iron_ingot,iron_ingot,iron_ingot,iron_ingot,iron_ingot,.,.,.");
            G("玻璃板", 16, "玻璃×6", "glass_pane", "glass,glass,glass,glass,glass,glass,.,.,.");
            G("砖块", 1, "红砖×4", "bricks", "brick,brick,.,brick,brick,.,.,.,.");
            G("石砖", 4, "石头×4", "stone_bricks", "stone,stone,.,stone,stone,.,.,.,.");
            G("砂岩", 1, "沙子×4", "sandstone", "sand,sand,.,sand,sand,.,.,.,.");
            G("羊毛", 1, "线×4", "white_wool", "string,string,.,string,string,.,.,.,.");
            G("地毯", 3, "羊毛×2", "white_carpet", "white_wool,white_wool,.,.,.,.,.,.,.");
            G("营火", 1, "木棍×3 + 煤炭×1 + 原木×3", "campfire", ".,stick,.,stick,coal,stick,oak_log,oak_log,oak_log");
            G("灯笼", 1, "铁粒×8 + 火把×1", "lantern", "iron_nugget,iron_nugget,iron_nugget,iron_nugget,torch,iron_nugget,iron_nugget,iron_nugget,iron_nugget");
            G("灵魂灯笼", 1, "铁粒×8 + 灵魂火把×1", "soul_lantern", "iron_nugget,iron_nugget,iron_nugget,iron_nugget,soul_torch,iron_nugget,iron_nugget,iron_nugget,iron_nugget");
            G("烟熏炉", 1, "熔炉×1 + 原木×4", "smoker", ".,furnace,.,oak_log,oak_log,oak_log,.,oak_log,.");
            G("高炉", 1, "熔炉×1 + 铁锭×5 + 平滑石头×3", "blast_furnace", "iron_ingot,iron_ingot,iron_ingot,iron_ingot,furnace,iron_ingot,smooth_stone,smooth_stone,smooth_stone");
            GS("切石机", 1, "铁锭×1 + 石头×3", "切石机", "stonecutter", ".,iron_ingot,.,stone,stone,stone,.,.,.");
            G("制图台", 1, "木板×4 + 纸×2", "cartography_table", "oak_planks,oak_planks,.,oak_planks,oak_planks,.,.,paper,paper");
            G("制箭台", 1, "木板×4 + 燧石×2", "fletching_table", "oak_planks,oak_planks,.,oak_planks,oak_planks,.,.,flint,flint");
            G("锻造台", 1, "铁锭×2 + 木板×4", "smithing_table", "iron_ingot,iron_ingot,.,oak_planks,oak_planks,oak_planks,oak_planks,oak_planks,oak_planks");
            G("织布机", 1, "木板×2 + 线×2", "loom", "oak_planks,oak_planks,.,string,string,.,.,.,.");
            G("木桶", 1, "木板×6 + 木台阶×2", "barrel", "oak_planks,.,oak_planks,oak_planks,.,oak_planks,oak_planks,oak_slab,oak_planks");
            G("堆肥桶", 1, "木台阶×7", "composter", "oak_slab,.,oak_slab,oak_slab,.,oak_slab,oak_slab,oak_slab,oak_slab");
            G("指南针", 1, "铁锭×4 + 红石×1", "compass", ".,iron_ingot,.,iron_ingot,redstone,iron_ingot,.,iron_ingot,.");
            G("时钟", 1, "金锭×4 + 红石×1", "clock", ".,gold_ingot,.,gold_ingot,redstone,gold_ingot,.,gold_ingot,.");
            G("地图", 1, "纸×8 + 指南针×1", "map", "paper,paper,paper,paper,compass,paper,paper,paper,paper");
            G("望远镜", 1, "铜锭×2 + 紫水晶碎片×1", "spyglass", ".,.,.,.,copper_ingot,.,copper_ingot,amethyst_shard,.");

            // ---- 红石 ----
            G("红石火把", 1, "红石×1 + 木棍×1", "redstone_torch", ".,redstone,.,.,stick,.,.,.,.");
            G("中继器", 1, "石头×3 + 红石火把×2 + 红石×1", "repeater", "stone,stone,stone,redstone_torch,redstone,redstone_torch,stone,stone,stone");
            G("比较器", 1, "石头×3 + 红石火把×3 + 石英×1", "comparator", "stone,stone,stone,redstone_torch,quartz,redstone_torch,stone,redstone_torch,stone");
            G("活塞", 1, "木板×3 + 圆石×4 + 铁锭×1 + 红石×1", "piston", "oak_planks,oak_planks,oak_planks,cobblestone,iron_ingot,cobblestone,cobblestone,redstone,cobblestone");
            G("粘性活塞", 1, "活塞×1 + 黏液球×1", "sticky_piston", ".,.,.,.,slime_ball,.,.,piston,.");
            G("观察者", 1, "圆石×6 + 红石×2 + 石英×1", "observer", "cobblestone,cobblestone,cobblestone,redstone,quartz,redstone,cobblestone,cobblestone,cobblestone");
            G("漏斗", 1, "铁锭×5 + 箱子×1", "hopper", "iron_ingot,.,iron_ingot,iron_ingot,chest,iron_ingot,.,iron_ingot,.");
            G("投掷器", 1, "圆石×7 + 红石×1", "dropper", "cobblestone,cobblestone,cobblestone,cobblestone,redstone,cobblestone,cobblestone,cobblestone,cobblestone");
            G("发射器", 1, "圆石×7 + 红石×1 + 弓×1", "dispenser", "cobblestone,cobblestone,cobblestone,cobblestone,bow,cobblestone,cobblestone,cobblestone,cobblestone");
            G("矿车", 1, "铁锭×5", "minecart", "iron_ingot,.,iron_ingot,iron_ingot,iron_ingot,iron_ingot,.,.,.");
            G("铁轨", 16, "铁锭×6 + 木棍×1", "rail", "iron_ingot,.,iron_ingot,iron_ingot,stick,iron_ingot,iron_ingot,.,iron_ingot");
            G("动力铁轨", 6, "金锭×6 + 木棍×1 + 红石×1", "powered_rail", "gold_ingot,.,gold_ingot,gold_ingot,stick,gold_ingot,gold_ingot,redstone,gold_ingot");
            G("探测铁轨", 6, "铁锭×6 + 石压力板×1 + 红石×1", "detector_rail", "iron_ingot,.,iron_ingot,iron_ingot,stone_pressure_plate,iron_ingot,iron_ingot,redstone,iron_ingot");
            G("TNT", 1, "火药×5 + 沙子×4", "tnt", "gunpowder,sand,gunpowder,sand,gunpowder,sand,gunpowder,sand,gunpowder");
            G("拉杆", 1, "木棍×1 + 圆石×1", "lever", ".,.,.,.,stick,.,.,cobblestone,.");
            G("石按钮", 1, "石头×1", "stone_button", ".,.,.,.,stone,.,.,.,.");
            G("石压力板", 1, "石头×2", "stone_pressure_plate", ".,.,.,.,.,.,stone,stone,.");
            G("阳光探测器", 1, "玻璃×3 + 石英×3 + 木台阶×3", "daylight_detector", "glass,glass,glass,quartz,quartz,quartz,oak_slab,oak_slab,oak_slab");
            G("音符盒", 1, "木板×8 + 红石×1", "note_block", "oak_planks,oak_planks,oak_planks,oak_planks,redstone,oak_planks,oak_planks,oak_planks,oak_planks");
            G("唱片机", 1, "木板×8 + 钻石×1", "jukebox", "oak_planks,oak_planks,oak_planks,oak_planks,diamond,oak_planks,oak_planks,oak_planks,oak_planks");
            G("红石灯", 1, "红石×4 + 萤石×1", "redstone_lamp", ".,redstone,.,redstone,glowstone,redstone,.,redstone,.");
            G("标靶", 1, "红石×4 + 干草捆×1", "target", ".,redstone,.,redstone,hay_block,redstone,.,redstone,.");
            G("陷阱箱", 1, "箱子×1 + 绊线钩×1", "trapped_chest", ".,.,.,.,tripwire_hook,.,.,chest,.");

            // ---- 熔炼 ----
            S("木炭", 1, "原木×1", "熔炉", "charcoal");
            S("玻璃", 1, "沙子×1", "熔炉", "glass");
            S("石头", 1, "圆石×1", "熔炉", "stone");
            S("平滑石头", 1, "石头×1", "熔炉", "smooth_stone");
            S("铁锭", 1, "铁矿石×1", "熔炉", "iron_ingot");
            S("金锭", 1, "金矿石×1", "熔炉", "gold_ingot");
            S("铜锭", 1, "铜矿石×1", "熔炉", "copper_ingot");
            G("下界合金锭", 1, "下界合金碎片×4 + 金锭×4", "netherite_ingot", "netherite_scrap,netherite_scrap,netherite_scrap,netherite_scrap,gold_ingot,gold_ingot,gold_ingot,gold_ingot,.");
            S("红砖", 1, "黏土球×1", "熔炉", "brick");
            S("木炭（高炉）", 1, "原木×1", "高炉", "charcoal");
            S("熟牛肉（烟熏炉）", 1, "生牛肉×1", "烟熏炉", "cooked_beef");

            // ---- 切石机 ----
            S("石台阶", 2, "石头×1", "切石机", "stone_slab");
            S("石楼梯", 1, "石头×1", "切石机", "stone_stairs");
            S("石砖台阶", 2, "石砖×1", "切石机", "stone_brick_slab");
            S("砂岩台阶", 2, "砂岩×1", "切石机", "sandstone_slab");
        }

        // 查询：名称或材料命中都算；空查询返回全部
        public static List<Recipe> Search(string q)
        {
            var list = new List<Recipe>();
            if (string.IsNullOrEmpty(q))
            {
                list.AddRange(All);
                return list;
            }
            string k = q.Trim().ToLowerInvariant();
            for (int i = 0; i < All.Count; i++)
            {
                Recipe r = All[i];
                if (r.Name.ToLowerInvariant().Contains(k) || r.Mats.ToLowerInvariant().Contains(k))
                    list.Add(r);
            }
            return list;
        }
    }
}
