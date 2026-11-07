// HugeLauncher.Ui - 左侧导航栏目录：顺序 / 显隐由 config.ini 的 NavOrder、NavHidden 决定
// 设置页「导航」卡片和主窗口都读这里，保证两边一致。
using System;
using System.Collections.Generic;

namespace HugeLauncher.Ui
{
    static class NavCatalog
    {
        public class Item
        {
            public string Key;      // 稳定 id（存配置用）
            public string Name;     // 显示名
            public string Icon;     // SVG path
            public Item(string key, string name, string icon) { Key = key; Name = name; Icon = icon; }
        }

        public static readonly List<Item> All = new List<Item>
        {
            new Item("home", "首页",
                "M3.5 10.5 L12 3.5 L20.5 10.5 M6 8.8 L6 20 L18 20 L18 8.8 M10 20 L10 14 L14 14 L14 20"),
            new Item("download", "下载",
                "M12 3 L12 15 M7 10 L12 15 L17 10 M4 20 L20 20"),
            new Item("multi", "联机",
                "M8 11 A4 4 0 1 0 8 3 A4 4 0 1 0 8 11 M2 21 L2 19 A6 6 0 0 1 14 19 L14 21 M16 4 A4 4 0 0 1 16 12 M22 21 L22 19 A6 6 0 0 0 18 13"),
            new Item("account", "账号",
                "M12 12 A4 4 0 1 0 12 4 A4 4 0 1 0 12 12 M4 21 L4 19 A4 4 0 0 1 8 15 L16 15 A4 4 0 0 1 20 19 L20 21"),
            new Item("settings", "设置",
                "M12 2 L14.5 4 L17.5 3.5 L18.5 6.5 L21.5 7.5 L21 10.5 L22 12 L21 13.5 L21.5 16.5 L18.5 17.5 L17.5 20.5 L14.5 20 L12 22 L9.5 20 L6.5 20.5 L5.5 17.5 L2.5 16.5 L3 13.5 L2 12 L3 10.5 L2.5 7.5 L5.5 6.5 L6.5 3.5 L9.5 4 Z M12 8 A4 4 0 1 0 12 16 A4 4 0 1 0 12 8"),
            new Item("more", "更多",
                "M6 11 L6 13 M12 11 L12 13 M18 11 L18 13"),
        };

        public static Item Find(string key)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].Key == key) return All[i];
            return null;
        }

        // 存配置的两串：order = 顺序键列表，hidden = 被删掉的键列表
        public static List<string> OrderFrom(Config cfg)
        {
            var list = new List<string>();
            if (cfg != null && !string.IsNullOrEmpty(cfg.NavOrder))
            {
                string[] parts = cfg.NavOrder.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    string k = parts[i].Trim();
                    if (k.Length == 0 || Find(k) == null) continue;
                    if (!list.Contains(k)) list.Add(k);
                }
            }
            // 配置里没有的项（新增项 / 老配置缺项）：按目录顺序插到「它前一个目录项」后面，
            // 而不是一律丢到末尾——否则被隐藏后又恢复的项会跑到导航最下面。
            for (int i = 0; i < All.Count; i++)
            {
                string k = All[i].Key;
                if (list.Contains(k)) continue;
                int at = 0;
                for (int j = i - 1; j >= 0; j--)
                {
                    int idx = list.IndexOf(All[j].Key);
                    if (idx >= 0) { at = idx + 1; break; }
                }
                list.Insert(at, k);
            }
            return list;
        }

        public static List<string> HiddenFrom(Config cfg)
        {
            var list = new List<string>();
            if (cfg == null || string.IsNullOrEmpty(cfg.NavHidden)) return list;
            string[] parts = cfg.NavHidden.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string k = parts[i].Trim();
                if (k.Length > 0 && Find(k) != null && !list.Contains(k)) list.Add(k);
            }
            return list;
        }

        // 真正显示在导航栏里的项（顺序已排好、已去掉隐藏项；至少保留一项）
        public static List<Item> Visible(Config cfg)
        {
            var order = OrderFrom(cfg);
            var hidden = HiddenFrom(cfg);
            var list = new List<Item>();
            for (int i = 0; i < order.Count; i++)
            {
                if (hidden.Contains(order[i])) continue;
                Item it = Find(order[i]);
                if (it != null) list.Add(it);
            }
            if (list.Count == 0 && All.Count > 0) list.Add(All[0]);
            return list;
        }

        public static void Save(Config cfg, List<string> order, List<string> hidden)
        {
            if (cfg == null) return;
            cfg.NavOrder = string.Join(",", order.ToArray());
            cfg.NavHidden = string.Join(",", hidden.ToArray());
            cfg.Save();
        }
    }
}
