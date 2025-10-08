using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Services.Dialog;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.Avalonia.ViewModels;

/// <summary>
/// 收藏页ViewModel - 对应 FavoritesViewModel
/// </summary>
public partial class FavoritesPageViewModel : ViewModelBase, IRecipient<StockFavoritesChanged>
{
    private readonly StockFavoriteService _favoriteService;
    private readonly StockService _stockService;
    private readonly StockInfoCache _stockInfoCache;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private bool _isLoading;

    public ObservableCollection<StockInfo> Stocks { get; set; } = new ObservableCollection<StockInfo>();

    /// <summary>
    /// 构造函数（使用依赖注入）
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
        LoadFavoriteStocksAsync();
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 加载收藏股票列表（渐进式加载策略）
    /// </summary>
    private async void LoadFavoriteStocksAsync()
    {
        IsLoading = true;
        try
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
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "加载收藏股票列表时出错");
            IsLoading = false;
        }
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
                    // 先尝试从缓存获取
                    var stockInfo = _stockInfoCache.Get(favorite.Code, favorite.Market);
                    
                    // 如果缓存中没有，则从网络获取
                    if (stockInfo == null)
                    {
                        stockInfo = await _stockService.GetStockInfoAsync(favorite.Code, favorite.Market);
                        // 缓存获取到的数据
                        _stockInfoCache.Set(stockInfo);
                    }
                    
                    // 在UI线程更新对应的股票信息
                    var existingStock = Stocks.FirstOrDefault(s => s.Code == favorite.Code && s.Market == favorite.Market);
                    if (existingStock != null)
                    {
                        var index = Stocks.IndexOf(existingStock);
                        Stocks[index] = stockInfo;
                    }
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

        try
        {
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
                _favoriteService.RemoveFavorite(stock.Code, stock.Market);
                Stocks.Remove(stock);
                Logger?.LogInformation($"已取消收藏股票: {stock.Name}({stock.Code})");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, $"取消收藏股票 {stock.Code} 时出错");
            await _dialogService.ShowMessageAsync("错误", "取消收藏失败，请稍后重试");
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
