using MarketAssistant.Views.Windows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Notification;

/// <summary>
/// Avalonia 平台的通知服务实现（桌面右下角通知，多通知按槽位向上堆叠）
/// </summary>
public class NotificationService : INotificationService
{
    private const int DefaultDuration = 5000;
    private readonly ILogger<NotificationService> _logger;

    // 在屏通知列表（仅 UI 线程访问）：索引即堆叠槽位，0 = 底部基准位
    private readonly List<NotificationWindow> _activeNotifications = [];

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
    /// 显示通知（桌面右下角）。连续通知按槽位向上堆叠，关闭后上方通知顺次下移，
    /// 不再全部重叠在同一坐标导致只剩最后一条可见。
    /// </summary>
    private void ShowNotification(string message, NotificationType type, int durationMs)
    {
        Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                var notification = new NotificationWindow();
                notification.SetMessage(message, type);
                notification.SetStackSlot(_activeNotifications.Count);
                _activeNotifications.Add(notification);

                try
                {
                    await notification.ShowNotificationAsync(durationMs);
                }
                finally
                {
                    _activeNotifications.Remove(notification);
                    RepositionStack();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "显示桌面通知失败");
            }
        });
    }

    private void RepositionStack()
    {
        for (var i = 0; i < _activeNotifications.Count; i++)
        {
            _activeNotifications[i].MoveToSlot(i);
        }
    }
}
