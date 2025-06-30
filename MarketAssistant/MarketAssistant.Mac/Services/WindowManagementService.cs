using MarketAssistant.Services;
using UIKit;

namespace MarketAssistant.Mac.Services
{
    /// <summary>
    /// Mac平台窗口管理服务实现
    /// </summary>
    public class WindowManagementService : IWindowManagementService
    {
        /// <summary>
        /// 隐藏窗口
        /// </summary>
        /// <param name="window">要隐藏的窗口</param>
        public void HideWindow(Window window)
        {
            // Mac平台特定的隐藏逻辑
            if (window.Handler?.PlatformView is UIWindow uiWindow)
            {
                uiWindow.Hidden = true;
            }
        }

        /// <summary>
        /// 显示并激活窗口
        /// </summary>
        /// <param name="window">要显示的窗口</param>
        public void ShowAndActivateWindow(Window window)
        {
            // Mac平台特定的显示逻辑
            if (window.Handler?.PlatformView is UIWindow uiWindow)
            {
                uiWindow.Hidden = false;
                uiWindow.MakeKeyAndVisible();
            }
        }

        /// <summary>
        /// 设置窗口关闭处理
        /// </summary>
        /// <param name="window">窗口</param>
        /// <param name="serviceProvider">服务提供者</param>
        public void SetupWindowCloseHandling(Window window, IServiceProvider serviceProvider)
        {
            WindowCloseHandler.SetupWindowCloseHandling(window, serviceProvider);
        }
    }
}