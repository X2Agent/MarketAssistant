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
    private readonly StockService _stockService;
    private bool _isLoading;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public FavoritesViewModel(
        ILogger<FavoritesViewModel> logger,
        StockFavoriteService favoriteService,
        StockService stockService) : base(logger)
    {
        _favoriteService = favoriteService;
        _stockService = stockService;
        SelectFavoriteStockCommand = new Command<FavoriteStock>(OnSelectFavoriteStock);
        RemoveFavoriteCommand = new Command<StockInfo>(OnRemoveFavorite);
        LoadFavoriteStocksAsync();
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 加载收藏股票列表（渐进式加载策略）
    /// </summary>
    private async void LoadFavoriteStocksAsync()
    {
        IsLoading = true;
        await SafeExecuteAsync(async () =>
        {
            // 第一步：快速显示收藏列表（仅基本信息）
            var favoritesCodes = _favoriteService.GetFavoritesCodes();
            
            Stocks.Clear();
            foreach (var favorite in favoritesCodes)
            {
                Stocks.Add(new StockInfo 
                { 
                    Code = favorite.Code, 
                    Market = favorite.Market, 
                    Name = $"{favorite.Market}.{favorite.Code}",
                    CurrentPrice = string.Empty,
                    ChangePercentage = string.Empty
                });
            }
            
            IsLoading = false;

            // 第二步：在后台逐个更新实时数据（限制并发数）
            await UpdateStockDataProgressivelyAsync(favoritesCodes);
        }, "加载收藏股票");
    }

    /// <summary>
    /// 渐进式更新股票实时数据（限制并发数，避免同时打开过多浏览器页面）
    /// </summary>
    private async Task UpdateStockDataProgressivelyAsync(List<FavoriteStock> favorites)
    {
        const int maxConcurrency = 3; // 最多同时请求3个股票数据
        var semaphore = new SemaphoreSlim(maxConcurrency);
        var tasks = new List<Task>();

        foreach (var favorite in favorites)
        {
            var task = Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var stockInfo = await _stockService.GetStockInfoAsync(favorite.Code, favorite.Market);
                    
                    // 在UI线程更新对应的股票信息
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        var existingStock = Stocks.FirstOrDefault(s => s.Code == favorite.Code && s.Market == favorite.Market);
                        if (existingStock != null)
                        {
                            var index = Stocks.IndexOf(existingStock);
                            Stocks[index] = stockInfo;
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, $"更新股票 {favorite.Code} 数据时出错");
                }
                finally
                {
                    semaphore.Release();
                }
            });

            tasks.Add(task);
        }

        await Task.WhenAll(tasks);
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