// HugeLauncher.Ai - UI 元素地图：剧本 LookAt 的目标注册表
// key 约定：tab:下载 / sub:模组 等导航项由主窗口注册；页面控件由页面加载时注册
// （如 res:search:mod / install:confirm / setup:memory / server:action）。
// 列表项等动态目标不进表，由页面暴露 FindXxx 方法，剧本 resolver 里直接调（滚动在方法内完成）。
using System;
using System.Collections.Generic;
using System.Windows;

namespace HugeLauncher.Ai
{
    static class UiMap
    {
        static readonly Dictionary<string, Func<FrameworkElement>> map =
            new Dictionary<string, Func<FrameworkElement>>();

        public static void Register(string key, Func<FrameworkElement> resolver)
        {
            lock (map) map[key] = resolver;
        }

        public static FrameworkElement Find(string key)
        {
            Func<FrameworkElement> r;
            lock (map)
            {
                if (!map.TryGetValue(key, out r) || r == null) return null;
            }
            try { return r(); } catch { return null; }
        }
    }
}
