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
        
        // 如果已有消息，则在UI加载完成后滚动到底部
        if (viewModel.ChatMessages.Count > 0)
        {
            Loaded += OnViewLoaded;
        }
        
        // 监听CollectionView的SizeChanged事件，确保在布局完成后滚动
        ChatCollectionView.SizeChanged += OnCollectionViewSizeChanged;
    }

    private bool _initialScrollPending = false;

    /// <summary>
    /// 视图加载完成后的处理
    /// </summary>
    private async void OnViewLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnViewLoaded;
        
        // 标记需要初始滚动
        _initialScrollPending = true;
        
        // 等待UI完全渲染完成
        await Task.Delay(200);
        ScrollToBottom();
    }

    /// <summary>
    /// CollectionView大小变化时的处理（确保布局完成后滚动）
    /// </summary>
    private void OnCollectionViewSizeChanged(object? sender, EventArgs e)
    {
        if (_initialScrollPending && BindingContext is ChatSidebarViewModel viewModel && viewModel.ChatMessages.Count > 0)
        {
            _initialScrollPending = false;
            ChatCollectionView.SizeChanged -= OnCollectionViewSizeChanged;
            
            // 延迟一点确保所有项都渲染完成
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(100);
                ScrollToBottom();
            });
        }
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
                if (BindingContext is ChatSidebarViewModel viewModel && 
                    viewModel.ChatMessages?.Count > 0)
                {
                    // 等待UI更新完成
                    await Task.Delay(100);
                    
                    var lastMessage = viewModel.ChatMessages.Last();
                    
                    // 使用更可靠的滚动方式
                    ChatCollectionView.ScrollTo(lastMessage, ScrollToPosition.End, animate: false);
                    
                    // 再次确保滚动到最底部
                    await Task.Delay(50);
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
