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
    /// 搜索结果项点击处理
    /// </summary>
    private void SearchResultItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control control &&
            control.Tag is AssetItem selectedAsset &&
            DataContext is HomePageViewModel viewModel)
        {
            // 标记事件已处理，防止列表处理
            e.Handled = true;

            // 关闭下拉框
            viewModel.Search.IsSearchResultVisible = false;

            // 执行导航
            viewModel.Search.NavigateToAssetCommand.Execute(selectedAsset);
        }
    }

    /// <summary>
    /// 搜索框键盘处理：Esc 关闭结果、上下键选择、回车跳转所选结果。
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
                if (TryMoveListSelection(SearchResultsList, 1))
                {
                    e.Handled = true;
                }
                break;

            case Key.Up:
                if (TryMoveListSelection(SearchResultsList, -1))
                {
                    e.Handled = true;
                }
                break;

            case Key.Enter:
                if (search.IsSearchResultVisible &&
                    SearchResultsList.SelectedItem is AssetItem selectedAsset)
                {
                    search.IsSearchResultVisible = false;
                    search.NavigateToAssetCommand.Execute(selectedAsset);
                    e.Handled = true;
                }
                break;
        }
    }

    /// <summary>
    /// 在结果列表中移动选中项，返回是否发生了移动
    /// </summary>
    private static bool TryMoveListSelection(ListBox list, int offset)
    {
        if (list.ItemCount == 0)
        {
            return false;
        }

        var newIndex = Math.Clamp(list.SelectedIndex + offset, 0, list.ItemCount - 1);
        if (newIndex == list.SelectedIndex && list.SelectedIndex >= 0)
        {
            return false;
        }

        list.SelectedIndex = newIndex;
        if (list.SelectedItem is { } item)
        {
            list.ScrollIntoView(item);
        }

        return true;
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
