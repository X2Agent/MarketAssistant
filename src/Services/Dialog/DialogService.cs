using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace MarketAssistant.Services.Dialog;

/// <summary>
/// Avalonia平台的对话框服务
/// </summary>
public class DialogService : IDialogService
{
    /// <summary>
    /// 显示简单的信息对话框（只有一个按钮）
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="button">按钮文本（默认"确定"）</param>
    public async Task ShowMessageAsync(string title, string message, string button = "确定")
    {
        await ShowCustomDialogAsync(title, message, new[] { button });
    }

    /// <summary>
    /// 显示确认对话框（两个按钮，都可自定义）
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="accept">确认按钮文本（默认"确认"）</param>
    /// <param name="cancel">取消按钮文本（默认"取消"）</param>
    /// <returns>如果用户点击确认返回true，点击取消返回false</returns>
    public async Task<bool> ShowConfirmationAsync(string title, string message, string accept = "确认", string cancel = "取消")
    {
        var result = await ShowCustomDialogAsync(title, message, new[] { accept, cancel });
        return result == accept;
    }

    /// <summary>
    /// 显示带有自定义按钮的对话框
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">消息内容</param>
    /// <param name="buttons">按钮文本数组</param>
    /// <returns>用户选择的按钮文本</returns>
    public async Task<string?> ShowCustomDialogAsync(string title, string message, string[] buttons)
    {
        // 确保在UI线程上执行
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowCustomDialogAsync(title, message, buttons));
        }

        var window = GetActiveWindow();
        if (window == null) return null;

        var tcs = new TaskCompletionSource<string?>();

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            MinWidth = 300,
            MaxWidth = 500,
            ShowInTaskbar = false,
            Topmost = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 16
        };

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 450
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12
        };

        // 处理对话框关闭事件
        dialog.Closing += (s, e) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.SetResult(null);
            }
        };

        for (int i = 0; i < buttons.Length; i++)
        {
            var buttonText = buttons[i];
            var button = new Button
            {
                Content = buttonText,
                MinWidth = 80,
                Padding = new Thickness(16, 8)
            };

            // 第一个按钮设置为默认（主要）按钮
            if (i == 0)
            {
                button.Classes.Add("accent");
            }

            // 使用局部变量避免闭包问题
            var capturedButtonText = buttonText;
            button.Click += (s, e) =>
            {
                if (!tcs.Task.IsCompleted)
                {
                    tcs.SetResult(capturedButtonText);
                }
                dialog.Close();
            };

            buttonPanel.Children.Add(button);
        }

        panel.Children.Add(textBlock);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        // 异步显示对话框
        _ = dialog.ShowDialog(window);

        return await tcs.Task;
    }

    /// <summary>
    /// 显示输入对话框（Avalonia特有功能）
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="message">提示信息</param>
    /// <param name="defaultValue">默认值</param>
    /// <returns>用户输入的内容，如果取消则为null</returns>
    public async Task<string?> ShowInputDialogAsync(string title, string message, string? defaultValue = null)
    {
        // 确保在UI线程上执行
        if (!Dispatcher.UIThread.CheckAccess())
        {
            return await Dispatcher.UIThread.InvokeAsync(() => ShowInputDialogAsync(title, message, defaultValue));
        }

        var window = GetActiveWindow();
        if (window == null) return null;

        var tcs = new TaskCompletionSource<string?>();

        var dialog = new Window
        {
            Title = title,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            CanResize = false,
            MinWidth = 350,
            ShowInTaskbar = false
        };

        var panel = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 12
        };

        var textBlock = new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 400
        };

        var textBox = new TextBox
        {
            Text = defaultValue ?? string.Empty,
            Width = 300,
            Watermark = "请输入...",
            Margin = new Thickness(0, 8, 0, 0)
        };

        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 12,
            Margin = new Thickness(0, 16, 0, 0)
        };

        var okButton = new Button
        {
            Content = "确定",
            IsDefault = true,
            MinWidth = 80,
            Padding = new Thickness(16, 8)
        };

        var cancelButton = new Button
        {
            Content = "取消",
            IsCancel = true,
            MinWidth = 80,
            Padding = new Thickness(16, 8)
        };

        // 处理对话框关闭事件
        dialog.Closing += (s, e) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.SetResult(null);
            }
        };

        // 处理Enter键确认
        textBox.KeyDown += (s, e) =>
        {
            if (e.Key == Key.Enter && !tcs.Task.IsCompleted)
            {
                tcs.SetResult(textBox.Text);
                dialog.Close();
            }
            else if (e.Key == Key.Escape && !tcs.Task.IsCompleted)
            {
                tcs.SetResult(null);
                dialog.Close();
            }
        };

        okButton.Click += (s, e) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.SetResult(textBox.Text);
            }
            dialog.Close();
        };

        cancelButton.Click += (s, e) =>
        {
            if (!tcs.Task.IsCompleted)
            {
                tcs.SetResult(null);
            }
            dialog.Close();
        };

        buttonPanel.Children.Add(okButton);
        buttonPanel.Children.Add(cancelButton);

        panel.Children.Add(textBlock);
        panel.Children.Add(textBox);
        panel.Children.Add(buttonPanel);

        dialog.Content = panel;

        // 异步显示对话框
        _ = dialog.ShowDialog(window);

        // 延迟设置焦点，确保对话框完全显示后再设置
        _ = Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await Task.Delay(100); // 短暂延迟确保对话框渲染完成
            textBox.Focus();
            textBox.SelectAll();
        });

        return await tcs.Task;
    }

    /// <summary>
    /// 获取当前活动窗口
    /// </summary>
    private Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            return desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
        }
        return null;
    }
}
