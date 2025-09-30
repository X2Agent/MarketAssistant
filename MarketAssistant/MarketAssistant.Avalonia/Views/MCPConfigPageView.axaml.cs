using Avalonia.Controls;
using Avalonia.Interactivity;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Avalonia.ViewModels;

namespace MarketAssistant.Avalonia.Views;

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
        if (sender is Border border && 
            border.Tag is MCPServerConfig config && 
            DataContext is MCPConfigPageViewModel viewModel)
        {
            viewModel.SelectedConfig = config;
        }
    }
}
