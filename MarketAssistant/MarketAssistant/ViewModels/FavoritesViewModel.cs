using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

public class FavoritesViewModel : ViewModelBase, IRecipient<StockFavoritesChanged>
{
    public ICommand SelectFavoriteStockCommand { get; set; }
    public ICommand RemoveFavoriteCommand { get; set; }
    public ObservableCollection<StockInfo> Stocks { get; set; } = new ObservableCollection<StockInfo>();

    private readonly StockFavoriteService _favoriteService;
    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public FavoritesViewModel(
        ILogger<FavoritesViewModel> logger,
        StockFavoriteService favoriteService) : base(logger)
    {
        _favoriteService = favoriteService;
        SelectFavoriteStockCommand = new Command<FavoriteStock>(OnSelectFavoriteStock);
        RemoveFavoriteCommand = new Command<StockInfo>(OnRemoveFavorite);
        LoadFavoriteStocksAsync();
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 加载收藏股票列表
    /// </summary>
    private async void LoadFavoriteStocksAsync()
    {
        IsLoading = true;
        await SafeExecuteAsync(async () =>
        {
            var favorites = await _favoriteService.GetFavoritesWithLatestDataAsync(CancellationToken.None);

            Stocks.Clear();
            foreach (var stock in favorites)
            {
                Stocks.Add(stock);
            }
        }, "加载收藏股票");
        IsLoading = false;
    }

    private async void OnSelectFavoriteStock(FavoriteStock stock)
    {
        if (stock == null) return;

        await Shell.Current.GoToAsync("stock", new Dictionary<string, object>
        {
            { "code", stock.FullCode }
        });
    }

    /// <summary>
    /// 移除收藏股票
    /// </summary>
    private async void OnRemoveFavorite(StockInfo stock)
    {
        if (stock == null) return;

        await SafeExecuteAsync(async () =>
        {
            bool isConfirmed = await Shell.Current.DisplayAlert(
                "确认删除",
                $"确定要将 {stock.Market}.{stock.Code} 从收藏列表中移除吗？",
                "确定",
                "取消");

            if (isConfirmed)
            {
                _favoriteService.RemoveFavorite(stock.Code, stock.Market);
                Stocks.Remove(stock);
            }
        }, "移除收藏股票");
    }

    public void Receive(StockFavoritesChanged message)
    {
        LoadFavoriteStocksAsync();
    }
}