using Avalonia.Controls;
using Avalonia.Input;
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
        if (e.Source is Control control && ActivateCard(control))
        {
            e.Handled = true;
        }
    }

    private void OnCardKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space && sender is Control control && ActivateCard(control))
        {
            e.Handled = true;
        }
    }

    private bool ActivateCard(Control control)
    {
        if (DataContext is not AssetSelectionPageViewModel viewModel)
        {
            return false;
        }

        if (control.DataContext is SelectionModeItem mode)
        {
            viewModel.SelectModeCommand.Execute(mode);
            return true;
        }

        if (control.DataContext is QuickSelectionStrategyInfo strategy)
        {
            viewModel.ExecuteQuickSelectionCommand.Execute(strategy);
            return true;
        }

        return false;
    }
}
