using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using MarketAssistant.Applications.Assets.Models;
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
    /// 资产项点击事件
    /// </summary>
    private void OnStockItemTapped(object? sender, TappedEventArgs e)
    {
        ActivateAsset(sender);
    }

    private void OnStockItemKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            ActivateAsset(sender);
            e.Handled = true;
        }
    }

    private void ActivateAsset(object? sender)
    {
        if (sender is Border border &&
            border.Tag is AssetInfo asset &&
            DataContext is FavoritesPageViewModel viewModel)
        {
            viewModel.SelectFavoriteAssetCommand?.Execute(asset);
        }
    }

    /// <summary>
    /// 表格视图行双击：进入标的详情（与卡片单击行为一致）。
    /// </summary>
    private void OnGridRowDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is DataGrid grid &&
            grid.SelectedItem is AssetInfo asset &&
            DataContext is FavoritesPageViewModel viewModel)
        {
            viewModel.SelectFavoriteAssetCommand?.Execute(asset);
        }
    }
}