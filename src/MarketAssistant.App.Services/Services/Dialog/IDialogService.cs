using MarketAssistant.Trading.Models;

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
    /// 显示策略执行历史对话框窗口
    /// </summary>
    Task ShowStrategyExecutionAsync(TradingStrategy strategy, IReadOnlyList<TradeRecord> records);

    /// <summary>
    /// 显示确认对话框（两个按钮，可自定义文本）
    /// 取消令牌触发时会主动关闭对话框并返回取消按钮语义的结果（false）。
    /// </summary>
    /// <param name="topmost">置顶显示（资金相关的人审确认应置顶，避免被全屏应用遮挡后静默超时拒绝）</param>
    Task<bool> ShowConfirmationAsync(string title, string message, string accept = "确认", string cancel = "取消", CancellationToken ct = default, bool topmost = false);

    /// <summary>
    /// 显示带有自定义按钮的对话框。
    /// 取消令牌触发时会主动关闭对话框（返回 null）。
    /// </summary>
    /// <param name="topmost">置顶显示（资金相关的人审确认应置顶，避免被全屏应用遮挡后静默超时拒绝）</param>
    Task<string?> ShowCustomDialogAsync(string title, string message, string[] buttons, CancellationToken ct = default, bool topmost = false);

    /// <summary>
    /// 显示输入对话框
    /// </summary>
    Task<string?> ShowInputDialogAsync(string title, string message, string? defaultValue = null);
}
