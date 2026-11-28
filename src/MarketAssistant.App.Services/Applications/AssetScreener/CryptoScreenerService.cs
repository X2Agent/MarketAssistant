using MarketAssistant.Applications.AssetScreener.Models;
using MarketAssistant.DataProviders;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.AssetScreener;

/// <summary>
/// 虚拟币筛选服务实现（基于CoinGecko免费API + 币安API补充）
/// </summary>
public sealed class CryptoScreenerService(
    ILogger<CryptoScreenerService> logger,
    CoinGeckoApiService coinGeckoService,
    BinanceMarketDataService binanceService) : IAssetScreenerService
{
    private readonly ILogger<CryptoScreenerService> _logger = logger;
    private readonly CoinGeckoApiService _coinGeckoService = coinGeckoService;
    private readonly BinanceMarketDataService _binanceService = binanceService;

    // CoinGecko API 限制
    private const int COINGECKO_MAX_PER_PAGE = 250;

    /// <summary>
    /// 根据筛选条件筛选虚拟币。
    /// 优先使用 CoinGecko（含市值/排名/供应量数据）并限定为币安有 USDT 现货交易对的币；
    /// CoinGecko 不可用时降级到 Binance（仅价格/成交额/24h 涨跌幅，遇到无法执行的条件直接失败）。
    /// </summary>
    public async Task<List<ScreenerAssetInfo>> ScreenAsync(object criteria, CancellationToken cancellationToken = default)
    {
        if (criteria is not CryptoCriteria cryptoCriteria)
        {
            throw new ArgumentException("筛选条件类型错误，期望 CryptoCriteria", nameof(criteria));
        }

        _logger.LogInformation("开始虚拟币筛选，条件数量: {Count}, 候选池: {Limit}",
            cryptoCriteria.Criteria.Count, cryptoCriteria.Limit);

        try
        {
            List<CoinGeckoMarket> markets;
            try
            {
                markets = await FetchFromCoinGeckoAsync(cryptoCriteria, cancellationToken);
            }
            catch (Exception ex) when (ex is not ArgumentException and not OperationCanceledException)
            {
                _logger.LogWarning(ex, "CoinGecko 数据源不可用，降级到 Binance 兜底");
                var fallbackResults = await FetchFromBinanceFallbackAsync(cryptoCriteria, cancellationToken);
                _logger.LogInformation("Binance 兜底筛选完成，结果数量: {Count}", fallbackResults.Count);
                return fallbackResults;
            }

            // 候选限定为币安有 USDT 现货交易对的币：详情页、自选与交易都走币安，筛出无交易对的币用户无法继续操作。
            // exchangeInfo 有 1 小时缓存；获取失败直接抛出，不降级为"不过滤"，否则会放行无法交易的币。
            var exchangeSymbols = (await _binanceService.GetExchangeInfoAsync(cancellationToken))?.Symbols
                ?? throw new FriendlyException("获取币安交易对信息失败，无法确认候选币种可交易，请稍后重试");
            var tradableBaseAssets = exchangeSymbols
                .Where(s => s.QuoteAsset == "USDT")
                .Select(s => s.BaseAsset)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var candidates = ApplyFilters(markets, cryptoCriteria)
                .Where(m => tradableBaseAssets.Contains(m.Symbol))
                .Take(cryptoCriteria.Limit)
                .ToList();
            _logger.LogInformation("CoinGecko 筛选完成，结果数量: {Count}", candidates.Count);
            return ConvertToScreenerInfo(candidates);
        }
        // 用户取消与已带用户友好信息的 FriendlyException 原样上抛，不二次包装
        catch (Exception ex) when (ex is not (OperationCanceledException or FriendlyException))
        {
            _logger.LogError(ex, "虚拟币筛选过程中发生错误");
            throw new FriendlyException("虚拟币筛选失败，请稍后重试", ex);
        }
    }

    /// <summary>
    /// 从CoinGecko获取数据
    /// </summary>
    private async Task<List<CoinGeckoMarket>> FetchFromCoinGeckoAsync(CryptoCriteria criteria, CancellationToken cancellationToken)
    {
        // 确定排序方式
        var order = DetermineOrder(criteria);

        // 确定分页参数（用于市值排名筛选）
        var (page, perPage) = DeterminePagination(criteria);

        _logger.LogDebug("CoinGecko查询参数 - Order: {Order}, Page: {Page}, PerPage: {PerPage}",
            order, page, perPage);

        // 7d/30d 涨跌幅恒请求：条件筛选、稳定币识别与第三步分析都要用；不请求时 7d 字段整列为空
        return await _coinGeckoService.GetCoinsMarketsAsync(
            vsCurrency: "usd",
            order: order,
            perPage: perPage,
            page: page,
            priceChangePercentage: "7d,30d",
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 应用本地筛选条件（同时排除锚定型代币）
    /// </summary>
    internal static List<CoinGeckoMarket> ApplyFilters(List<CoinGeckoMarket> markets, CryptoCriteria criteria)
    {
        var filtered = markets.Where(m => !IsPegged(m));

        foreach (var condition in criteria.Criteria)
        {
            filtered = condition.Code.ToLowerInvariant() switch
            {
                "market_cap" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.MarketCap >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.MarketCap <= condition.MaxValue)),

                "market_cap_rank" => filtered.Where(m =>
                    m.MarketCapRank.HasValue &&
                    (!condition.MinValue.HasValue || m.MarketCapRank >= (int)condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.MarketCapRank <= (int)condition.MaxValue)),

                "volume_24h" or "total_volume" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.TotalVolume >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.TotalVolume <= condition.MaxValue)),

                "price_change_24h" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.PriceChangePercentage24h >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.PriceChangePercentage24h <= condition.MaxValue)),

                "price_change_7d" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.PriceChangePercentage7dInCurrency >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.PriceChangePercentage7dInCurrency <= condition.MaxValue)),

                "price_change_30d" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.PriceChangePercentage30dInCurrency >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.PriceChangePercentage30dInCurrency <= condition.MaxValue)),

                "current_price" or "price" => filtered.Where(m =>
                    (!condition.MinValue.HasValue || m.CurrentPrice >= condition.MinValue) &&
                    (!condition.MaxValue.HasValue || m.CurrentPrice <= condition.MaxValue)),

                _ => filtered
            };
        }

        return [.. filtered];
    }

    private List<ScreenerAssetInfo> ConvertToScreenerInfo(List<CoinGeckoMarket> markets)
    {
        return [.. markets.Select(m => (ScreenerAssetInfo)new ScreenerCryptoInfo
        {
            Name = m.Name,
            Symbol = m.Symbol.ToUpperInvariant(),
            Current = m.CurrentPrice ?? 0,
            Pct = m.PriceChangePercentage24h ?? 0,
            Amount = m.TotalVolume ?? 0,
            Mc = m.MarketCap ?? 0,
            Fmc = m.FullyDilutedValuation ?? 0,
            Volume = m.TotalVolume ?? 0,
            MarketCapRank = m.MarketCapRank ?? 0,
            PriceChange7d = m.PriceChangePercentage7dInCurrency ?? 0,
            PriceChange30d = m.PriceChangePercentage30dInCurrency ?? 0,
            CirculatingSupply = m.CirculatingSupply ?? 0,
            TotalSupply = m.TotalSupply ?? 0,
            MaxSupply = m.MaxSupply
        })];
    }

    /// <summary>
    /// Binance 兜底筛选：当 CoinGecko 不可用时，从币安获取 USDT 交易对行情。
    /// 币安行情不含市值/排名/供应量/7d/30d，遇到这类条件直接失败，见 <see cref="ApplyBinanceFilters"/>。
    /// </summary>
    private async Task<List<ScreenerAssetInfo>> FetchFromBinanceFallbackAsync(CryptoCriteria criteria, CancellationToken cancellationToken)
    {
        var tickers = await _binanceService.GetAll24hrTickersFullAsync(cancellationToken);

        // 仅保留指定计价货币的交易对（默认 USDT），排除锚定型交易对。
        // 兜底行情没有 30d 涨跌幅，价格带比 CoinGecko 路径收窄到 0.97–1.03 以降低误伤
        var quoteCurrency = string.IsNullOrWhiteSpace(criteria.QuoteCurrency) ? "USDT" : criteria.QuoteCurrency;
        var filtered = tickers.Where(t =>
            t.Symbol.EndsWith(quoteCurrency, StringComparison.OrdinalIgnoreCase) &&
            !(t.LastPrice is >= 0.97m and <= 1.03m && t.PriceChangePercent is > -1m and < 1m)).ToList();

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
    /// 应用 Binance 数据支持的筛选条件（仅 24h 涨跌幅、成交额、当前价）。
    /// 其余条件（市值、排名、7d/30d 涨跌幅）币安无法执行，抛 <see cref="FriendlyException"/> 而不是静默忽略：
    /// 忽略会让"市值前 50"返回成交额前 N 的无关币，用户还看不出条件被丢了。
    /// </summary>
    internal static List<Binance24hrTicker> ApplyBinanceFilters(List<Binance24hrTicker> tickers, CryptoCriteria criteria)
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

                _ => throw new FriendlyException(
                    $"CoinGecko 暂时不可用，而币安行情不含“{condition.Code}”数据，无法按该条件筛选，请稍后重试")
            };
        }

        return [.. filtered];
    }

    /// <summary>
    /// 锚定型代币（稳定币与国债/理财类 RWA 代币）：价格贴近 1 美元且 24h、30d 波动极小。
    /// 选币面向波动性标的，这类代币入选既无推荐价值，转交易对时还会拼出 USDTUSDT 等无效代码；
    /// 固定符号清单会随新稳定币过期（2026-11-28 实测前 250 名里 10 符号清单只命中 8 个，本判定命中 54 个且无误伤）。
    /// 已知残留：非美元稳定币、脱锚币、日波动大的 RWA 代币识别不到。
    /// </summary>
    internal static bool IsPegged(CoinGeckoMarket market) =>
        market.CurrentPrice is >= 0.9m and <= 1.5m
        && market.PriceChangePercentage24h is > -1m and < 1m
        && market.PriceChangePercentage30dInCurrency is > -3m and < 3m;

    /// <summary>
    /// 将 Binance Ticker 转换为 ScreenerCryptoInfo
    /// </summary>
    private List<ScreenerAssetInfo> ConvertBinanceToScreenerInfo(List<Binance24hrTicker> tickers)
    {
        return [.. tickers.Select(t =>
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
        })];
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

    internal static (int page, int perPage) DeterminePagination(CryptoCriteria criteria)
    {
        var rankFilter = criteria.Criteria.FirstOrDefault(c =>
            c.Code.Equals("market_cap_rank", StringComparison.OrdinalIgnoreCase));

        if (rankFilter != null)
        {
            var minRank = Math.Max(1, (int)(rankFilter.MinValue ?? 1));
            var maxRank = Math.Max(minRank, (int)(rankFilter.MaxValue ?? COINGECKO_MAX_PER_PAGE));

            // CoinGecko 分页偏移量为 (page - 1) * perPage：
            // 当起止排名均在单页上限 (250) 内时直接拉取第 1 页并覆盖到 maxRank（有其他客户端过滤条件时拉满 250），
            // 避免改变 perPage 导致页偏移错位；超出 250 时固定 perPage=250 按页对齐。
            if (minRank <= COINGECKO_MAX_PER_PAGE)
            {
                var hasOtherFilters = criteria.Criteria.Count > 1;
                var targetSize = hasOtherFilters
                    ? COINGECKO_MAX_PER_PAGE
                    : Math.Clamp(maxRank + 10, 20, COINGECKO_MAX_PER_PAGE);
                return (1, targetSize);
            }

            var page = (minRank - 1) / COINGECKO_MAX_PER_PAGE + 1;
            return (page, COINGECKO_MAX_PER_PAGE);
        }

        // 其余情况一律拉满 250：本地要先剔除锚定型代币、再与币安 USDT 现货交易对求交（2026-11-28 实测
        // 非稳定币里约 36%–51% 没有交易对），带条件时还要本地过滤，只拉 Limit+10 条凑不满候选池。
        return (1, COINGECKO_MAX_PER_PAGE);
    }

    #endregion
}

