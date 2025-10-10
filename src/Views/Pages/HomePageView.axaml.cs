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
    /// 搜索框按键处理（回车搜索）
    /// </summary>
    private void StockSearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is HomePageViewModel viewModel)
        {
            // 如果有选中的项目，直接选择
            if (StockSearchBox.SelectedItem is StockItem selectedStock)
            {
                viewModel.Search.SelectStockCommand.Execute(selectedStock);
                e.Handled = true;
                return;
            }

            // 如果没有选中项但有搜索结果，选择第一个结果
            if (viewModel.Search.SearchResults.Count > 0)
            {
                var firstStock = viewModel.Search.SearchResults[0];
                viewModel.Search.SelectStockCommand.Execute(firstStock);
                e.Handled = true;
                return;
            }

            // 如果有输入但没有结果，可以提示用户
            if (!string.IsNullOrWhiteSpace(viewModel.Search.SearchQuery))
            {
                // 可以在这里添加提示逻辑
                e.Handled = true;
            }
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