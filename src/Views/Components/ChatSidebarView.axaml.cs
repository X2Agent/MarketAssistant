using Avalonia;
using Avalonia.Controls;
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
    }
}

