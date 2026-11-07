// HugeLauncher - 游玩统计（纯本地）：按天记启动次数，供首页/找乐子的小部件读
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace HugeLauncher
{
    static class Stats
    {
        static readonly Dictionary<string, int> days = new Dictionary<string, int>();
        static bool loaded;
        static string filePath;

        static string FilePath
        {
            get
            {
                if (filePath == null) filePath = Path.Combine(Paths.LauncherDir, "stats.dat");
                return filePath;
            }
        }

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            days.Clear();
            try
            {
                if (File.Exists(FilePath))
                {
                    string[] lines = File.ReadAllLines(FilePath);
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string[] p = lines[i].Split('|');
                        if (p.Length != 2) continue;
                        int n;
                        if (int.TryParse(p[1], out n)) days[p[0]] = n;
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (KeyValuePair<string, int> kv in days)
                    sb.Append(kv.Key).Append('|').Append(kv.Value).Append('\n');
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }

        static string K(DateTime t) { return t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }

        public static void OnLaunch(string ver)
        {
            Load();
            string k = K(DateTime.Now);
            int n;
            days.TryGetValue(k, out n);
            days[k] = n + 1;
            Save();
        }

        public static int Today()
        {
            Load();
            int n;
            days.TryGetValue(K(DateTime.Now), out n);
            return n;
        }

        public static int ThisWeek()
        {
            Load();
            int total = 0;
            DateTime t = DateTime.Now;
            for (int i = 0; i < 7; i++) total += CountOn(t.AddDays(-i));
            return total;
        }

        public static int Total()
        {
            Load();
            int total = 0;
            foreach (KeyValuePair<string, int> kv in days) total += kv.Value;
            return total;
        }

        public static int CountOn(DateTime t)
        {
            Load();
            int n;
            days.TryGetValue(K(t), out n);
            return n;
        }

        // 最近 7 天（旧→新）
        public static int[] Last7()
        {
            Load();
            var arr = new int[7];
            DateTime t = DateTime.Now;
            for (int i = 0; i < 7; i++) arr[6 - i] = CountOn(t.AddDays(-i));
            return arr;
        }

        // 清空本周（近 7 天）统计，从今天重新计数；更早的历史保留
        public static void ClearWeek()
        {
            Load();
            DateTime t = DateTime.Now;
            for (int i = 0; i < 7; i++) days.Remove(K(t.AddDays(-i)));
            Save();
        }
    }
}
