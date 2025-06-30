using MarketAssistant.Infrastructure;
using MarketAssistant.Services;

namespace MarketAssistant
{
    public partial class App : Application
    {
        private IApplicationExitService? _applicationExitService;

        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            var window = new Window(new AppShell());
            var width = DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density;
            var height = DeviceDisplay.Current.MainDisplayInfo.Height / DeviceDisplay.Current.MainDisplayInfo.Density;
            //window.Width = width;
            //window.Height = height;

            // 获取退出管理服务
            _applicationExitService = Handler?.MauiContext?.Services?.GetService<IApplicationExitService>();

            // 订阅Window生命周期事件来处理资源清理
            window.Stopped += OnWindowStopped;
            window.Destroying += OnWindowDestroying;

            // 订阅窗口关闭事件
            if (_applicationExitService != null)
            {
                window.Created += (sender, e) =>
                {
                    // 在窗口创建后设置关闭处理逻辑
                    SetupWindowCloseHandling(window);
                };
            }

            return window;
        }

        private async void OnWindowStopped(object? sender, EventArgs e)
        {
            await CleanupResourcesAsync();
        }

        private async void OnWindowDestroying(object? sender, EventArgs e)
        {
            await CleanupResourcesAsync();
        }

        /// <summary>
        /// 设置窗口关闭处理逻辑
        /// </summary>
        /// <param name="window">窗口实例</param>
        private void SetupWindowCloseHandling(Window window)
        {
            try
            {
                // 注意：MAUI的Destroying事件无法阻止窗口关闭
                // 实际的关闭拦截由平台特定的WindowManagementService处理
                if (Handler?.MauiContext?.Services != null)
                {
                    var windowManagementService = Handler.MauiContext.Services.GetService<IWindowManagementService>();
                    windowManagementService?.SetupWindowCloseHandling(window, Handler.MauiContext.Services);
                }
            }
            catch (Exception ex)
            {
                // 记录错误但不影响窗口创建
                System.Diagnostics.Debug.WriteLine($"设置窗口关闭处理时出错: {ex.Message}");
            }
        }

        private async Task CleanupResourcesAsync()
        {
            // 清理资源，特别是Playwright相关资源
            var serviceProvider = Handler?.MauiContext?.Services;
            if (serviceProvider != null)
            {
                try
                {
                    var playwrightService = serviceProvider.GetService<Infrastructure.PlaywrightService>();
                    if (playwrightService != null)
                    {
                        await playwrightService.DisposeAsync();
                    }

                    // 清理全局异常处理器
                    GlobalExceptionHandler.Cleanup();
                }
                catch (Exception ex)
                {
                    // 记录清理过程中的异常，但不抛出
                    System.Diagnostics.Debug.WriteLine($"Error during cleanup: {ex.Message}");
                }
            }
        }

        protected override void OnStart()
        {
            base.OnStart();

            // 初始化全局异常处理
            var serviceProvider = Handler?.MauiContext?.Services;
            if (serviceProvider != null)
            {
                GlobalExceptionHandler.Initialize(serviceProvider);
            }
        }
    }
}
