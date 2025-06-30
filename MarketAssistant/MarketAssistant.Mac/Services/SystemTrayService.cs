using AppKit;
using Foundation;
using MarketAssistant.Services;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Mac.Services
{
    //https://github.com/davidortinau/WeatherTwentyOne/blob/main/src/WeatherTwentyOne/Platforms/MacCatalyst/TrayService.cs
    
    /// <summary>
    /// Mac平台系统托盘服务实现
    /// </summary>
    public class SystemTrayService : ISystemTrayService
    {
        private readonly ILogger<SystemTrayService> _logger;
        private NSStatusItem? _statusItem;
        private NSMenu? _menu;
        private bool _disposed = false;

        public event EventHandler? TrayIconClicked;
        public event EventHandler? ShowMainWindowRequested;
        public event EventHandler? ExitApplicationRequested;

        public SystemTrayService(ILogger<SystemTrayService> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// 初始化系统托盘
        /// </summary>
        public void Initialize()
        {
            try
            {
                // 在主线程上执行UI操作
                NSApplication.SharedApplication.InvokeOnMainThread(() =>
                {
                    // 创建状态栏项目
                    _statusItem = NSStatusBar.SystemStatusBar.CreateStatusItem(NSStatusItemLength.Variable);

                    if (_statusItem?.Button != null)
                    {
                        // 设置默认图标和标题
                        _statusItem.Button.Title = "MA";
                        _statusItem.Button.ToolTip = "Market Assistant";

                        // 创建菜单
                        CreateMenu();

                        // 设置菜单
                        _statusItem.Menu = _menu;
                    }
                });

                _logger.LogInformation("Mac系统托盘服务初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化Mac系统托盘服务失败");
                throw;
            }
        }

        /// <summary>
        /// 显示托盘图标
        /// </summary>
        public void ShowTrayIcon()
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                if (_statusItem != null)
                {
                    _statusItem.Visible = true;
                    _logger.LogDebug("显示系统托盘图标");
                }
            });
        }

        /// <summary>
        /// 隐藏托盘图标
        /// </summary>
        public void HideTrayIcon()
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                if (_statusItem != null)
                {
                    _statusItem.Visible = false;
                    _logger.LogDebug("隐藏系统托盘图标");
                }
            });
        }

        /// <summary>
        /// 设置托盘图标
        /// </summary>
        /// <param name="icon">图标对象（NSImage 对象或文件路径字符串）</param>
        public void SetTrayIcon(object icon)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                if (_statusItem?.Button == null) return;

                try
                {
                    switch (icon)
                    {
                        case NSImage imageObj:
                            imageObj.Size = new CoreGraphics.CGSize(18, 18);
                            _statusItem.Button.Image = imageObj;
                            _statusItem.Button.Title = ""; // 清除文字标题
                            break;
                        case string iconPath when File.Exists(iconPath):
                            var image = new NSImage(iconPath);
                            image.Size = new CoreGraphics.CGSize(18, 18);
                            _statusItem.Button.Image = image;
                            _statusItem.Button.Title = ""; // 清除文字标题
                            break;
                        case string iconPath:
                            _logger.LogWarning($"托盘图标文件不存在: {iconPath}");
                            SetDefaultIcon();
                            break;
                        default:
                            _logger.LogWarning($"不支持的图标类型: {icon?.GetType().Name}");
                            SetDefaultIcon();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"设置托盘图标失败: {iconPath}");
                    SetDefaultIcon();
                }
            });
        }

        /// <summary>
        /// 设置托盘提示文本
        /// </summary>
        /// <param name="text">提示文本</param>
        public void SetTrayTooltip(string text)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                if (_statusItem?.Button != null)
                {
                    _statusItem.Button.ToolTip = text;
                }
            });
        }

        /// <summary>
        /// 显示托盘通知
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="message">消息内容</param>
        public void ShowNotification(string title, string message)
        {
            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                var notification = new NSUserNotification
                {
                    Title = title,
                    InformativeText = message,
                    DeliveryDate = NSDate.Now
                };

                NSUserNotificationCenter.DefaultUserNotificationCenter.DeliverNotification(notification);
            });
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            NSApplication.SharedApplication.InvokeOnMainThread(() =>
            {
                try
                {
                    if (_statusItem != null)
                    {
                        NSStatusBar.SystemStatusBar.RemoveStatusItem(_statusItem);
                        _statusItem.Dispose();
                        _statusItem = null;
                    }

                    if (_menu != null)
                    {
                        _menu.Dispose();
                        _menu = null;
                    }

                    _disposed = true;
                    _logger.LogInformation("Mac系统托盘服务已释放资源");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "释放Mac系统托盘服务资源时出错");
                }
            });
        }

        /// <summary>
        /// 设置默认图标
        /// </summary>
        private void SetDefaultIcon()
        {
            if (_statusItem?.Button != null)
            {
                _statusItem.Button.Title = "MA";
                _statusItem.Button.Image = null;
            }
        }

        /// <summary>
        /// 创建菜单
        /// </summary>
        private void CreateMenu()
        {
            _menu = new NSMenu();

            // 显示主界面菜单项
            var showMenuItem = new NSMenuItem("显示主界面", (sender, e) =>
            {
                ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
            });
            _menu.AddItem(showMenuItem);

            // 分隔线
            _menu.AddItem(NSMenuItem.SeparatorItem);

            // 退出菜单项
            var exitMenuItem = new NSMenuItem("退出", (sender, e) =>
            {
                ExitApplicationRequested?.Invoke(this, EventArgs.Empty);
            });
            _menu.AddItem(exitMenuItem);
        }
    }
}