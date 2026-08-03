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
    /// </summary>
    /// <returns>如果用户点击确认返回 true，点击取消返回 false</returns>
    public async Task<bool> ShowConfirmationAsync(string title, string message, string accept = "确认", string cancel = "取消")
    {
        var result = await ShowCustomDialogAsync(title, message, new[] { accept, cancel });
        return result == accept;
    }

    /// <summary>
    /// 显示带有自定义按钮的对话框
    /// </summary>
    /// <returns>用户选择的按钮文本</returns>
    public async Task<string?> ShowCustomDialogAsync(string title, string message, string[] buttons)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowCustomDialogAsync(title, message, buttons));
        }

        var owner = GetActiveWindow();
        if (owner == null) return null;

        var dialog = new MessageDialogWindow();
        dialog.SetContent(title, message, buttons);
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
