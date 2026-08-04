using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MarketAssistant.Applications.Settings;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

public partial class MCPConfigPageView : UserControl
{
    public MCPConfigPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 服务器项点击事件
    /// </summary>
    private void OnServerItemTapped(object? sender, RoutedEventArgs e)
    {
        SelectServer(sender);
    }

    private void OnServerItemKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            SelectServer(sender);
            e.Handled = true;
        }
    }

    private void SelectServer(object? sender)
    {
        if (sender is Border border &&
            border.Tag is MCPServerConfig config &&
            DataContext is MCPConfigPageViewModel viewModel)
        {
            viewModel.SelectedConfig = config;
        }
    }
}
