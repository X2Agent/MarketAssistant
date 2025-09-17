using MarketAssistant.ViewModels;
using System.Windows.Input;

namespace MarketAssistant.Views;

/// <summary>
/// 聊天侧边栏视图
/// </summary>
public partial class ChatSidebarView : ContentView
{
    public static readonly BindableProperty CloseCommandProperty =
        BindableProperty.Create(nameof(CloseCommand), typeof(ICommand), typeof(ChatSidebarView), null);

    public ICommand CloseCommand
    {
        get => (ICommand)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public ChatSidebarView()
    {
        InitializeComponent();
    }

    public ChatSidebarView(ChatSidebarViewModel viewModel) : this()
    {
        BindingContext = viewModel;
    }

    /// <summary>
    /// 滚动到底部显示最新消息
    /// </summary>
    public void ScrollToBottom()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(100); // 等待UI更新
            await ChatScrollView.ScrollToAsync(0, double.MaxValue, true);
        });
    }
}
