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

    public ChatSidebarView()
    {
        InitializeComponent();

        // 监听 DataContext 变化以订阅集合变更事件
        DataContextChanged += OnDataContextChanged;

        // 监听输入框按键
        MessageEntry.KeyDown += MessageEntry_KeyDown;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ChatSidebarViewModel vm)
        {
            // 订阅新 ViewModel 的事件
            vm.ChatMessages.CollectionChanged += ChatMessages_CollectionChanged;
        }
    }

    private void ChatMessages_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 仅当有新消息添加时滚动到底部
        if (e.Action == NotifyCollectionChangedAction.Add)
        {
            Dispatcher.UIThread.Post(() =>
            {
                // 滚动到最后一项
                if (ChatListBox.ItemCount > 0)
                {
                    ChatListBox.ScrollIntoView(ChatListBox.ItemCount - 1);
                }
            });
        }
    }

    private void MessageEntry_KeyDown(object? sender, KeyEventArgs e)
    {
        // Enter 发送，Shift+Enter 换行
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

