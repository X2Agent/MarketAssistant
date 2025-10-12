using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Services.Dialog;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 收藏页ViewModel - 对应 FavoritesViewModel
/// </summary>
public partial class FavoritesPageViewModel : ViewModelBase, IRecipient<StockFavoritesChanged>
{
    private readonly StockFavoriteService _favoriteService;
    private readonly StockService _stockService;
    private readonly StockInfoCache _stockInfoCache;
    private readonly IDialogService _dialogService;

    public ObservableCollection<StockInfo> Stocks { get; set; } = new ObservableCollection<StockInfo>();

    /// <summary>
    /// 构造函数
    /// </summary>
    public FavoritesPageViewModel(
        StockFavoriteService favoriteService, 
        StockService stockService,
        StockInfoCache stockInfoCache,
        IDialogService dialogService,
        ILogger<FavoritesPageViewModel> logger) 
        : base(logger)
    {
        _favoriteService = favoriteService;
        _stockService = stockService;
        _stockInfoCache = stockInfoCache;
        _dialogService = dialogService;
        _ = LoadFavoriteStocksAsync();
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 加载收藏股票列表
    /// </summary>
    private async Task LoadFavoriteStocksAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            // 获取收藏列表
            var favoritesCodes = _favoriteService.GetFavoritesCodes();
            
            Stocks.Clear();

            // 使用并发加载所有股票数据
            await UpdateStockDataProgressivelyAsync(favoritesCodes);
        }, "加载收藏列表");
    }

    /// <summary>
    /// 渐进式加载股票实时数据（限制并发数，避免同时打开过多浏览器页面）
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
                    // 先尝试从缓存获取
                    var stockInfo = _stockInfoCache.Get(favorite.Code, favorite.Market);
                    
                    // 如果缓存中没有，则从网络获取
                    if (stockInfo == null)
                    {
                        stockInfo = await _stockService.GetStockInfoAsync(favorite.Code, favorite.Market);
                        // 缓存获取到的数据
                        _stockInfoCache.Set(stockInfo);
                    }
                    
                    // 添加到列表中
                    Stocks.Add(stockInfo);
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, $"加载股票 {favorite.Code} 数据时出错");
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
        if (stock == null) return;

        // 显示确认对话框
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "取消收藏",
            $"确定要取消收藏 {stock.Name}({stock.Code}) 吗？",
            "确定",
            "取消"
        );

        // 用户确认后才执行删除
        if (confirmed)
        {
            await SafeExecuteAsync(async () =>
            {
                _favoriteService.RemoveFavorite(stock.Code, stock.Market);
                Stocks.Remove(stock);
                Logger?.LogInformation($"已取消收藏股票: {stock.Name}({stock.Code})");
                await Task.CompletedTask;
            }, "取消收藏");
        }
    }

    /// <summary>
    /// 接收收藏变更消息
    /// </summary>
    public void Receive(StockFavoritesChanged message)
    {
        _ = LoadFavoriteStocksAsync();
    }
}
