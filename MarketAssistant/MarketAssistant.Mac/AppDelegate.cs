using Foundation;
using MarketAssistant.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using UIKit;
using AppKit;

namespace MarketAssistant.Mac
{
    [Register(nameof(AppDelegate))]
    public class AppDelegate : MauiUIApplicationDelegate
    {
        private ILogger<AppDelegate>? _logger;
        private IApplicationExitService? _applicationExitService;
        private Window? _mainWindow;

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

        public override bool FinishedLaunching(UIApplication application, NSDictionary launchOptions)
        {
            var result = base.FinishedLaunching(application, launchOptions);

            // 获取服务
            var serviceProvider = IPlatformApplication.Current?.Services;
            if (serviceProvider != null)
            {
                _logger = serviceProvider.GetService<ILogger<AppDelegate>>();
                _applicationExitService = serviceProvider.GetService<IApplicationExitService>();
            }

            _logger?.LogDebug("Mac AppDelegate 初始化完成");

            return result;
        }

        /// <summary>
        /// 设置主窗口引用
        /// </summary>
        /// <param name="window">主窗口</param>
        public void SetMainWindow(Window window)
        {
            _mainWindow = window;
            _logger?.LogDebug("主窗口已设置到AppDelegate");
        }

        /// <summary>
        /// 获取应用程序退出服务
        /// </summary>
        /// <returns>应用程序退出服务</returns>
        public IApplicationExitService? GetApplicationExitService()
        {
            return _applicationExitService;
        }

        /// <summary>
        /// 获取主窗口
        /// </summary>
        /// <returns>主窗口</returns>
        public Window? GetMainWindow()
        {
            return _mainWindow;
        }
    }
}
