using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using Microsoft.Extensions.Logging;
using MarketAssistant.Services.Dialog;

namespace MarketAssistant.Applications.Stocks;

/// <summary>
/// 主页股票相关服务实现
/// </summary>
public class HomeStockService : IHomeStockService
{
    private readonly StockService _stockService;
    private readonly StockSearchHistory _searchHistory;
    private readonly StockFavoriteService _favoriteService;
    private readonly IDialogService _dialogService;
    private readonly ILogger<HomeStockService> _logger;

    public HomeStockService(
        StockService stockService,
        StockSearchHistory searchHistory,
        StockFavoriteService favoriteService,
        IDialogService dialogService,
        ILogger<HomeStockService> logger)
    {
        _stockService = stockService;
        _searchHistory = searchHistory;
        _favoriteService = favoriteService;
        _dialogService = dialogService;
        _logger = logger;
    }

    /// <summary>
    /// 搜索股票
    /// </summary>
    public async Task<List<StockItem>> SearchStockAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<StockItem>();

        try
        {
            var results = await _stockService.SearchStockAsync(query, cancellationToken);
            return results.Select(stock => new StockItem { Name = stock.Name, Code = stock.Code }).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "搜索股票时出错，查询：{Query}", query);
            return new List<StockItem>();
        }
    }

    /// <summary>
    /// 获取热门股票
    /// </summary>
    public async Task<List<HotStock>> GetHotStocksAsync()
    {
        try
        {
            return await _stockService.GetHotStocksAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取热门股票时出错");
            return new List<HotStock>();
        }
    }

    /// <summary>
    /// 获取最近查看的股票
    /// </summary>
    public List<StockItem> GetRecentStocks()
    {
        try
        {
            return _searchHistory.GetSearchHistory();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取最近查看股票时出错");
            return new List<StockItem>();
        }
    }

    /// <summary>
    /// 添加到最近查看
    /// </summary>
    public void AddToRecentStocks(StockItem stock)
    {
        try
        {
            _searchHistory.AddSearchHistory(stock);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "添加到最近查看时出错，股票：{StockName}", stock?.Name);
        }
    }

    /// <summary>
    /// 添加到收藏
    /// </summary>
    public async Task<bool> AddToFavoriteAsync(object stockParameter)
    {
        try
        {
            string stockName = "";
            string code = "";
            string market = "";

            if (stockParameter is HotStock hotStock)
            {
                stockName = hotStock.Name;
                code = hotStock.Code;
                market = hotStock.Market;
            }
            else if (stockParameter is StockItem stockItem)
            {
                stockName = stockItem.Name;
                code = stockItem.Code;

                // 尝试从股票代码中提取市场代码
                if (code.StartsWith("sh") || code.StartsWith("sz"))
                {
                    market = code.Substring(0, 2).ToUpper();
                    code = code.Substring(2);
                }
            }
            else
            {
                return false;
            }

            // 显示确认对话框
            bool confirmed = await _dialogService.ShowConfirmationAsync(
                "添加收藏", 
                $"确定要将 {stockName} 添加到收藏列表吗？", 
                "确认", 
                "取消");

            if (confirmed)
            {
                _favoriteService.AddFavorite(code, market);
                await _dialogService.ShowMessageAsync("收藏成功", $"已将 {stockName} 添加到收藏列表");
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "添加收藏时出错");
            await _dialogService.ShowMessageAsync("收藏失败", "添加收藏时发生错误，请稍后重试");
            return false;
        }
    }
}

