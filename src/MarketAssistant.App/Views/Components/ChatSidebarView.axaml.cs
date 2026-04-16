using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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

    private ChatSidebarViewModel? _subscribedViewModel;

    public ChatSidebarView()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        MessageEntry.KeyDown += MessageEntry_KeyDown;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
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
        SubscribeToViewModel(DataContext as ChatSidebarViewModel);
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

    private void ChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (ChatListBox.ItemCount > 0)
                {
                    ChatListBox.ScrollIntoView(ChatListBox.ItemCount - 1);
                }
            });
        }
    }

    private void MessageEntry_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            if (DataContext is ChatSidebarViewModel vm && !string.IsNullOrWhiteSpace(vm.UserInput))
            {
                if (vm.SendMessageCommand.CanExecute(null))
                {
                    vm.SendMessageCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }
    }
}

