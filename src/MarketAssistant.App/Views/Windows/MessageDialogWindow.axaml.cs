using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace MarketAssistant.Views.Windows;

/// <summary>
/// 通用消息对话框窗口（消息展示 + 动态按钮列表）。
/// AXAML 负责布局与样式，code-behind 仅负责按钮点击结果回传与危险操作的默认焦点。
/// </summary>
public partial class MessageDialogWindow : Window
{
    private int _initialFocusIndex = -1;

    /// <summary>
    /// 用户选择的按钮文本；窗口关闭时由 <see cref="OnButtonClick"/> 设置。
    /// </summary>
    public string? Result { get; private set; }

    public MessageDialogWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    /// <summary>
    /// 设置对话框内容。
    /// </summary>
    /// <param name="title">窗口标题</param>
    /// <param name="message">消息文本</param>
    /// <param name="buttons">按钮文本数组，第一个是确认动作</param>
    /// <param name="destructive">
    /// 危险操作：第一个按钮用危险色，最后一个按钮绑定 Esc 并在打开后取得焦点（回车默认落在安全侧）；
    /// 否则第一个按钮用主按钮样式
    /// </param>
    public void SetContent(string title, string message, string[] buttons, bool destructive = false)
    {
        Title = title;
        var messageText = this.FindControl<TextBlock>("MessageText");
        if (messageText != null)
            messageText.Text = message;

        var buttonItemsControl = this.FindControl<ItemsControl>("ButtonItemsControl");
        if (buttonItemsControl != null)
        {
            buttonItemsControl.ItemsSource = buttons
                .Select((text, i) => new DialogButton(
                    text,
                    isPrimary: i == 0 && !destructive,
                    isDanger: i == 0 && destructive,
                    isCancel: destructive && i == buttons.Length - 1))
                .ToList();
        }

        _initialFocusIndex = destructive ? buttons.Length - 1 : -1;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_initialFocusIndex < 0)
            return;

        // 按钮由 ItemsControl 按模板生成，Opened 时容器可能尚未完成布局，等布局后再取焦点；
        // 以 Tab 方式取焦点才会触发 :focus-visible 焦点环，让用户看见回车默认落在哪个按钮
        Dispatcher.UIThread.Post(() =>
        {
            var items = this.FindControl<ItemsControl>("ButtonItemsControl");
            var container = items?.ContainerFromIndex(_initialFocusIndex) as ContentPresenter;
            (container?.Child as Button)?.Focus(NavigationMethod.Tab);
        }, DispatcherPriority.Loaded);
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
    public sealed class DialogButton(string text, bool isPrimary, bool isDanger, bool isCancel)
    {
        public string Text { get; } = text;
        public bool IsPrimary { get; } = isPrimary;
        public bool IsDanger { get; } = isDanger;
        public bool IsCancel { get; } = isCancel;
    }
}
