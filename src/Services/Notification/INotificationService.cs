namespace MarketAssistant.Services.Notification;

/// <summary>
/// 通知服务接口
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// 显示成功消息
    /// </summary>
    void ShowSuccess(string message, int durationMs = 3000);

    /// <summary>
    /// 显示错误消息
    /// </summary>
    void ShowError(string message, int durationMs = 3000);

    /// <summary>
    /// 显示信息消息
    /// </summary>
    void ShowInfo(string message, int durationMs = 3000);

    /// <summary>
    /// 显示警告消息
    /// </summary>
    void ShowWarning(string message, int durationMs = 3000);
}


