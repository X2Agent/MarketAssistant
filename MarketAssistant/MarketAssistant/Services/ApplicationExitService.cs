using MarketAssistant.Applications.Settings;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services
{
    /// <summary>
    /// 应用程序退出管理服务实现
    /// </summary>
    public class ApplicationExitService : IApplicationExitService
    {
        private readonly ILogger<ApplicationExitService> _logger;
        private readonly ISystemTrayService _systemTrayService;
        private readonly IWindowManagementService _windowManagementService;
        private Window? _mainWindow;
        private bool _isMinimizedToTray = false;

        public ApplicationExitService(
            ILogger<ApplicationExitService> logger,
            ISystemTrayService systemTrayService,
            IWindowManagementService windowManagementService)
        {
            _logger = logger;
            _systemTrayService = systemTrayService;
            _windowManagementService = windowManagementService;

            // 初始化系统托盘
            InitializeSystemTray();

            // 订阅托盘服务事件
            _systemTrayService.ShowMainWindowRequested += OnShowMainWindowRequested;
            _systemTrayService.ExitApplicationRequested += OnExitApplicationRequested;
        }

        /// <summary>
        /// 初始化系统托盘
        /// </summary>
        private void InitializeSystemTray()
        {
            try
            {
                // 初始化系统托盘服务
                _systemTrayService.Initialize();

                //MAUI 处理后的文件名
                var fileName = "tray_logo.scale-100.ico";
                // 设置托盘图标 - 使用绝对路径
                var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                _systemTrayService.SetTrayIcon(iconPath);

                // 显示托盘图标
                _systemTrayService.ShowTrayIcon();

                // 设置托盘提示文本
                _systemTrayService.SetTrayTooltip(ApplicationInfo.AppName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化系统托盘服务时出错");
            }
        }

        /// <summary>
        /// 处理窗口关闭请求
        /// </summary>
        /// <param name="window">要关闭的窗口</param>
        /// <returns>是否允许关闭窗口</returns>
        public async Task<bool> HandleWindowCloseRequestAsync(Window window)
        {
            try
            {
                // 如果不是主窗口，直接允许关闭
                if (!IsMainWindow(window))
                {
                    _logger.LogDebug("非主窗口关闭请求，直接允许关闭");
                    return true;
                }

                // 显示选择对话框
                var choice = await ShowCloseChoiceDialogAsync();

                switch (choice)
                {
                    case WindowCloseChoice.Cancel:
                        return false;

                    case WindowCloseChoice.MinimizeToTray:
                        MinimizeToTray(window);
                        return false; // 不关闭窗口，而是隐藏

                    case WindowCloseChoice.ExitApplication:
                        ExitApplication();
                        return true;

                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理窗口关闭请求时出错");
                return true; // 出错时允许关闭
            }
        }

        /// <summary>
        /// 最小化到托盘
        /// </summary>
        /// <param name="window">要最小化的窗口</param>
        public void MinimizeToTray(Window window)
        {
            try
            {
                if (IsMainWindow(window))
                {
                    _mainWindow = window;

                    // 使用平台特定的窗口管理服务隐藏窗口
                    _windowManagementService.HideWindow(window);

                    _isMinimizedToTray = true;

                    // 显示托盘图标
                    _systemTrayService.ShowTrayIcon();

                    // 显示托盘通知
                    _systemTrayService.ShowNotification(
                        "Market Assistant",
                        "应用程序已最小化到系统托盘");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "最小化到托盘时出错");
            }
        }

        /// <summary>
        /// 从托盘恢复主窗口
        /// </summary>
        public void RestoreFromTray()
        {
            try
            {
                if (_mainWindow != null && _isMinimizedToTray)
                {
                    // 使用平台特定的窗口管理服务显示和激活窗口
                    _windowManagementService.ShowAndActivateWindow(_mainWindow);

                    // 激活窗口
                    Application.Current?.ActivateWindow(_mainWindow);

                    _isMinimizedToTray = false;

                    _logger.LogInformation("从系统托盘恢复主窗口");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "从托盘恢复主窗口时出错");
            }
        }

        /// <summary>
        /// 退出应用程序
        /// </summary>
        public void ExitApplication()
        {
            try
            {
                // 隐藏托盘图标
                _systemTrayService.HideTrayIcon();

                // 释放托盘服务资源
                _systemTrayService.Dispose();

                // 退出应用程序
                Application.Current?.Quit();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "退出应用程序时出错");
            }
        }

        /// <summary>
        /// 检查是否为主窗口
        /// </summary>
        /// <param name="window">窗口实例</param>
        /// <returns>是否为主窗口</returns>
        public bool IsMainWindow(Window window)
        {
            // 简单的判断逻辑：第一个创建的窗口或包含AppShell的窗口
            return window.Page is AppShell ||
                   Application.Current?.Windows?.FirstOrDefault() == window;
        }

        /// <summary>
        /// 显示关闭选择对话框
        /// </summary>
        /// <returns>用户选择</returns>
        private async Task<WindowCloseChoice> ShowCloseChoiceDialogAsync()
        {
            try
            {
                // 确保在主线程中显示对话框
                var tcs = new TaskCompletionSource<string>();

                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.Dispatch(async () =>
                    {
                        try
                        {
                            var result = await Shell.Current.DisplayActionSheet(
                                "是否退出？",
                                "取消",
                                null,
                                "最小化到托盘",
                                "退出应用程序");
                            tcs.SetResult(result);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "在主线程显示对话框时出错");
                            tcs.SetResult("取消");
                        }
                    });
                }
                else
                {
                    // 如果没有 Dispatcher，直接尝试显示
                    var result = await Shell.Current.DisplayActionSheet(
                        "选择操作",
                        "取消",
                        null,
                        "最小化到托盘",
                        "退出应用程序");
                    tcs.SetResult(result);
                }

                var dialogResult = await tcs.Task;
                return dialogResult switch
                {
                    "最小化到托盘" => WindowCloseChoice.MinimizeToTray,
                    "退出应用程序" => WindowCloseChoice.ExitApplication,
                    _ => WindowCloseChoice.Cancel
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "显示关闭选择对话框时出错");
                return WindowCloseChoice.Cancel;
            }
        }

        /// <summary>
        /// 处理显示主窗口请求
        /// </summary>
        private void OnShowMainWindowRequested(object? sender, EventArgs e)
        {
            RestoreFromTray();
        }

        /// <summary>
        /// 处理退出应用程序请求
        /// </summary>
        private void OnExitApplicationRequested(object? sender, EventArgs e)
        {
            ExitApplication();
        }
    }
}