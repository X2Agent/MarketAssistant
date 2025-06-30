using MarketAssistant.Services;
using Window = Microsoft.Maui.Controls.Window;
using System.Runtime.InteropServices;

namespace MarketAssistant.WinUI.Services
{
    /// <summary>
    /// Windows平台窗口管理服务实现
    /// </summary>
    public class WindowManagementService : IWindowManagementService
    {
        /// <summary>
        /// 隐藏窗口
        /// </summary>
        /// <param name="window">要隐藏的窗口</param>
        public void HideWindow(Window window)
        {
            // Windows平台特定的隐藏逻辑
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUIWindow)
            {
                // 确保在UI线程上执行
                if (winUIWindow.DispatcherQueue.HasThreadAccess)
                {
                    SafeHideWindow(winUIWindow);
                }
                else
                {
                    winUIWindow.DispatcherQueue.TryEnqueue(() => SafeHideWindow(winUIWindow));
                }
            }
        }

        /// <summary>
        /// 安全地隐藏窗口
        /// </summary>
        /// <param name="winUIWindow">WinUI窗口实例</param>
        private void SafeHideWindow(Microsoft.UI.Xaml.Window winUIWindow)
        {
            try
            {
                // 检查AppWindow是否可用
                if (winUIWindow.AppWindow != null)
                {
                    winUIWindow.AppWindow.Hide();
                }
                else
                {
                    // 降级方案：最小化窗口
                    if (winUIWindow.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                    {
                        presenter.Minimize();
                    }
                }
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x8001010E))
            {
                // RPC_E_WRONG_THREAD - 尝试降级方案
                try
                {
                    // 尝试最小化窗口作为降级方案
                    if (winUIWindow.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                    {
                        presenter.Minimize();
                    }
                }
                catch
                {
                    // 如果所有方法都失败，记录错误但不抛出异常
                    System.Diagnostics.Debug.WriteLine($"无法隐藏窗口: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 显示并激活窗口
        /// </summary>
        /// <param name="window">要显示的窗口</param>
        public void ShowAndActivateWindow(Window window)
        {
            // Windows平台特定的显示逻辑
            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUIWindow)
            {
                // 确保在UI线程上执行
                if (winUIWindow.DispatcherQueue.HasThreadAccess)
                {
                    SafeShowWindow(winUIWindow);
                }
                else
                {
                    winUIWindow.DispatcherQueue.TryEnqueue(() => SafeShowWindow(winUIWindow));
                }
            }
        }

        /// <summary>
        /// 安全地显示窗口
        /// </summary>
        /// <param name="winUIWindow">WinUI窗口实例</param>
        private void SafeShowWindow(Microsoft.UI.Xaml.Window winUIWindow)
        {
            try
            {
                if (winUIWindow.AppWindow != null)
                {
                    winUIWindow.AppWindow.Show();
                    winUIWindow.Activate();
                }
                else
                {
                    // 降级方案：直接激活窗口
                    winUIWindow.Activate();
                }
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x8001010E))
            {
                // RPC_E_WRONG_THREAD - 尝试降级方案
                try
                {
                    // 尝试直接激活窗口
                    winUIWindow.Activate();
                }
                catch
                {
                    // 如果所有方法都失败，记录错误但不抛出异常
                    System.Diagnostics.Debug.WriteLine($"无法显示窗口: {ex.Message}");
                }
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