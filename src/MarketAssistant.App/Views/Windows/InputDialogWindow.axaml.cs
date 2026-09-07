using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace MarketAssistant.Views.Windows;

/// <summary>
/// 输入对话框窗口（提示信息 + 文本输入 + 确定/取消按钮）。
/// 支持 Enter 确认、Esc 取消的键盘操作。
/// </summary>
public partial class InputDialogWindow : Window
{
    /// <summary>
    /// 用户输入的文本；取消时为 null。
    /// </summary>
    public string? Result { get; private set; }

    private bool _isClosed;

    public InputDialogWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 设置对话框内容。
    /// </summary>
    /// <param name="title">窗口标题</param>
    /// <param name="message">提示信息</param>
    /// <param name="defaultValue">输入框默认值</param>
    public void SetContent(string title, string message, string? defaultValue)
    {
        Title = title;

        var promptText = this.FindControl<TextBlock>("PromptText");
        if (promptText != null)
            promptText.Text = message;

        var inputTextBox = this.FindControl<TextBox>("InputTextBox");
        if (inputTextBox != null)
        {
            inputTextBox.Text = defaultValue ?? string.Empty;
            inputTextBox.KeyDown += OnInputKeyDown;
        }
    }

    /// <summary>
    /// 窗口显示后聚焦到输入框并全选文本。
    /// </summary>
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        var inputTextBox = this.FindControl<TextBox>("InputTextBox");
        if (inputTextBox != null)
        {
            inputTextBox.Focus();
            inputTextBox.SelectAll();
        }
    }

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (_isClosed) return;

        switch (e.Key)
        {
            case Key.Enter:
                ConfirmInput();
                e.Handled = true;
                break;
            case Key.Escape:
                CancelInput();
                e.Handled = true;
                break;
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => ConfirmInput();

    private void OnCancelClick(object? sender, RoutedEventArgs e) => CancelInput();

    private void ConfirmInput()
    {
        if (_isClosed) return;

        var inputTextBox = this.FindControl<TextBox>("InputTextBox");
        Result = inputTextBox?.Text;
        _isClosed = true;
        Close();
    }

    private void CancelInput()
    {
        if (_isClosed) return;

        Result = null;
        _isClosed = true;
        Close();
    }
}
