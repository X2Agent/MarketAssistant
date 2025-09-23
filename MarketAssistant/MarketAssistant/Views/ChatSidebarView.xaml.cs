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
        
        // 订阅ViewModel的滚动事件
        viewModel.ScrollToBottom += ScrollToBottom;
        
        // 监听消息集合变化
        viewModel.ChatMessages.CollectionChanged += OnChatMessagesChanged;
    }

    /// <summary>
    /// 处理消息集合变化
    /// </summary>
    private void OnChatMessagesChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
        {
            ScrollToBottom();
        }
    }

    /// <summary>
    /// 滚动到底部显示最新消息
    /// </summary>
    public void ScrollToBottom()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.Delay(50); // 等待UI更新
                
                if (BindingContext is ChatSidebarViewModel viewModel && 
                    viewModel.ChatMessages?.Count > 0)
                {
                    var lastMessage = viewModel.ChatMessages.Last();
                    ChatCollectionView.ScrollTo(lastMessage, ScrollToPosition.End, animate: true);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"滚动到底部失败: {ex.Message}");
            }
        });
    }
}
