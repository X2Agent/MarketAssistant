using MarketAssistant.Services;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.WinUI;

internal static class WindowCloseHandler
{
    private static ILogger? _logger;
    private static IApplicationExitService? _applicationExitService;

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

            if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUIWindow)
            {
                // 订阅窗口即将关闭事件（可以阻止关闭）
                winUIWindow.AppWindow.Closing += OnWindowClosing;

                _logger?.LogDebug("Windows窗口关闭处理器已设置");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "设置Windows窗口关闭处理时出错");
        }
    }

    /// <summary>
    /// 窗口即将关闭事件处理
    /// </summary>
    private static void OnWindowClosing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args)
    {
        try
        {
            if (_applicationExitService != null)
            {
                // 通过AppWindow查找对应的MAUI窗口
                var mauiWindow = FindMauiWindowByAppWindow(sender);
                if (mauiWindow != null)
                {
                    // 先阻止关闭，然后异步处理用户选择
                    args.Cancel = true;
                    _logger?.LogDebug("窗口关闭被暂时阻止，等待用户选择");

                    // 在后台线程处理用户选择
                     _ = Task.Run(async () =>
                     {
                         try
                         {
                             var shouldClose = await _applicationExitService.HandleWindowCloseRequestAsync(mauiWindow);
                             if (shouldClose)
                             {
                                 // 如果用户选择关闭，在主线程上关闭窗口
                                 Application.Current?.Dispatcher?.Dispatch(() =>
                                 {
                                     try
                                     {
                                         if (mauiWindow.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUIWindow)
                                         {
                                             winUIWindow.Close();
                                         }
                                     }
                                     catch (Exception ex)
                                     {
                                         _logger?.LogError(ex, "关闭窗口时出错");
                                     }
                                 });
                             }
                         }
                         catch (Exception ex)
                         {
                             _logger?.LogError(ex, "异步处理窗口关闭请求时出错");
                         }
                     });
                }
                else
                {
                    _logger?.LogWarning("未找到对应的MAUI窗口，允许关闭");
                }
            }
            else
            {
                _logger?.LogWarning("ApplicationExitService未初始化，允许关闭");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理Windows窗口关闭事件时出错");
        }
    }

    /// <summary>
    /// 通过AppWindow查找对应的MAUI窗口
    /// </summary>
    /// <param name="appWindow">AppWindow实例</param>
    /// <returns>MAUI窗口</returns>
    private static Window? FindMauiWindowByAppWindow(Microsoft.UI.Windowing.AppWindow appWindow)
    {
        try
        {
            var app = Application.Current;
            if (app?.Windows != null)
            {
                foreach (var window in app.Windows)
                {
                    if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window winUIWindow &&
                        winUIWindow.AppWindow == appWindow)
                    {
                        return window;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "通过AppWindow查找MAUI窗口时出错");
        }

        return null;
    }
}
