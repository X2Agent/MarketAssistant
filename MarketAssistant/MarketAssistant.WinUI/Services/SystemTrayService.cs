using MarketAssistant.Services;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Windows.Forms;

namespace MarketAssistant.WinUI.Services
{
    /// <summary>
    /// Windows平台系统托盘服务实现
    /// </summary>
    public class SystemTrayService : ISystemTrayService
    {
        private readonly ILogger<SystemTrayService> _logger;
        private NotifyIcon? _notifyIcon;
        private ContextMenuStrip? _contextMenu;
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
                // 创建托盘图标
                _notifyIcon = new NotifyIcon
                {
                    Text = "Market Assistant",
                    Visible = false
                };

                // 设置默认图标
                SetDefaultIcon();

                // 创建右键菜单
                CreateContextMenu();

                // 绑定事件
                _notifyIcon.Click += OnTrayIconClick;
                _notifyIcon.DoubleClick += OnTrayIconDoubleClick;

                _logger.LogInformation("Windows系统托盘服务初始化完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化Windows系统托盘服务失败");
                throw;
            }
        }

        /// <summary>
        /// 显示托盘图标
        /// </summary>
        public void ShowTrayIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = true;
                _logger.LogDebug("显示系统托盘图标");
            }
        }

        /// <summary>
        /// 隐藏托盘图标
        /// </summary>
        public void HideTrayIcon()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _logger.LogDebug("隐藏系统托盘图标");
            }
        }

        /// <summary>
        /// 设置托盘图标
        /// </summary>
        /// <param name="icon">图标对象（Icon 对象或文件路径字符串）</param>
        public void SetTrayIcon(object icon)
        {
            if (_notifyIcon == null) return;

            try
            {
                switch (icon)
                {
                    case Icon iconObj:
                        _notifyIcon.Icon = iconObj;
                        break;
                    case string iconPath when File.Exists(iconPath):
                        _notifyIcon.Icon = new Icon(iconPath);
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
                _logger.LogError(ex, $"设置托盘图标失败: {icon}");
                SetDefaultIcon();
            }
        }

        /// <summary>
        /// 设置托盘提示文本
        /// </summary>
        /// <param name="text">提示文本</param>
        public void SetTrayTooltip(string text)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Text = text;
            }
        }

        /// <summary>
        /// 显示托盘通知
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="message">消息内容</param>
        public void ShowNotification(string title, string message)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            try
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _notifyIcon = null;
                }

                if (_contextMenu != null)
                {
                    _contextMenu.Dispose();
                    _contextMenu = null;
                }

                _disposed = true;
                _logger.LogInformation("Windows系统托盘服务已释放资源");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放Windows系统托盘服务资源时出错");
            }
        }

        /// <summary>
        /// 设置默认图标
        /// </summary>
        private void SetDefaultIcon()
        {
            try
            {
                // 创建一个简单的默认图标
                using var bitmap = new Bitmap(16, 16);
                using var graphics = Graphics.FromImage(bitmap);
                graphics.FillEllipse(Brushes.Blue, 0, 0, 16, 16);

                var iconHandle = bitmap.GetHicon();
                _notifyIcon!.Icon = Icon.FromHandle(iconHandle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置默认托盘图标失败");
            }
        }

        /// <summary>
        /// 创建右键菜单
        /// </summary>
        private void CreateContextMenu()
        {
            _contextMenu = new ContextMenuStrip();

            // 显示主界面菜单项
            var showMenuItem = new ToolStripMenuItem("显示主界面")
            {
                Font = new System.Drawing.Font(_contextMenu.Font, FontStyle.Bold)
            };
            showMenuItem.Click += (sender, e) => ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
            _contextMenu.Items.Add(showMenuItem);

            // 分隔线
            _contextMenu.Items.Add(new ToolStripSeparator());

            // 退出菜单项
            var exitMenuItem = new ToolStripMenuItem("退出");
            exitMenuItem.Click += (sender, e) => ExitApplicationRequested?.Invoke(this, EventArgs.Empty);
            _contextMenu.Items.Add(exitMenuItem);

            _notifyIcon!.ContextMenuStrip = _contextMenu;
        }

        /// <summary>
        /// 托盘图标单击事件
        /// </summary>
        private void OnTrayIconClick(object? sender, EventArgs e)
        {
            TrayIconClicked?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// 托盘图标双击事件
        /// </summary>
        private void OnTrayIconDoubleClick(object? sender, EventArgs e)
        {
            ShowMainWindowRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}