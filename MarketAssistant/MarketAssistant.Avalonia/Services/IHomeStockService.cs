using MarketAssistant.Applications.Stocks.Models;

namespace MarketAssistant.Services;

/// <summary>
/// 主页股票相关服务接口
/// </summary>
public interface IHomeStockService
{
    /// <summary>
    /// 搜索股票
    /// </summary>
    Task<List<StockItem>> SearchStockAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取热门股票
    /// </summary>
    Task<List<HotStock>> GetHotStocksAsync();

    /// <summary>
    /// 获取最近查看的股票
    /// </summary>
    List<StockItem> GetRecentStocks();

    /// <summary>
    /// 添加到最近查看
    /// </summary>
    void AddToRecentStocks(StockItem stock);

    /// <summary>
    /// 添加到收藏
    /// </summary>
    Task<bool> AddToFavoriteAsync(object stockParameter);
}
