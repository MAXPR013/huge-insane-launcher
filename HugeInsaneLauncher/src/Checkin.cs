// HugeLauncher - 签到：真实记录每天是否打过卡（原来首页是写死的「连续 6 天」假数据）
using System;
using System.Collections.Generic;
using System.IO;

namespace HugeLauncher
{
    static class Checkin
    {
        static readonly HashSet<string> days = new HashSet<string>();
        static bool loaded;
        static string path;

        static string FilePath
        {
            get
            {
                if (path == null) path = Path.Combine(Paths.LauncherDir, "checkin.dat");
                return path;
            }
        }

        static string K(DateTime d) { return d.ToString("yyyy-MM-dd"); }

        public static void Load()
        {
            if (loaded) return;
            loaded = true;
            days.Clear();
            try
            {
                if (File.Exists(FilePath))
                    foreach (string ln in File.ReadAllLines(FilePath))
                    {
                        string s = ln.Trim();
                        if (s.Length == 10) days.Add(s);
                    }
            }
            catch { }
        }

        static void Save()
        {
            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (string s in days) sb.Append(s).Append('\n');
                File.WriteAllText(FilePath, sb.ToString());
            }
            catch { }
        }

        public static bool IsChecked(DateTime d)
        {
            Load();
            return days.Contains(K(d));
        }

        public static void MarkToday()
        {
            Load();
            if (days.Add(K(DateTime.Now))) Save();
        }

        // 连续天数：从今天往前数；今天还没打卡就从昨天开始数
        public static int Streak()
        {
            Load();
            DateTime d = DateTime.Now;
            if (!days.Contains(K(d))) d = d.AddDays(-1);
            int n = 0;
            while (days.Contains(K(d)))
            {
                n++;
                d = d.AddDays(-1);
            }
            return n;
        }

        public static int MonthCount()
        {
            Load();
            DateTime now = DateTime.Now;
            int n = 0;
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);
            for (int i = 1; i <= daysInMonth; i++)
                if (days.Contains(K(new DateTime(now.Year, now.Month, i)))) n++;
            return n;
        }
    }
}
