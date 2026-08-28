using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using MarketAssistant.Views.Windows;

namespace MarketAssistant.Services.Dialog;

/// <summary>
/// Avalonia 平台的对话框服务，UI 模板定义在 <see cref="MessageDialogWindow"/> 与
/// <see cref="InputDialogWindow"/>，本类仅负责窗口创建、UI 线程切换与结果回传。
/// </summary>
public class DialogService : IDialogService
{
    /// <summary>
    /// 显示简单的信息对话框（只有一个按钮）
    /// </summary>
    public async Task ShowMessageAsync(string title, string message, string button = "确定")
    {
        await ShowCustomDialogAsync(title, message, new[] { button });
    }

    /// <summary>
    /// 显示确认对话框（两个按钮，都可自定义）
    /// 取消令牌触发时主动关闭对话框，返回 false（取消语义）。
    /// </summary>
    /// <returns>如果用户点击确认返回 true，点击取消返回 false</returns>
    public async Task<bool> ShowConfirmationAsync(string title, string message, string accept = "确认", string cancel = "取消", CancellationToken ct = default)
    {
        var result = await ShowCustomDialogAsync(title, message, new[] { accept, cancel }, ct);
        return result == accept;
    }

    /// <summary>
    /// 显示带有自定义按钮的对话框
    /// 取消令牌触发时主动关闭对话框（Result 为 null），
    /// 避免"超时已自动拒绝但对话框仍挂在屏幕上、用户点击结果被丢弃"的错位。
    /// </summary>
    /// <returns>用户选择的按钮文本；取消令牌触发或无活动窗口时为 null</returns>
    public async Task<string?> ShowCustomDialogAsync(string title, string message, string[] buttons, CancellationToken ct = default)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowCustomDialogAsync(title, message, buttons, ct));
        }

        var owner = GetActiveWindow();
        if (owner == null) return null;

        var dialog = new MessageDialogWindow();
        dialog.SetContent(title, message, buttons);
        using var cancelRegistration = ct.Register(() => Dispatcher.UIThread.Post(() => dialog.Close()));
        await dialog.ShowDialog(owner);
        return dialog.Result;
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
    /// 获取当前活动窗口
    /// </summary>
    private static Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        }
        return null;
    }
}
