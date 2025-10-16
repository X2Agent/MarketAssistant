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
    /// 热门股票卡片点击事件
    /// </summary>
    private void HotStockCard_Tapped(object? sender, RoutedEventArgs e)
    {
        // 如果点击的是 Button 或其子元素，不处理
        if (e.Source is Button || IsDescendantOf(e.Source as Control, typeof(Button)))
        {
            return;
        }

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
        // 如果点击的是 Button 或其子元素，不处理
        if (e.Source is Button || IsDescendantOf(e.Source as Control, typeof(Button)))
        {
            return;
        }

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

    /// <summary>
    /// 检查控件是否是指定类型的后代
    /// </summary>
    private bool IsDescendantOf(Control? control, Type ancestorType)
    {
        while (control != null)
        {
            if (ancestorType.IsInstanceOfType(control))
            {
                return true;
            }
            control = control.Parent as Control;
        }
        return false;
    }
}