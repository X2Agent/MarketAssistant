using Avalonia.Controls;
using Avalonia.Input;
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
    /// 表格视图行双击：进入标的详情（与卡片激活行为一致）。
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