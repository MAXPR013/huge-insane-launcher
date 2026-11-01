// HugeLauncher - 联机大厅：内置公共服务器名录（静态数据，直接编译进 exe）
using System.Collections.Generic;

namespace HugeLauncher
{
    // 一条服务器名录
    class ServerEntry
    {
        public string Address;     // 连接地址（SLP 查询目标）
        public string DisplayName; // 卡片显示名
        public string Intro;       // 我们自己写的中文一句话介绍
        public string Category;    // 所属分类（ServerCatalog.CatXxx 之一）
        public bool Pinned;        // 置顶卡（不参与分类切换与搜索过滤）
        public string Ver;         // 最新支持版本（2026-08-15 实况 SLP 抓包烤入；null = 未抓到，由实况探测补位显示）
    }

    static class ServerCatalog
    {
        // 五个分类（按钮顺序即此顺序；默认选中第一个）
        public const string CatCn = "国内低延迟";
        public const string CatMini = "综合小游戏";
        public const string CatSmp = "生存SMP";
        public const string CatRpg = "RPG魔改";
        public const string CatHard = "无政府硬核";
        public static readonly string[] Categories = new[] { CatCn, CatMini, CatSmp, CatRpg, CatHard };

        public static readonly ServerEntry[] All = new[]
        {
            // ===== 国内低延迟 =====
            new ServerEntry { Address = "mczero.top", DisplayName = "mczero.top", Intro = "农夫乐事·技能·公会·领地·宠物，优质生存服", Category = CatCn, Pinned = true },
            new ServerEntry { Address = "3c3u.org", DisplayName = "3C3U 无政府", Intro = "国内最火中文无政府服，无拘无束弱肉强食", Category = CatCn },
            new ServerEntry { Address = "2b2t.xin", DisplayName = "2B2T.XIN", Intro = "中文无政府老牌，已升级 1.21.11", Category = CatCn, Ver = "26.1.x" },
            new ServerEntry { Address = "mc.iecraft.com", DisplayName = "御灵游", Intro = "七年老牌 RPG：RPG+粘液+生存+建筑，永不删档", Category = CatCn },
            new ServerEntry { Address = "2b2t.cc", DisplayName = "2B2T.ZONE", Intro = "中文无政府新服，1.21.4", Category = CatCn },
            new ServerEntry { Address = "3b3t.cn", DisplayName = "3B3T", Intro = "国产无政府，高版本 Leaf 核心", Category = CatCn, Ver = "26.2" },
            new ServerEntry { Address = "2b2t.ink", DisplayName = "2B2T.ink", Intro = "中文无政府镜像站", Category = CatCn },
            new ServerEntry { Address = "mc.yihaifenglan.com", DisplayName = "怡海风岚", Intro = "经典纯净生存群组：生存/海岛/城镇", Category = CatCn, Ver = "1.18.2" },
            new ServerEntry { Address = "demo.sjmc.club", DisplayName = "方块交大展示服", Intro = "上海交大 MC 社展示服，参观校园建筑", Category = CatCn, Ver = "1.20.4" },

            // ===== 综合小游戏 =====
            new ServerEntry { Address = "mp.mc-complex.com", DisplayName = "Complex Gaming", Intro = "6000+ 在线巨型网络：宝可梦/派系/空岛/生存", Category = CatMini, Ver = "26.2" },
            new ServerEntry { Address = "play.talonmc.net", DisplayName = "TalonMC", Intro = "监狱与空岛老牌强服，Cobblemon 新上线", Category = CatMini, Ver = "1.21.10" },
            new ServerEntry { Address = "play.pika-network.net", DisplayName = "PikaNetwork", Intro = "离线圈顶流：空岛/起床/派系/练习", Category = CatMini },
            new ServerEntry { Address = "play.jartexnetwork.com", DisplayName = "JartexNetwork", Intro = "老牌离线网络：起床/空岛/派系/监狱", Category = CatMini, Ver = "26.2" },
            new ServerEntry { Address = "alwination.id", DisplayName = "AlwiNation", Intro = "印尼最大服：地球/原版/大亨", Category = CatMini, Ver = "26.2" },
            new ServerEntry { Address = "play.purpleprison.xyz", DisplayName = "PurplePrison", Intro = "2014 年至今的 OP Prison 监狱老服", Category = CatMini, Ver = "1.21.x" },
            new ServerEntry { Address = "play.blocksmc.com", DisplayName = "BlocksMC", Intro = "中东最大网络，新游 Pillars of Fortune", Category = CatMini, Ver = "26.x" },
            new ServerEntry { Address = "minesive.com", DisplayName = "Minesive", Intro = "现代化 Practice 竞技场", Category = CatMini, Ver = "26.2" },
            new ServerEntry { Address = "top.hylexmc.net", DisplayName = "HylexMC", Intro = "巴西离线网络：SkyMines，无 P2W", Category = CatMini, Ver = "26.2" },
            new ServerEntry { Address = "mc.gamster.org", DisplayName = "Gamster", Intro = "罗马尼亚老牌：SpeedBuilders/起床/空岛", Category = CatMini, Ver = "26.2" },
            new ServerEntry { Address = "top.chickencraft.nl", DisplayName = "ChickenCraft", Intro = "BoxPvP/水晶PvP/LifeSteal 竞技场", Category = CatMini, Ver = "26.2" },
            new ServerEntry { Address = "play.boxpvp.net", DisplayName = "BoxPvP Network", Intro = "水晶PvP/鞘翅PvP/随机装备", Category = CatMini, Ver = "26.2" },
            new ServerEntry { Address = "play.krypticmc.net", DisplayName = "KrypticMC", Intro = "小游戏/KitPvP/LifeSteal 综合网络", Category = CatMini },
            new ServerEntry { Address = "bedwarspractice.club", DisplayName = "Bedwars Practice", Intro = "起床战争专项练习：守床/搭桥/对刀", Category = CatMini, Ver = "1.21.x" },
            new ServerEntry { Address = "mc.mineland.net", DisplayName = "Mineland", Intro = "欧洲老牌：起床/生存/空岛/创造", Category = CatMini, Ver = "26.x" },

            // ===== 生存SMP =====
            new ServerEntry { Address = "go.minepeak.org", DisplayName = "MinePeak", Intro = "生存/LifeSteal/起床/决斗综合大服", Category = CatSmp, Ver = "1.20.x" },
            new ServerEntry { Address = "play.applemc.fun", DisplayName = "AppleMC", Intro = "赛季制 SkyBlock 大服", Category = CatSmp, Ver = "26.2" },
            new ServerEntry { Address = "play.opblocks.com", DisplayName = "OPBlocks", Intro = "高质量监狱/生存/空岛/宝可梦", Category = CatSmp, Ver = "26.2" },
            new ServerEntry { Address = "mp.lifestealsmp.com", DisplayName = "LifeSteal SMP", Intro = "LifeSteal 玩法鼻祖服，S11 赛季", Category = CatSmp },
            new ServerEntry { Address = "mp.loverfella.com", DisplayName = "LoverCraft", Intro = "生存 S9 赛季火热进行中", Category = CatSmp, Ver = "26.2" },
            new ServerEntry { Address = "play.minemalia.org", DisplayName = "MineMalia", Intro = "无卡顿定制生存/监狱/空岛", Category = CatSmp, Ver = "26.2" },
            new ServerEntry { Address = "ultimismc.com", DisplayName = "UltimisMC", Intro = "LifeSteal 新赛季刚开", Category = CatSmp },
            new ServerEntry { Address = "play.strongcraft.org", DisplayName = "StrongCraft", Intro = "LifeSteal/PvP/BoxPvP 新经济", Category = CatSmp, Ver = "26.2" },
            new ServerEntry { Address = "zasmp.net", DisplayName = "zaSMP", Intro = "硬核生存新 META", Category = CatSmp },
            new ServerEntry { Address = "mp.vulengate.com", DisplayName = "Vulengate", Intro = "社区驱动生存，禁止抄家，死亡不掉落", Category = CatSmp },
            new ServerEntry { Address = "play.faderealms.net", DisplayName = "FadeRealms", Intro = "零延迟网络，周更内容", Category = CatSmp, Ver = "26.2" },
            new ServerEntry { Address = "mp.twenture.net", DisplayName = "Twenture", Intro = "生存/地球城镇/LifeSteal/单方块", Category = CatSmp, Ver = "26.2" },

            // ===== RPG魔改 =====
            new ServerEntry { Address = "play.minepiece.net", DisplayName = "MinePiece", Intro = "海贼王 MMORPG：免模组，果实能力/航海冒险", Category = CatRpg, Ver = "1.21.11" },
            // 御灵游双分类挂载（同时在国内低延迟），有意重复
            new ServerEntry { Address = "mc.iecraft.com", DisplayName = "御灵游", Intro = "七年老牌 RPG：RPG+粘液+生存+建筑，永不删档", Category = CatRpg },

            // ===== 无政府硬核 =====
            new ServerEntry { Address = "mc.mineberry.org", DisplayName = "MineBerry", Intro = "万人容量：生存/无政府/起床/空岛", Category = CatHard, Ver = "1.20.x" },
            // 3C3U / 2B2T.XIN 双分类挂载（同时在国内低延迟），有意重复
            new ServerEntry { Address = "3c3u.org", DisplayName = "3C3U 无政府", Intro = "国内最火中文无政府服，无拘无束弱肉强食", Category = CatHard },
            new ServerEntry { Address = "play.extremecraft.net", DisplayName = "ExtremeCraft", Intro = "20 种模式综合网络，含纯无政府", Category = CatHard, Ver = "26.2" },
            new ServerEntry { Address = "6b6t.org", DisplayName = "6b6t", Intro = "自称全球最大无政府：/tpa /home，始于2022", Category = CatHard },
            new ServerEntry { Address = "2b2t.xin", DisplayName = "2B2T.XIN", Intro = "中文无政府老牌，已升级 1.21.11", Category = CatHard, Ver = "26.1.x" },
            new ServerEntry { Address = "9b9t.com", DisplayName = "9b9t", Intro = "老牌国际无政府", Category = CatHard },
            new ServerEntry { Address = "0b0t.org", DisplayName = "0b0t", Intro = "1.12.2 无政府活化石", Category = CatHard },
            new ServerEntry { Address = "5b5t.org", DisplayName = "5B5T", Intro = "无政府生存", Category = CatHard },
            new ServerEntry { Address = "8b8t.me", DisplayName = "8b8t", Intro = "2016 年至今的无政府生存", Category = CatHard },
        };

        // 置顶卡条目（Pinned=true 的那条）
        public static ServerEntry Pinned
        {
            get
            {
                foreach (var e in All) if (e.Pinned) return e;
                return All[0];
            }
        }

        public static List<ServerEntry> ByCategory(string cat)
        {
            var list = new List<ServerEntry>();
            foreach (var e in All) if (e.Category == cat) list.Add(e);
            return list;
        }
    }
}
