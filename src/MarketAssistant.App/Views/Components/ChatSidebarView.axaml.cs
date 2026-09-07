using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using MarketAssistant.ViewModels;
using System.Collections.Specialized;
using System.Linq;
using System.Windows.Input;

namespace MarketAssistant.Views.Components;

/// <summary>
/// 聊天侧边栏视图
/// </summary>
public partial class ChatSidebarView : UserControl
{
    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ChatSidebarView, ICommand?>(nameof(CloseCommand));

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    /// <summary>
    /// 距底不超过该值视为"在底部"（允许自动跟随）；过大会导致贴底滚动与用户上滚互相拉扯
    /// </summary>
    private const double NearBottomThreshold = 24;

    private ChatSidebarViewModel? _subscribedViewModel;
    private bool _isNearBottom = true;
    private bool _isAutoScrollScheduled;
    private ScrollViewer? _listScrollViewer;

    public ChatSidebarView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        // Tunnel 策略先于 TextBox 类处理器执行：AcceptsReturn=true 时其会先消费 Enter，冒泡订阅收不到
        MessageEntry.AddHandler(KeyDownEvent, MessageEntry_KeyDown, RoutingStrategies.Tunnel);

        // 监听内部滚动位置：ListBox 的 ScrollViewer 滚动事件会冒泡上来
        AddHandler(ScrollViewer.ScrollChangedEvent, OnListScrollChanged, RoutingStrategies.Bubble);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        ResetScrollTracking();
        SubscribeToViewModel(DataContext as ChatSidebarViewModel);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        UnsubscribeFromViewModel();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        UnsubscribeFromViewModel();
        ResetScrollTracking();
        SubscribeToViewModel(DataContext as ChatSidebarViewModel);
    }

    /// <summary>
    /// 侧边栏关闭再打开（或切换会话）会重建 ListBox 模板，缓存的 ScrollViewer 与贴底状态需重置
    /// </summary>
    private void ResetScrollTracking()
    {
        _listScrollViewer = null;
        _isNearBottom = true;
    }

    private void SubscribeToViewModel(ChatSidebarViewModel? vm)
    {
        if (vm == null || vm == _subscribedViewModel)
            return;

        _subscribedViewModel = vm;
        vm.ChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
    }

    private void UnsubscribeFromViewModel()
    {
        if (_subscribedViewModel == null)
            return;

        _subscribedViewModel.ChatMessages.CollectionChanged -= ChatMessages_CollectionChanged;
        _subscribedViewModel = null;
    }

    /// <summary>
    /// 跟踪用户滚动位置：只有当用户停留在列表底部附近时才允许自动滚动跟随，
    /// 避免批量注入历史消息或流式刷新时把用户正在阅读的内容顶走
    /// </summary>
    private void OnListScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (e.Source is not ScrollViewer scrollViewer || !ReferenceEquals(scrollViewer, FindListScrollViewer()))
            return;

        // 流式输出使消息不断长高、Extent 随之增长，这属于内容变化而非用户滚动：
        // 不据此改判贴底状态，避免把“内容顶高”误判成“用户离开底部”。
        // 停在底部时顺势跟随新长出的内容；上滚离开底部后 Offset 不变、直接跳过重算
        if (e.ExtentDelta.Y > 0 && e.OffsetDelta.Y == 0)
        {
            if (_isNearBottom)
                ScheduleFollowScroll();
            return;
        }

        var distanceFromBottom = scrollViewer.Extent.Height - scrollViewer.Offset.Y - scrollViewer.Viewport.Height;
        _isNearBottom = distanceFromBottom <= NearBottomThreshold;
    }

    private ScrollViewer? FindListScrollViewer()
    {
        // 流式期间 ScrollChanged 高频触发，避免每次都递归遍历可视树
        _listScrollViewer ??= ChatListBox.FindDescendantOfType<ScrollViewer>();
        return _listScrollViewer;
    }

    private void ChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
            return;

        ScheduleFollowScroll();
    }

    /// <summary>
    /// 贴底跟随滚动：新增消息与流式推高 Extent 都会走到这里，
    /// 通过布局周期去重避免滚动操作被高频事件淹没、列表反复跳动
    /// </summary>
    private void ScheduleFollowScroll()
    {
        if (!_isNearBottom || _isAutoScrollScheduled)
            return;

        _isAutoScrollScheduled = true;
        // 用 Loaded 优先级（布局完成后、渲染前执行）：Background 会晚一帧才补滚动，
        // 形成“Extent 先增长、Offset 后跳底”的两段式运动，视觉上就是列表整体抽动
        Dispatcher.UIThread.Post(() =>
        {
            _isAutoScrollScheduled = false;
            if (!_isNearBottom)
                return;

            var scrollViewer = FindListScrollViewer();
            if (scrollViewer is null)
                return;

            var maxOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
            if (scrollViewer.Extent.Height > scrollViewer.Viewport.Height)
            {
                scrollViewer.Offset = new Vector(scrollViewer.Offset.X, maxOffset);
            }
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Enter 发送并标记 Handled；Shift+Enter 放行给 TextBox 原生换行</summary>
    private void MessageEntry_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (e.KeyModifiers != KeyModifiers.None)
            return;

        e.Handled = true;

        if (DataContext is ChatSidebarViewModel vm
            && !string.IsNullOrWhiteSpace(vm.UserInput)
            && vm.SendMessageCommand.CanExecute(null))
        {
            // 用户主动发言后必然期望看到最新回复，强制恢复底部跟随
            _isNearBottom = true;
            vm.SendMessageCommand.Execute(null);
        }
    }
}

