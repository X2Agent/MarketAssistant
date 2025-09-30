using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.ViewModels.Home;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Avalonia.ViewModels;

/// <summary>
/// 首页ViewModel
/// </summary>
public class HomePageViewModel : ViewModelBase, IDisposable
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
    /// 临时构造：暂不使用DI，手动创建依赖。后续需要重构为使用DI容器
    /// </summary>
    public HomePageViewModel() : base(null)
    {
        // TODO: 这里应该通过DI注入，暂时简化处理
        // 由于缺少服务实例，这些子ViewModels暂时无法正常工作
        // 需要在Program.cs中配置DI容器后重构此代码
        
        Logger?.LogWarning("HomePageViewModel: 使用临时构造，部分功能可能不可用");
    }
    
    /// <summary>
    /// 完整构造函数（待DI容器配置后使用）
    /// </summary>
    public HomePageViewModel(
        HomeSearchViewModel searchViewModel,
        HotStocksViewModel hotStocksViewModel,
        RecentStocksViewModel recentStocksViewModel,
        TelegraphNewsViewModel newsViewModel,
        ILogger<HomePageViewModel> logger) : base(logger)
    {
        Search = searchViewModel;
        HotStocks = hotStocksViewModel;
        RecentStocks = recentStocksViewModel;
        News = newsViewModel;

        // 订阅子ViewModel事件
        Search.StockSelected += OnStockSelected;
        HotStocks.HotStockSelected += OnHotStockSelected;
        RecentStocks.RecentStockSelected += OnRecentStockSelected;
    }

    /// <summary>
    /// 处理搜索股票选择事件
    /// </summary>
    private void OnStockSelected(object? sender, StockItem stock)
    {
        NavigateToStock(stock.Code, stock);
    }

    /// <summary>
    /// 处理热门股票选择事件
    /// </summary>
    private void OnHotStockSelected(object? sender, HotStock stock)
    {
        var stockCode = $"{stock.Market}{stock.Code}".ToLower();
        var stockItem = new StockItem { Name = stock.Name, Code = stockCode };
        NavigateToStock(stockCode, stockItem);
    }

    /// <summary>
    /// 处理最近股票选择事件
    /// </summary>
    private void OnRecentStockSelected(object? sender, StockItem stock)
    {
        NavigateToStock(stock.Code, stock);
    }

    /// <summary>
    /// 导航到股票详情页
    /// </summary>
    private void NavigateToStock(string stockCode, StockItem? stockItem = null)
    {
        SafeExecute(() =>
        {
            // 添加到最近查看（如果有股票信息）
            if (stockItem != null)
            {
                RecentStocks.AddToRecentStocks(stockItem);
            }

            // 清除搜索结果
            Search.ClearSearchResults();

            // TODO: 实现Avalonia的导航逻辑
            Logger?.LogInformation($"导航到股票详情页: {stockCode}");
        }, "导航到股票详情页");
    }

    /// <summary>
    /// 启动定时器
    /// </summary>
    public void StartTimer()
    {
        News.StartUpdates();
        Logger?.LogInformation("新闻更新服务已启动");
    }

    /// <summary>
    /// 暂停定时器
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
        
        GC.SuppressFinalize(this);
    }
}