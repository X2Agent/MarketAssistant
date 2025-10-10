using Avalonia.Controls;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

/// <summary>
/// 代理分析页面视图
/// </summary>
public partial class AgentAnalysisPageView : UserControl
{
    public AgentAnalysisPageView()
    {
        InitializeComponent();
        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AgentAnalysisViewModel viewModel)
        {
            await viewModel.LoadAnalysisDataAsync();
        }
    }
}

