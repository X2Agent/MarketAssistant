using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

public partial class FavoritesPageView : UserControl
{
    public FavoritesPageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 阻止事件冒泡
    /// </summary>
    private void OnDeleteButtonTapped(object? sender, TappedEventArgs e)
    {
        // 标记事件已处理，阻止冒泡到外层 Border
        e.Handled = true;
    }

    /// <summary>
    /// 股票项点击事件
    /// </summary>
    private void OnStockItemTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border border &&
            border.Tag is StockInfo stock &&
            DataContext is FavoritesPageViewModel viewModel)
        {
            viewModel.SelectFavoriteStockCommand?.Execute(stock);
        }
    }
}