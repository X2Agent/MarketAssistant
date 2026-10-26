using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MarketAssistant.Trading.Models;
using MarketAssistant.ViewModels.Home;
using MarketAssistant.Views.Windows;

namespace MarketAssistant.Services.Dialog;

/// <summary>
/// Avalonia 平台的对话框服务，UI 模板定义在 <see cref="MessageDialogWindow"/> 与
/// <see cref="InputDialogWindow"/>，本类仅负责窗口创建、UI 线程切换与结果回传。
/// </summary>
public class DialogService : IDialogService, IWindowService
{
    /// <summary>已打开的每日简报窗口；关闭时清空，用于重复打开防护。</summary>
    private DailyBriefingWindow? _briefingWindow;

    /// <summary>
    /// 显示简单的信息对话框（只有一个按钮）
    /// </summary>
    public async Task ShowMessageAsync(string title, string message, string button = "确定")
    {
        await ShowCustomDialogAsync(title, message, [button]);
    }

    /// <summary>
    /// 显示确认对话框（两个按钮，都可自定义）
    /// 取消令牌触发时主动关闭对话框，返回 false（取消语义）。
    /// </summary>
    /// <returns>如果用户点击确认返回 true，点击取消返回 false</returns>
    public async Task<bool> ShowConfirmationAsync(string title, string message, string accept = "确认", string cancel = "取消", CancellationToken ct = default, bool topmost = false)
    {
        var result = await ShowCustomDialogAsync(title, message, [accept, cancel], ct, topmost);
        return result == accept;
    }

    /// <summary>
    /// 显示带有自定义按钮的对话框
    /// 取消令牌触发时主动关闭对话框（Result 为 null），
    /// 避免"超时已自动拒绝但对话框仍挂在屏幕上、用户点击结果被丢弃"的错位。
    /// </summary>
    /// <returns>用户选择的按钮文本；取消令牌触发或无活动窗口时为 null</returns>
    public async Task<string?> ShowCustomDialogAsync(string title, string message, string[] buttons, CancellationToken ct = default, bool topmost = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowCustomDialogAsync(title, message, buttons, ct, topmost));
        }

        var owner = GetActiveWindow();
        if (owner == null) return null;

        var dialog = new MessageDialogWindow();
        dialog.SetContent(title, message, buttons);
        dialog.Topmost = topmost;
        using var cancelRegistration = ct.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }

    /// <summary>
    /// 显示策略执行历史对话框窗口
    /// </summary>
    public async Task ShowStrategyExecutionAsync(TradingStrategy strategy, IReadOnlyList<TradeRecord> records)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            await Dispatcher.UIThread.InvokeAsync(() => ShowStrategyExecutionAsync(strategy, records));
            return;
        }

        var owner = GetActiveWindow();
        if (owner == null) return;

        var window = new StrategyExecutionWindow();
        window.SetContent(strategy, records);
        await window.ShowDialog(owner);
    }

    /// <summary>
    /// 显示输入对话框
    /// </summary>
    /// <returns>用户输入的内容，如果取消则为 null</returns>
    public async Task<string?> ShowInputDialogAsync(string title, string message, string? defaultValue = null)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowInputDialogAsync(title, message, defaultValue));
        }

        var owner = GetActiveWindow();
        if (owner == null) return null;

        var dialog = new InputDialogWindow();
        dialog.SetContent(title, message, defaultValue);
        await dialog.ShowDialog(owner);
        return dialog.Result;
    }

    /// <summary>
    /// 以非模态方式显示每日简报窗口。已打开时聚焦既有窗口，避免重复开窗。
    /// </summary>
    /// <remarks>
    /// 与模态对话框不同，非模态窗口在无可见 owner 时仍可展示（仅失去居中锚点），
    /// 故 <see cref="GetActiveWindow"/> 取不到 owner 时直接 <c>Show()</c> 而非放弃。
    /// </remarks>
    public void ShowDailyBriefingWindow(DailyBriefingViewModel viewModel)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ShowDailyBriefingWindow(viewModel));
            return;
        }

        if (_briefingWindow is { IsVisible: true })
        {
            _briefingWindow.Activate();
            return;
        }

        var owner = GetActiveWindow();
        _briefingWindow = new DailyBriefingWindow { DataContext = viewModel };
        _briefingWindow.Closed += (_, _) => _briefingWindow = null;
        if (owner is not null)
        {
            _briefingWindow.Show(owner);
        }
        else
        {
            _briefingWindow.Show();
        }
    }

    /// <summary>
    /// 获取当前活动窗口
    /// </summary>
    /// <remarks>
    /// 只兜底到"可见"的主窗口：主窗口被隐藏进托盘时，对隐藏窗口 ShowDialog 用户根本看不到，
    /// 返回 null 让调用方（如交易确认）走"拒绝并通知"的 fail-closed 分支。
    /// </remarks>
    private static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows.FirstOrDefault(w => w.IsActive) ??
                   (desktop.MainWindow is { IsVisible: true } main ? main : null);
        }
        return null;
    }
}
