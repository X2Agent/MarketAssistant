using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using System.Collections.ObjectModel;

namespace MarketAssistant.Avalonia.ViewModels;

/// <summary>
/// 收藏页ViewModel - 对应 FavoritesViewModel
/// </summary>
public partial class FavoritesPageViewModel : ViewModelBase, IRecipient<StockFavoritesChanged>
{
    private readonly StockFavoriteService? _favoriteService;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<StockInfo> Stocks { get; set; } = new ObservableCollection<StockInfo>();

    public FavoritesPageViewModel()
    {
        // 无参构造函数用于设计时
    }

    public FavoritesPageViewModel(StockFavoriteService favoriteService)
    {
        _favoriteService = favoriteService;
        LoadFavoriteStocksAsync();
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 加载收藏股票列表
    /// </summary>
    private async void LoadFavoriteStocksAsync()
    {
        if (_favoriteService == null) return;

        IsLoading = true;
        try
        {
            var favorites = await _favoriteService.GetFavoritesWithLatestDataAsync(CancellationToken.None);

            Stocks.Clear();
            foreach (var stock in favorites)
            {
                Stocks.Add(stock);
            }
        }
        catch (Exception)
        {
            // 忽略加载错误
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// 选择收藏股票
    /// </summary>
    [RelayCommand]
    private void SelectFavoriteStock(StockInfo? stock)
    {
        if (stock == null) return;

        // TODO: Avalonia 导航到股票详情页
        // 需要实现导航逻辑
    }

    /// <summary>
    /// 移除收藏股票
    /// </summary>
    [RelayCommand]
    private async Task RemoveFavorite(StockInfo? stock)
    {
        if (stock == null || _favoriteService == null) return;

        try
        {
            // TODO: Avalonia 显示确认对话框
            // 暂时直接删除
            _favoriteService.RemoveFavorite(stock.Code, stock.Market);
            Stocks.Remove(stock);
        }
        catch (Exception)
        {
            // 忽略删除错误
        }
    }

    /// <summary>
    /// 接收收藏变更消息
    /// </summary>
    public void Receive(StockFavoritesChanged message)
    {
        LoadFavoriteStocksAsync();
    }
}
