using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Applications.Telegrams;
using MarketAssistant.ViewModels;

namespace MarketAssistant.Views.Pages;

public partial class HomePageView : UserControl
{
    public HomePageView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 搜索结果项点击处理
    /// </summary>
    private void SearchResultItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Border border &&
            border.Tag is StockItem selectedStock &&
            DataContext is HomePageViewModel viewModel)
        {
            // 标记事件已处理，防止AutoCompleteBox处理
            e.Handled = true;

            // 关闭下拉框
            viewModel.Search.IsSearchResultVisible = false;

            // 执行导航
            viewModel.Search.SelectStockCommand.Execute(selectedStock);
        }
    }

    /// <summary>
    /// 阻止事件冒泡
    /// </summary>
    private void OnPreventTapped(object? sender, TappedEventArgs e)
    {
        e.Handled = true;
    }

    /// <summary>
    /// 热门股票卡片点击事件
    /// </summary>
    private void HotStockCard_Tapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border &&
            border.Tag is HotStock hotStock &&
            DataContext is HomePageViewModel viewModel)
        {
            viewModel.HotStocks.SelectHotStockCommand.Execute(hotStock);
        }
    }

    /// <summary>
    /// 最近查看股票卡片点击事件
    /// </summary>
    private void RecentStockCard_Tapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border &&
            border.Tag is StockItem stockItem &&
            DataContext is HomePageViewModel viewModel)
        {
            viewModel.RecentStocks.SelectRecentStockCommand.Execute(stockItem);
        }
    }

    /// <summary>
    /// 新闻卡片点击事件
    /// </summary>
    private void NewsCard_Tapped(object? sender, RoutedEventArgs e)
    {
        if (sender is Border border &&
            border.Tag is Telegram telegram &&
            DataContext is HomePageViewModel viewModel)
        {
            viewModel.News.OpenNewsCommand.Execute(telegram);
        }
    }
}