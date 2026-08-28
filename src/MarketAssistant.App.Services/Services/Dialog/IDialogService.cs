namespace MarketAssistant.Services.Dialog;

/// <summary>
/// 对话框服务接口，支持依赖注入和单元测试
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// 显示简单的信息对话框（只有一个按钮）
    /// </summary>
    Task ShowMessageAsync(string title, string message, string button = "确定");

    /// <summary>
    /// 显示确认对话框（两个按钮，可自定义文本）
    /// 取消令牌触发时会主动关闭对话框并返回取消按钮语义的结果（false）。
    /// </summary>
    Task<bool> ShowConfirmationAsync(string title, string message, string accept = "确认", string cancel = "取消", CancellationToken ct = default);

    /// <summary>
    /// 显示带有自定义按钮的对话框。
    /// 取消令牌触发时会主动关闭对话框（返回 null）。
    /// </summary>
    Task<string?> ShowCustomDialogAsync(string title, string message, string[] buttons, CancellationToken ct = default);

    /// <summary>
    /// 显示输入对话框
    /// </summary>
    Task<string?> ShowInputDialogAsync(string title, string message, string? defaultValue = null);
}
