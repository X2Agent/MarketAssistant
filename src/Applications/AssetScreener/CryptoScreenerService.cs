using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.Services.Data;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.AssetScreener;

/// <summary>
/// 虚拟币筛选服务实现（基于CoinGecko免费API + 币安API补充）
/// </summary>
public sealed class CryptoScreenerService : IAssetScreenerService
{
    private readonly ILogger<CryptoScreenerService> _logger;
    private readonly CoinGeckoApiService _coinGeckoService;
    private readonly BinanceMarketDataService _binanceService;

    // CoinGecko API 限制
    private const int COINGECKO_MAX_PER_PAGE = 250;
    private const int COINGECKO_DEFAULT_PAGE_SIZE = 100;

    public CryptoScreenerService(
        ILogger<CryptoScreenerService> logger,
        CoinGeckoApiService coinGeckoService,
        BinanceMarketDataService binanceService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _coinGeckoService = coinGeckoService ?? throw new ArgumentNullException(nameof(coinGeckoService));
        _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
    }

    /// <summary>
    /// 根据筛选条件筛选虚拟币
    /// </summary>
    public async Task<List<ScreenerStockInfo>> ScreenAsync(object criteria)
    {
        if (criteria is not CryptoCriteria cryptoCriteria)
        {
            throw new ArgumentException("筛选条件类型错误，期望 CryptoCriteria", nameof(criteria));
        }

        _logger.LogInformation("开始虚拟币筛选，条件数量: {Count}, 限制: {Limit}",
            cryptoCriteria.Criteria.Count, cryptoCriteria.Limit);

        try
        {
            // 1. 从CoinGecko获取基础数据
            var markets = await FetchFromCoinGeckoAsync(cryptoCriteria);

            // 2. 应用筛选条件
            var filtered = ApplyFilters(markets, cryptoCriteria);

            // 3. 限制结果数量
            var limited = filtered.Take(cryptoCriteria.Limit).ToList();

            // 4. 转换为ScreenerStockInfo格式
            var results = ConvertToScreenerInfo(limited);

            _logger.LogInformation("虚拟币筛选完成，结果数量: {Count}", results.Count);

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "虚拟币筛选过程中发生错误");
            throw new FriendlyException("虚拟币筛选失败，请稍后重试", ex);
        }
    }

    /// <summary>
    /// 从CoinGecko获取数据
    /// </summary>
    private async Task<List<CoinGeckoMarket>> FetchFromCoinGeckoAsync(CryptoCriteria criteria)
    {
        // 确定排序方式
        var order = DetermineOrder(criteria);

        // 确定分页参数（用于市值排名筛选）
        var (page, perPage) = DeterminePagination(criteria);

        // 确定类别
        var category = GetCategoryFilter(criteria);

        // 确定需要的价格变化时间段
        var priceChangePercentage = HasPriceChangeFilter(criteria) ? "7d,30d" : null;

        _logger.LogDebug("CoinGecko查询参数 - Order: {Order}, Page: {Page}, PerPage: {PerPage}, Category: {Category}",
            order, page, perPage, category);

        // 调用CoinGecko API
        var markets = await _coinGeckoService.GetCoinsMarketsAsync(
            vsCurrency: "usd",
            category: category,
            order: order,
            perPage: perPage,
            page: page,
            priceChangePercentage: priceChangePercentage);

        return markets;
    }

    /// <summary>
    /// 应用本地筛选条件
    /// </summary>
    private List<CoinGeckoMarket> ApplyFilters(List<CoinGeckoMarket> markets, CryptoCriteria criteria)
    {
        var filtered = markets.AsEnumerable();

        foreach (var condition in criteria.Criteria)
        {
            filtered = condition.Code.ToLowerInvariant() switch
            {
                "market_cap" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.Market_Cap >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.Market_Cap <= condition.MaxValue)),

                "market_cap_rank" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.Market_Cap_Rank >= (int)condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.Market_Cap_Rank <= (int)condition.MaxValue)),

                "volume_24h" or "total_volume" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.Total_Volume >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.Total_Volume <= condition.MaxValue)),

                "price_change_24h" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.Price_Change_Percentage_24h >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.Price_Change_Percentage_24h <= condition.MaxValue)),

                "price_change_7d" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.Price_Change_Percentage_7d_In_Currency >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.Price_Change_Percentage_7d_In_Currency <= condition.MaxValue)),

                "current_price" or "price" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.Current_Price >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.Current_Price <= condition.MaxValue)),

                _ => filtered
            };
        }

        return filtered.ToList();
    }

    /// <summary>
    /// 转换为通用筛选结果格式
    /// </summary>
    private List<ScreenerStockInfo> ConvertToScreenerInfo(List<CoinGeckoMarket> markets)
    {
        return markets.Select(m => new ScreenerStockInfo
        {
            Name = m.Name,
            Symbol = m.Symbol.ToUpperInvariant(),
            Current = m.Current_Price ?? 0,
            Pct = m.Price_Change_Percentage_24h ?? 0,
            Amount = m.Total_Volume ?? 0,
            Mc = m.Market_Cap ?? 0,
            Fmc = m.Fully_Diluted_Valuation ?? 0,
            Volume = m.Total_Volume ?? 0,
            VolumeRatio = 0, // CoinGecko不提供量比
            Tr = 0, // CoinGecko不提供换手率

            // 虚拟币不支持的股票指标，全部填0
            PeTtm = 0,
            PeLyr = 0,
            Pb = 0,
            Psr = 0,
            RoeDiluted = 0,
            Bps = 0,
            Eps = 0,
            NetProfit = 0,
            TotalRevenue = 0,
            DyL = 0,
            Npay = 0,
            Oiy = 0,
            Niota = 0,

            // 历史涨跌幅
            Pct5 = m.Price_Change_Percentage_7d_In_Currency ?? 0,
            Pct10 = 0,
            Pct20 = m.Price_Change_Percentage_30d_In_Currency ?? 0,
            Pct60 = 0,
            Pct120 = 0,
            Pct250 = 0, // CoinGecko免费API不提供1年数据

            // 关注度数据
            Follow = 0,
            Tweet = 0,
            Deal = 0,
            Follow7d = 0,
            Tweet7d = 0,
            Deal7d = 0,
            Follow7dPct = 0,
            Tweet7dPct = 0,
            Deal7dPct = 0
        }).ToList();
    }

    #region 辅助方法

    private string DetermineOrder(CryptoCriteria criteria)
    {
        // 检查是否有交易量筛选 -> 按交易量排序
        if (criteria.Criteria.Any(c => c.Code.Contains("volume", StringComparison.OrdinalIgnoreCase)))
        {
            return "volume_desc";
        }

        // 检查是否有价格变化筛选 -> 按市值排序（更稳定）
        if (criteria.Criteria.Any(c => c.Code.Contains("price_change", StringComparison.OrdinalIgnoreCase)))
        {
            return "market_cap_desc";
        }

        // 默认按市值排序
        return "market_cap_desc";
    }

    private (int page, int perPage) DeterminePagination(CryptoCriteria criteria)
    {
        var rankFilter = criteria.Criteria.FirstOrDefault(c =>
            c.Code.Equals("market_cap_rank", StringComparison.OrdinalIgnoreCase));

        if (rankFilter != null)
        {
            var minRank = (int)(rankFilter.MinValue ?? 1);
            var maxRank = (int)(rankFilter.MaxValue ?? COINGECKO_DEFAULT_PAGE_SIZE);

            // 计算需要的页数
            var page = (minRank - 1) / COINGECKO_DEFAULT_PAGE_SIZE + 1;
            var perPage = Math.Min(maxRank - minRank + 1, COINGECKO_MAX_PER_PAGE);

            return (page, perPage);
        }

        return (1, Math.Min(criteria.Limit, COINGECKO_MAX_PER_PAGE));
    }

    private string? GetCategoryFilter(CryptoCriteria criteria)
    {
        var categoryFilter = criteria.Criteria.FirstOrDefault(c =>
            c.Code.Equals("category", StringComparison.OrdinalIgnoreCase));

        return categoryFilter?.MinValue?.ToString(); // 使用MinValue存储类别名称
    }

    private bool HasPriceChangeFilter(CryptoCriteria criteria)
    {
        return criteria.Criteria.Any(c =>
            c.Code.Contains("price_change_7d", StringComparison.OrdinalIgnoreCase) ||
            c.Code.Contains("price_change_30d", StringComparison.OrdinalIgnoreCase));
    }

    #endregion
}

