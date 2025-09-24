using MarketAssistant.ViewModels;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Collections.Specialized;
using System.Windows.Input;

namespace MarketAssistant.Views;

/// <summary>
/// 聊天侧边栏视图
/// </summary>
public partial class ChatSidebarView : ContentView
{
    private readonly ILogger<ChatSidebarView>? _logger;
    private ChatSidebarViewModel? _viewModel;
    private bool _isCollectionViewLoaded = false;

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

    public ChatSidebarView(ChatSidebarViewModel viewModel, ILogger<ChatSidebarView> logger) : this()
    {
        _logger = logger;
        BindingContext = viewModel;
        AttachToViewModel(viewModel);
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (BindingContext is ChatSidebarViewModel newViewModel)
        {
            AttachToViewModel(newViewModel);
        }
        else
        {
            DetachFromViewModel();
        }
    }

    private void AttachToViewModel(ChatSidebarViewModel viewModel)
    {
        if (ReferenceEquals(_viewModel, viewModel))
        {
            return;
        }

        DetachFromViewModel();

        _viewModel = viewModel;
        _viewModel.ChatMessages.CollectionChanged += OnChatMessagesChanged;

        // 如果已经有消息且CollectionView已加载，延迟滚动到底部
        if (_viewModel.ChatMessages.Count > 0 && _isCollectionViewLoaded)
        {
            _ = ScrollToBottomAsync();
        }
    }

    private void DetachFromViewModel()
    {
        if (_viewModel is null)
        {
            return;
        }

        _viewModel.ChatMessages.CollectionChanged -= OnChatMessagesChanged;
        _viewModel = null;
    }

    /// <summary>
    /// 聊天消息集合变化时自动滚动到底部
    /// </summary>
    private void OnChatMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 只在添加、替换或重置消息时滚动到底部
        if (e.Action is NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Replace or NotifyCollectionChangedAction.Reset)
        {
            _ = ScrollToBottomAsync();
        }
    }

    /// <summary>
    /// CollectionView 初始化完成后触发
    /// </summary>
    private void OnChatCollectionViewLoaded(object? sender, EventArgs e)
    {
        _isCollectionViewLoaded = true;
        // 初始化完成后延迟滚动到底部
        _ = ScrollToBottomAsync();
    }

    /// <summary>
    /// 异步滚动到底部，确保CollectionView完全初始化
    /// </summary>
    private async Task ScrollToBottomAsync()
    {
        if (!_isCollectionViewLoaded)
        {
            return;
        }

        try
        {
            // 等待一小段时间确保UI完全渲染
            await Task.Delay(100);

            await Dispatcher.DispatchAsync(() =>
            {
                if (!IsVisible || ChatCollectionView.ItemsSource is not IList items || items.Count == 0)
                {
                    return;
                }

                ChatCollectionView.ScrollTo(items[^1], position: ScrollToPosition.End, animate: true);
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "滚动到底部失败");
        }
    }
}
