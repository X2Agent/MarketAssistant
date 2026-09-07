using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MarketAssistant.Views.Windows;

/// <summary>
/// 通用消息对话框窗口（消息展示 + 动态按钮列表）。
/// AXAML 负责布局与样式，code-behind 仅负责按钮点击结果回传。
/// </summary>
public partial class MessageDialogWindow : Window
{
    /// <summary>
    /// 用户选择的按钮文本；窗口关闭时由 <see cref="OnButtonClick"/> 设置。
    /// </summary>
    public string? Result { get; private set; }

    public MessageDialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 设置对话框内容。
    /// </summary>
    /// <param name="title">窗口标题</param>
    /// <param name="message">消息文本</param>
    /// <param name="buttons">按钮文本数组，第一个会被标记为主要按钮</param>
    public void SetContent(string title, string message, string[] buttons)
    {
        Title = title;
        var messageText = this.FindControl<TextBlock>("MessageText");
        if (messageText != null)
            messageText.Text = message;

        var buttonItemsControl = this.FindControl<ItemsControl>("ButtonItemsControl");
        if (buttonItemsControl != null)
        {
            buttonItemsControl.ItemsSource = buttons
                .Select((text, i) => new DialogButton(text, isPrimary: i == 0))
                .ToList();
        }
    }

    private void OnButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.DataContext is DialogButton dialogButton)
        {
            Result = dialogButton.Text;
            Close();
        }
    }

    /// <summary>
    /// 对话框按钮数据模型。
    /// </summary>
    public sealed class DialogButton(string text, bool isPrimary)
    {
        public string Text { get; } = text;
        public bool IsPrimary { get; } = isPrimary;
    }
}
