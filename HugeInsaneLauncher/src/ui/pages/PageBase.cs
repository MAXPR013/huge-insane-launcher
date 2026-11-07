// HugeLauncher.Ui - 页面集成契约：页面基类 + 导航器（所有页面子代理必须遵守，禁止修改本文件）
using System;
using System.Windows;
using System.Windows.Controls;

namespace HugeLauncher.Ui.Pages
{
    // 所有右侧内容页的基类。主窗口把页面放进内容区，切换时调用 OnEnter/OnLeave。
    class PageBase : Grid
    {
        public Config Cfg;
        public object NavParam;   // Navigator.Go(key, param) 的 param，主窗口在 OnEnter 前注入

        // 页面被切到前台时调用（在此刷新数据/启动后台加载；先读 NavParam）
        public virtual void OnEnter() { }
        public virtual void OnLeave() { }

        // toast 提示（主窗口实现后自动可用；未实现时静默）
        public static Action<string> ToastHandler;
        public static void Toast(string msg) { var h = ToastHandler; if (h != null) h(msg); }

        // 请求主窗口启动指定版本（主窗口实现；未实现时静默）
        public static Action<string> LaunchHandler;
        public static void RequestLaunch(string versionId) { var h = LaunchHandler; if (h != null) h(versionId); }

        // 版本选择变更通知（主窗口刷新侧栏「当前版本」；未实现时静默）
        public static Action VersionSelectHandler;
        public static void NotifyVersionSelected() { var h = VersionSelectHandler; if (h != null) h(); }

        // 重新查看新手引导（主窗口实现）
        // 导航布局被改过：主窗口重建成新的导航栏
        public static Action NavRefreshHandler;
        public static void RequestNavRefresh() { var h = NavRefreshHandler; if (h != null) h(); }
        public static Action RestartOnboardHandler;
        public static void RequestRestartOnboard() { var h = RestartOnboardHandler; if (h != null) h(); }

        // 窗口级模态：内容挂到主窗口最上层，背景按需模糊
        public static Action<UIElement, bool, Action> ModalShowHandler;
        public static Action ModalHideHandler;
        public static void ShowModal(UIElement content, bool blurEverything, Action onClosed)
        {
            var h = ModalShowHandler;
            if (h != null) h(content, blurEverything, onClosed);
        }
        public static void HideModal()
        {
            var h = ModalHideHandler;
            if (h != null) h();
        }
    }

    // 页面导航。页面内跳转一律调 Navigator.Go，禁止直接 new 别的页面。
    static class Navigator
    {
        // 主窗口注册处理器；参数为目标页面 key 与可选参数
        public static Action<string, object> Handler;
        public static void Go(string pageKey) { Go(pageKey, null); }
        public static void Go(string pageKey, object param)
        {
            var h = Handler;
            if (h != null) h(pageKey, param);
        }
    }
}
