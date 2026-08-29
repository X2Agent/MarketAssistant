using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.DataProviders;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 虚拟币资产信息服务实现（基于币安API + CoinGecko市值数据）
/// </summary>
public class CryptoAssetInfoService : IAssetInfoService
{
    /// <summary>首页热门资产展示条数（2 列 × 5 行）</summary>
    private const int HotAssetCount = 10;

    private readonly BinanceMarketDataService _binanceService;
    private readonly CoinGeckoApiService _coinGeckoService;
    private readonly ICryptoAliasRegistry _aliasRegistry;
    private readonly ILogger<CryptoAssetInfoService> _logger;
    private readonly IMemoryCache _memoryCache;

    public CryptoAssetInfoService(
        BinanceMarketDataService binanceService,
        CoinGeckoApiService coinGeckoService,
        ICryptoAliasRegistry aliasRegistry,
        ILogger<CryptoAssetInfoService> logger,
        IMemoryCache memoryCache)
    {
        _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
        _coinGeckoService = coinGeckoService ?? throw new ArgumentNullException(nameof(coinGeckoService));
        _aliasRegistry = aliasRegistry;
        _logger = logger;
        _memoryCache = memoryCache;
    }

    public async Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<(string Name, string Code)>();
        }

        keyword = keyword.Trim().ToUpperInvariant();

        var symbols = await GetSymbolsAsync(cancellationToken);

        var results = symbols
            .Where(s => s.BaseAsset.Contains(keyword))
            .Select(s => (Name: s.BaseAsset, Code: s.Symbol))
            .Take(20)
            .ToList();

        _logger.LogInformation("虚拟币搜索: {Keyword}, 返回 {Count} 条结果", keyword, results.Count);
        return results;
    }

    public async Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default)
    {
        string symbol = ToBinanceFormat(code);

        var ticker = await _binanceService.Get24hrTickerAsync(symbol, cancellationToken);

        if (ticker == null)
        {
            throw new FriendlyException("获取币安行情数据失败");
        }

        var assetInfo = new AssetInfo
        {
            Code = symbol,
            Name = ExtractBaseCurrency(ticker.Symbol),
            MarketType = MarketType.Crypto,
            Market = "Binance",
            CurrentPrice = PriceFormatter.Format(ticker.LastPrice),
            ChangePercentage = FormatPercentage(ticker.PriceChangePercent),
            Volume24h = FormatVolume(ticker.Volume),
            MarketCap = await FetchMarketCapAsync(ExtractBaseCurrency(ticker.Symbol), cancellationToken)
        };

        _logger.LogInformation("成功获取虚拟币详情: {Symbol}", symbol);
        return assetInfo;
    }

    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        var tickers = await _binanceService.GetAll24hrTickersAsync();

        if (tickers == null || tickers.Count == 0)
        {
            _logger.LogWarning("币安API返回数据为空");
            return new List<HotAsset>();
        }

        // 稳定币列表（用于过滤稳定币互换交易对）
        var stablecoins = new[] { "USDT", "USDC", "BUSD", "FDUSD", "DAI", "TUSD", "USDP" };

        var hotAssets = tickers
            .Where(t => t.Symbol.EndsWith("USDT") && t.Symbol != "USDT")
            .Where(t =>
            {
                var baseCurrency = ExtractBaseCurrency(t.Symbol);
                var isStablecoin = stablecoins.Contains(baseCurrency);
                var isPriceNearOne = t.LastPrice >= 0.95m && t.LastPrice <= 1.05m;

                return !(isStablecoin && isPriceNearOne);
            })
            .OrderByDescending(t => t.QuoteVolume)
            .Take(HotAssetCount)
            .Select(t => new HotAsset
            {
                Name = ExtractBaseCurrency(t.Symbol),
                Code = t.Symbol,
                Market = "Binance",
                CurrentPrice = PriceFormatter.Format(t.LastPrice),
                ChangePercentage = FormatOpenClosePercentage(t.OpenPrice, t.LastPrice),
                MarketType = MarketType.Crypto,
                MetricLabel = "交易量",
                MetricValue = FormatVolume(t.QuoteVolume),
                SectorName = "加密货币"
            })
            .ToList();

        _logger.LogInformation("成功获取热门虚拟币: {Count} 个", hotAssets.Count);
        return hotAssets;
    }

    private async Task<string> FetchMarketCapAsync(string baseCurrency, CancellationToken cancellationToken)
    {
        try
        {
            var idMap = await _aliasRegistry.GetCoinGeckoIdMapAsync(cancellationToken);
            var coinId = idMap.TryGetValue(baseCurrency.ToUpperInvariant(), out var id)
                ? id
                : ToCoinGeckoId(baseCurrency);
            var marketData = await _coinGeckoService.GetCoinMarketDataAsync(coinId, cancellationToken: cancellationToken);

            if (marketData is { Count: > 0 } && marketData[0] is System.Text.Json.Nodes.JsonObject data)
            {
                var marketCap = data["market_cap"]?.GetValue<decimal?>();
                if (marketCap.HasValue && marketCap.Value > 0)
                {
                    return FormatVolume(marketCap.Value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "从CoinGecko获取市值失败: {BaseCurrency}，将显示为N/A", baseCurrency);
        }

        return "N/A";
    }

    #region 辅助方法

    /// <summary>
    /// 获取所有交易对信息（使用 IMemoryCache 缓存，只获取 TRADING 状态）
    /// </summary>
    private async Task<List<BinanceSymbolInfo>> GetSymbolsAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(CacheKeys.CryptoSymbols, out List<BinanceSymbolInfo>? cachedSymbols) && cachedSymbols != null)
        {
            return cachedSymbols;
        }

        var exchangeInfo = await _binanceService.GetExchangeInfoAsync(cancellationToken);

        var tradingSymbols = exchangeInfo?.Symbols ?? new List<BinanceSymbolInfo>();

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };
        _memoryCache.Set(CacheKeys.CryptoSymbols, tradingSymbols, cacheOptions);

        _logger.LogInformation("已缓存 {Count} 个 TRADING 状态的交易对", tradingSymbols.Count);
        return tradingSymbols;
    }

    private string FormatPercentage(decimal? percent)
    {
        if (!percent.HasValue)
        {
            return "0.00%";
        }

        return $"{percent.Value:F2}%";
    }

    /// <summary>
    /// 根据开盘价和收盘价计算涨跌幅（用于MINI数据）
    /// </summary>
    private string FormatOpenClosePercentage(decimal openPrice, decimal lastPrice)
    {
        if (openPrice == 0)
        {
            return "0.00%";
        }

        var changePercent = ((lastPrice - openPrice) / openPrice) * 100;
        return $"{changePercent:F2}%";
    }

    private string FormatVolume(decimal volume)
    {
        if (volume >= 1_000_000_000)
        {
            return $"{(volume / 1_000_000_000):N2}B";
        }
        else if (volume >= 1_000_000)
        {
            return $"{(volume / 1_000_000):N2}M";
        }
        else if (volume >= 1_000)
        {
            return $"{(volume / 1_000):N2}K";
        }
        else
        {
            return $"{volume:N2}";
        }
    }

    #endregion
}






