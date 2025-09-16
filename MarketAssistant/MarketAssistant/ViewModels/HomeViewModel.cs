using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.ViewModels.Home;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

public class HomeViewModel : ViewModelBase, IDisposable
{
    /// <summary>
    /// 搜索功能ViewModel
    /// </summary>
    public HomeSearchViewModel Search { get; }

    /// <summary>
    /// 热门股票ViewModel
    /// </summary>
    public HotStocksViewModel HotStocks { get; }

    /// <summary>
    /// 最近查看ViewModel
    /// </summary>
    public RecentStocksViewModel RecentStocks { get; }

    /// <summary>
    /// 新闻快讯ViewModel
    /// </summary>
    public TelegraphNewsViewModel News { get; }

    /// <summary>
    /// 导航到股票详情命令
    /// </summary>
    public ICommand NavigateToStockCommand { get; set; }

    public HomeViewModel(
        HomeSearchViewModel searchViewModel,
        HotStocksViewModel hotStocksViewModel,
        RecentStocksViewModel recentStocksViewModel,
        TelegraphNewsViewModel newsViewModel,
        ILogger<HomeViewModel> logger) : base(logger)
    {
        Search = searchViewModel;
        HotStocks = hotStocksViewModel;
        RecentStocks = recentStocksViewModel;
        News = newsViewModel;

        // 初始化导航命令
        NavigateToStockCommand = new Command<object>(OnNavigateToStock);

        // 订阅子ViewModel事件
        Search.StockSelected += OnStockSelected;
        HotStocks.HotStockSelected += OnHotStockSelected;
        RecentStocks.RecentStockSelected += OnRecentStockSelected;
    }

    /// <summary>
    /// 处理搜索股票选择事件
    /// </summary>
    private async void OnStockSelected(object? sender, StockItem stock)
    {
        await NavigateToStockAsync(stock.Code, stock);
    }

    /// <summary>
    /// 处理热门股票选择事件
    /// </summary>
    private async void OnHotStockSelected(object? sender, HotStock stock)
    {
        var stockCode = $"{stock.Market}{stock.Code}".ToLower();
        var stockItem = new StockItem { Name = stock.Name, Code = stockCode };
        await NavigateToStockAsync(stockCode, stockItem);
    }

    /// <summary>
    /// 处理最近股票选择事件
    /// </summary>
    private async void OnRecentStockSelected(object? sender, StockItem stock)
    {
        await NavigateToStockAsync(stock.Code, stock);
    }

    /// <summary>
    /// 通用导航处理
    /// </summary>
    private async void OnNavigateToStock(object? parameter)
    {
        if (parameter is StockItem stock)
        {
            await NavigateToStockAsync(stock.Code, stock);
        }
        else if (parameter is HotStock hotStock)
        {
            var stockCode = $"{hotStock.Market}{hotStock.Code}".ToLower();
            var stockItem = new StockItem { Name = hotStock.Name, Code = stockCode };
            await NavigateToStockAsync(stockCode, stockItem);
        }
    }

    /// <summary>
    /// 导航到股票详情页
    /// </summary>
    private async Task NavigateToStockAsync(string stockCode, StockItem? stockItem = null)
    {
        await SafeExecuteAsync(async () =>
        {
            // 添加到最近查看（如果有股票信息）
            if (stockItem != null)
            {
                RecentStocks.AddToRecentStocks(stockItem);
            }

            // 清除搜索结果
            Search.ClearSearchResults();

            // 导航到股票详情页
            await Shell.Current.GoToAsync("stock", new Dictionary<string, object>
            {
                { "code", stockCode }
            });
        }, "导航到股票详情页");
    }

    /// <summary>
    /// 启动定时器（兼容性方法）
    /// </summary>
    public void StartTimer()
    {
        News.StartUpdates();
        Logger?.LogInformation("新闻更新服务已启动");
    }

    /// <summary>
    /// 暂停定时器（兼容性方法）
    /// </summary>
    public void StopTimer()
    {
        News.StopUpdates();
        Logger?.LogInformation("新闻更新服务已暂停");
    }

    /// <summary>
    /// 刷新热门股票
    /// </summary>
    public async Task RefreshHotStocksAsync()
    {
        await HotStocks.LoadHotStocksAsync();
    }

    /// <summary>
    /// 刷新最近查看股票
    /// </summary>
    public void RefreshRecentStocks()
    {
        RecentStocks.LoadRecentStocks();
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        // 取消事件订阅
        Search.StockSelected -= OnStockSelected;
        HotStocks.HotStockSelected -= OnHotStockSelected;
        RecentStocks.RecentStockSelected -= OnRecentStockSelected;
        
        // 释放子ViewModel资源
        News.Dispose();
    }
}