using Avalonia.Controls;
using Avalonia.Interactivity;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Avalonia.ViewModels;

namespace MarketAssistant.Avalonia.Views;

public partial class FavoritesPageView : UserControl
{
    public FavoritesPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 股票项点击事件
    /// </summary>
    private void OnStockItemTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border && 
            border.Tag is StockInfo stock && 
            DataContext is FavoritesPageViewModel viewModel)
        {
            viewModel.SelectFavoriteStockCommand?.Execute(stock);
        }
    }
}