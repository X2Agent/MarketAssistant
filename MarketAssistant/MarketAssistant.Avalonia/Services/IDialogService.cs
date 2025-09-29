namespace MarketAssistant.Avalonia.Services;

/// <summary>
/// 对话框服务接口，支持依赖注入和单元测试
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// 显示简单的消息对话框
    /// </summary>
    Task<bool> ShowAlertAsync(string title, string message, string cancel = "取消");

    /// <summary>
    /// 显示确认对话框
    /// </summary>
    Task<bool> ShowConfirmationAsync(string title, string message, string accept = "确认", string cancel = "取消");

    /// <summary>
    /// 显示带有自定义按钮的对话框
    /// </summary>
    Task<string?> ShowCustomDialogAsync(string title, string message, string[] buttons);

    /// <summary>
    /// 显示输入对话框
    /// </summary>
    Task<string?> ShowInputDialogAsync(string title, string message, string? defaultValue = null);
}
