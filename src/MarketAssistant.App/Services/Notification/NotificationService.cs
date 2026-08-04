using MarketAssistant.Views.Windows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Notification;

/// <summary>
/// Avalonia 平台的通知服务实现（桌面右下角通知）
/// </summary>
public class NotificationService : INotificationService
{
    private const int DefaultDuration = 5000;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public void ShowSuccess(string message, int durationMs = DefaultDuration)
    {
        ShowNotification(message, NotificationType.Success, durationMs);
    }

    public void ShowError(string message, int durationMs = DefaultDuration)
    {
        ShowNotification(message, NotificationType.Error, durationMs);
    }

    public void ShowInfo(string message, int durationMs = DefaultDuration)
    {
        ShowNotification(message, NotificationType.Info, durationMs);
    }

    public void ShowWarning(string message, int durationMs = DefaultDuration)
    {
        ShowNotification(message, NotificationType.Warning, durationMs);
    }

    /// <summary>
    /// 显示通知（桌面右下角）
    /// </summary>
    private void ShowNotification(string message, NotificationType type, int durationMs)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var notification = new NotificationWindow();
                notification.SetMessage(message, type);
                await notification.ShowNotificationAsync(durationMs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "显示桌面通知失败");
            }
        });
    }
}
