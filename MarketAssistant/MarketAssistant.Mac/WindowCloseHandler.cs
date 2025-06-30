using MarketAssistant.Services;
using Microsoft.Extensions.Logging;
using UIKit;

namespace MarketAssistant.Mac;

/// <summary>
/// Mac平台窗口关闭处理器
/// </summary>
internal static class WindowCloseHandler
{
    private static ILogger? _logger;
    private static IApplicationExitService? _applicationExitService;
    private static Window? _mainWindow;

    /// <summary>
    /// 设置窗口关闭处理
    /// </summary>
    /// <param name="window">MAUI窗口</param>
    /// <param name="serviceProvider">服务提供者</param>
    public static void SetupWindowCloseHandling(Window window, IServiceProvider serviceProvider)
    {
        try
        {
            _logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger("WindowCloseHandler");
            _applicationExitService = serviceProvider.GetService<IApplicationExitService>();
            _mainWindow = window;

            // 确保AppDelegate已正确设置
            if (UIApplication.SharedApplication.Delegate is AppDelegate appDelegate)
            {
                appDelegate.SetMainWindow(window);
                _logger?.LogDebug("Mac窗口关闭处理器已设置，主窗口已注册到AppDelegate");
            }
            else
            {
                _logger?.LogWarning("无法获取AppDelegate实例，窗口关闭处理可能无法正常工作");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "设置Mac窗口关闭处理时出错");
        }
    }

    /// <summary>
    /// 处理应用程序即将终止事件
    /// </summary>
    /// <param name="window">MAUI窗口</param>
    /// <returns>是否允许终止</returns>
    public static async Task<bool> HandleApplicationWillTerminate(Window window)
    {
        try
        {
            if (_applicationExitService != null)
            {
                _logger?.LogDebug("处理Mac应用程序终止请求");
                return await _applicationExitService.HandleWindowCloseRequestAsync(window);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理Mac应用程序终止事件时出错");
        }

        return true; // 默认允许终止
    }

    /// <summary>
    /// 处理窗口即将关闭事件
    /// </summary>
    /// <param name="window">MAUI窗口</param>
    /// <returns>是否允许关闭</returns>
    public static async Task<bool> HandleWindowWillClose(Window window)
    {
        try
        {
            if (_applicationExitService != null)
            {
                _logger?.LogDebug("处理Mac窗口关闭请求");
                return await _applicationExitService.HandleWindowCloseRequestAsync(window);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理Mac窗口关闭事件时出错");
        }

        return true; // 默认允许关闭
    }

    /// <summary>
    /// 获取当前主窗口
    /// </summary>
    /// <returns>主窗口实例</returns>
    public static Window? GetMainWindow()
    {
        return _mainWindow;
    }

    /// <summary>
    /// 获取应用程序退出服务
    /// </summary>
    /// <returns>应用程序退出服务实例</returns>
    public static IApplicationExitService? GetApplicationExitService()
    {
        return _applicationExitService;
    }
}