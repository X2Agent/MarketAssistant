using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MarketAssistant.Applications.Assets.Models;
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
    /// 搜索结果项点击处理：激活所点资产并导航
    /// </summary>
    private void SearchResultItem_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: AssetItem asset } &&
            DataContext is HomePageViewModel viewModel)
        {
            e.Handled = true;

            // 执行导航（内部会关闭下拉框）
            viewModel.Search.NavigateToAssetCommand.Execute(asset);
        }
    }

    /// <summary>
    /// 搜索框键盘处理：Esc 关闭结果、上下键移动高亮、回车跳转高亮结果。
    /// 输入法组合期间回车由输入法消费，不会进入此处理，避免重复提交。
    /// </summary>
    private void StockSearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not HomePageViewModel viewModel)
        {
            return;
        }

        var search = viewModel.Search;

        switch (e.Key)
        {
            case Key.Escape:
                if (search.IsSearchResultVisible)
                {
                    search.IsSearchResultVisible = false;
                    e.Handled = true;
                }
                break;

            case Key.Down:
                if (search.IsSearchResultVisible && search.MoveSelection(1))
                {
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (search.IsSearchResultVisible && search.MoveSelection(-1))
                {
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                if (search.IsSearchResultVisible &&
                    search.SelectedResult is AssetItem selectedAsset)
                {
                    search.IsSearchResultVisible = false;
                    search.NavigateToAssetCommand.Execute(selectedAsset);
                    e.Handled = true;
                }
                break;
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
    /// 热门资产卡片点击事件
    /// </summary>
    private void HotStockCard_Tapped(object? sender, RoutedEventArgs e)
    {
        ActivateHotAsset(sender);
    }

    private void HotStockCard_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsActivationKey(e))
        {
            ActivateHotAsset(sender);
            e.Handled = true;
        }
    }

    private void ActivateHotAsset(object? sender)
    {
        if (sender is Border border &&
            border.Tag is HotAsset hotAsset &&
            DataContext is HomePageViewModel viewModel)
        {
            viewModel.HotAssets.SelectHotAssetCommand.Execute(hotAsset);
        }
    }

    /// <summary>
    /// 最近查看资产卡片点击事件
    /// </summary>
    private void RecentStockCard_Tapped(object? sender, RoutedEventArgs e)
    {
        ActivateRecentAsset(sender);
    }

    private void RecentStockCard_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsActivationKey(e))
        {
            ActivateRecentAsset(sender);
            e.Handled = true;
        }
    }

    private void ActivateRecentAsset(object? sender)
    {
        if (sender is Border border &&
            border.Tag is AssetItem assetItem &&
            DataContext is HomePageViewModel viewModel)
        {
            viewModel.RecentAssets.SelectRecentAssetCommand.Execute(assetItem);
        }
    }

    /// <summary>
    /// 新闻卡片点击事件
    /// </summary>
    private void NewsCard_Tapped(object? sender, RoutedEventArgs e)
    {
        ActivateNews(sender);
    }

    private void NewsCard_KeyDown(object? sender, KeyEventArgs e)
    {
        if (IsActivationKey(e))
        {
            ActivateNews(sender);
            e.Handled = true;
        }
    }

    private void ActivateNews(object? sender)
    {
        if (sender is Border border &&
            border.Tag is Telegram telegram &&
            DataContext is HomePageViewModel viewModel)
        {
            viewModel.News.OpenNewsCommand.Execute(telegram);
        }
    }

    private static bool IsActivationKey(KeyEventArgs e) => e.Key is Key.Enter or Key.Space;
}
