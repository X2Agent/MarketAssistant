using Avalonia.Controls;
using Avalonia.Interactivity;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

public partial class AssetSelectionPageView : UserControl
{
    public AssetSelectionPageView()
    {
        InitializeComponent();

        // 处理选股模式选择和快速策略选择的点击事件
        AddHandler(Border.TappedEvent, OnBorderTapped, RoutingStrategies.Bubble);
    }

    private void OnBorderTapped(object? sender, RoutedEventArgs e)
    {
        if (e.Source is not Control control || DataContext is not AssetSelectionPageViewModel viewModel)
            return;

        // 处理选股模式卡片点击
        if (control.DataContext is SelectionModeItem mode)
        {
            viewModel.SelectModeCommand.Execute(mode);
            e.Handled = true;
        }
        // 处理快速策略卡片点击
        else if (control.DataContext is QuickSelectionStrategyInfo strategy)
        {
            viewModel.ExecuteQuickSelectionCommand.Execute(strategy);
            e.Handled = true;
        }
    }
}



