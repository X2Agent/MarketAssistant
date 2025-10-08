using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MarketAssistant.Avalonia.ViewModels;

namespace MarketAssistant.Avalonia.Views;

/// <summary>
/// 代理分析页面视图
/// </summary>
public partial class AgentAnalysisPageView : UserControl
{
    private readonly ChatSidebarViewModel _chatSidebarViewModel;

    public AgentAnalysisPageView()
    {
        InitializeComponent();
    }

    public AgentAnalysisPageView(AgentAnalysisViewModel viewModel, ChatSidebarViewModel chatSidebarViewModel) : this()
    {
        _chatSidebarViewModel = chatSidebarViewModel;
        
        viewModel.ChatSidebarViewModel = _chatSidebarViewModel;
        
        _chatSidebarViewModel.InitializeEmpty();
        
        DataContext = viewModel;

        Loaded += OnPageLoaded;
    }

    private async void OnPageLoaded(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is AgentAnalysisViewModel viewModel)
        {
            await viewModel.LoadAnalysisDataAsync();
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}

