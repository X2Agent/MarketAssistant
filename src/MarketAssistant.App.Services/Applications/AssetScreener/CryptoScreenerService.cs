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
    /// 优先使用 CoinGecko（含市值/排名/供应量数据），失败时降级到 Binance（仅价格/成交量/涨跌幅）
    /// </summary>
    public async Task<List<ScreenerAssetInfo>> ScreenAsync(object criteria)
    {
        if (criteria is not CryptoCriteria cryptoCriteria)
        {
            throw new ArgumentException("筛选条件类型错误，期望 CryptoCriteria", nameof(criteria));
        }

        _logger.LogInformation("开始虚拟币筛选，条件数量: {Count}, 限制: {Limit}",
            cryptoCriteria.Criteria.Count, cryptoCriteria.Limit);

        try
        {
            // 1. 优先从 CoinGecko 获取数据（含市值、排名、供应量）
            List<ScreenerAssetInfo> results;
            try
            {
                var markets = await FetchFromCoinGeckoAsync(cryptoCriteria);
                var filtered = ApplyFilters(markets, cryptoCriteria);
                var limited = filtered.Take(cryptoCriteria.Limit).ToList();
                results = ConvertToScreenerInfo(limited);
                _logger.LogInformation("CoinGecko 筛选完成，结果数量: {Count}", results.Count);
            }
            catch (Exception ex) when (ex is not ArgumentException)
            {
                _logger.LogWarning(ex, "CoinGecko 数据源不可用，降级到 Binance 兜底");
                results = await FetchFromBinanceFallbackAsync(cryptoCriteria);
                _logger.LogInformation("Binance 兜底筛选完成，结果数量: {Count}", results.Count);
            }

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
                    m.Market_Cap_Rank.HasValue &&
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

                "price_change_30d" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.Price_Change_Percentage_30d_In_Currency >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.Price_Change_Percentage_30d_In_Currency <= condition.MaxValue)),

                "current_price" or "price" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.Current_Price >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.Current_Price <= condition.MaxValue)),

                _ => filtered
            };
        }

        return filtered.ToList();
    }

    private List<ScreenerAssetInfo> ConvertToScreenerInfo(List<CoinGeckoMarket> markets)
    {
        return markets.Select(m => (ScreenerAssetInfo)new ScreenerCryptoInfo
        {
            Name = m.Name,
            Symbol = m.Symbol.ToUpperInvariant(),
            Current = m.Current_Price ?? 0,
            Pct = m.Price_Change_Percentage_24h ?? 0,
            Amount = m.Total_Volume ?? 0,
            Mc = m.Market_Cap ?? 0,
            Fmc = m.Fully_Diluted_Valuation ?? 0,
            Volume = m.Total_Volume ?? 0,
            MarketCapRank = m.Market_Cap_Rank ?? 0,
            PriceChange7d = m.Price_Change_Percentage_7d_In_Currency ?? 0,
            PriceChange30d = m.Price_Change_Percentage_30d_In_Currency ?? 0,
            CirculatingSupply = m.Circulating_Supply ?? 0,
            TotalSupply = m.Total_Supply ?? 0,
            MaxSupply = m.Max_Supply
        }).ToList();
    }

    /// <summary>
    /// Binance 兜底筛选：当 CoinGecko 不可用时，从币安获取 USDT 交易对行情
    /// 注意：Binance 不提供市值/排名/供应量数据，相关筛选条件将被忽略
    /// </summary>
    private async Task<List<ScreenerAssetInfo>> FetchFromBinanceFallbackAsync(CryptoCriteria criteria)
    {
        var tickers = await _binanceService.GetAll24hrTickersFullAsync();

        // 仅保留指定计价货币的交易对（默认 USDT），排除杠杆/稳定币交易对
        var quoteCurrency = string.IsNullOrWhiteSpace(criteria.QuoteCurrency) ? "USDT" : criteria.QuoteCurrency;
        var filtered = tickers.Where(t =>
            t.Symbol.EndsWith(quoteCurrency, StringComparison.OrdinalIgnoreCase) &&
            !IsStableCoinPair(t.Symbol)).ToList();

        // 应用 Binance 支持的筛选条件（价格变化、成交量、当前价）
        filtered = ApplyBinanceFilters(filtered, criteria);

        // 按成交额降序排序并限制数量
        var limited = filtered
            .OrderByDescending(t => t.QuoteVolume)
            .Take(criteria.Limit)
            .ToList();

        return ConvertBinanceToScreenerInfo(limited);
    }

    /// <summary>
    /// 应用 Binance 数据支持的筛选条件（仅价格变化、成交量、当前价）
    /// </summary>
    private List<Binance24hrTicker> ApplyBinanceFilters(List<Binance24hrTicker> tickers, CryptoCriteria criteria)
    {
        var filtered = tickers.AsEnumerable();

        foreach (var condition in criteria.Criteria)
        {
            filtered = condition.Code.ToLowerInvariant() switch
            {
                "volume_24h" or "total_volume" => filtered.Where(t =>
                    (!condition.MinValue.HasValue || t.QuoteVolume >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || t.QuoteVolume <= condition.MaxValue)),

                "price_change_24h" => filtered.Where(t =>
                    (!condition.MinValue.HasValue || t.PriceChangePercent >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || t.PriceChangePercent <= condition.MaxValue)),

                "current_price" or "price" => filtered.Where(t =>
                    (!condition.MinValue.HasValue || t.LastPrice >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || t.LastPrice <= condition.MaxValue)),

                // market_cap、market_cap_rank、price_change_7d/30d 等 Binance 不支持，忽略
                _ => filtered
            };
        }

        return filtered.ToList();
    }

    /// <summary>
    /// 判断是否为稳定币交易对（排除 USDCUSDT、DAIUSDT 等）
    /// </summary>
    private static bool IsStableCoinPair(string symbol)
    {
        var stableCoins = new[] { "USDC", "DAI", "TUSD", "BUSD", "FDUSD", "USDP" };
        return stableCoins.Any(sc => symbol.StartsWith(sc + "USDT", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 将 Binance Ticker 转换为 ScreenerCryptoInfo
    /// </summary>
    private List<ScreenerAssetInfo> ConvertBinanceToScreenerInfo(List<Binance24hrTicker> tickers)
    {
        return tickers.Select(t =>
        {
            // BTCUSDT → BTC
            var baseAsset = t.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)
                ? t.Symbol[..^4]
                : t.Symbol;

            return (ScreenerAssetInfo)new ScreenerCryptoInfo
            {
                Name = baseAsset,
                Symbol = baseAsset,
                Current = t.LastPrice,
                Pct = t.PriceChangePercent ?? 0,
                Amount = t.QuoteVolume,
                Mc = 0,            // Binance 不提供市值
                Fmc = 0,
                Volume = t.QuoteVolume,
                MarketCapRank = 0,  // Binance 不提供排名
                PriceChange7d = 0,  // Binance 不提供 7d 数据
                PriceChange30d = 0,
                CirculatingSupply = 0,
                TotalSupply = 0
            };
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

