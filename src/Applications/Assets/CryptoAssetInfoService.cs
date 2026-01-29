using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Services.Data;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 虚拟币资产信息服务实现（基于币安API）
/// </summary>
public class CryptoAssetInfoService : IAssetInfoService
{
    private readonly BinanceMarketDataService _binanceService;
    private readonly ILogger<CryptoAssetInfoService> _logger;
    private readonly IMemoryCache _memoryCache;
    private const string SYMBOLS_CACHE_KEY = "BinanceSymbols";

    public CryptoAssetInfoService(
        BinanceMarketDataService binanceService,
        ILogger<CryptoAssetInfoService> logger,
        IMemoryCache memoryCache)
    {
        _binanceService = binanceService ?? throw new ArgumentNullException(nameof(binanceService));
        _logger = logger;
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// 搜索虚拟币（支持名称和代码）
    /// </summary>
    public async Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<(string Name, string Code)>();
        }

        keyword = keyword.Trim().ToUpperInvariant();

        // 获取所有交易对信息（已过滤为 TRADING 状态）
        var symbols = await GetSymbolsAsync(cancellationToken);

        // 搜索匹配的交易对
        var results = symbols
            .Where(s => s.BaseAsset.Contains(keyword))
            .Select(s => (Name: s.BaseAsset, Code: s.Symbol))
            .Take(20)
            .ToList();

        _logger.LogInformation("虚拟币搜索: {Keyword}, 返回 {Count} 条结果", keyword, results.Count);
        return results;
    }

    /// <summary>
    /// 获取虚拟币详细信息
    /// </summary>
    public async Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default)
    {

        // 格式化交易对代码
        string symbol = ToBinanceFormat(code);

        // 调用币安服务获取24小时价格统计
        var ticker = await _binanceService.Get24hrTickerAsync(symbol, cancellationToken);

        if (ticker == null)
        {
            throw new FriendlyException("获取币安行情数据失败");
        }

        // 构建资产信息
        var assetInfo = new AssetInfo
        {
            Code = symbol,
            Name = ExtractBaseCurrency(ticker.Symbol), // 提取基础币种
            MarketType = MarketType.Crypto,
            Market = "Binance",
            CurrentPrice = FormatPrice(ticker.LastPrice),
            ChangePercentage = FormatPercentage(ticker.PriceChangePercent),
            Volume24h = FormatVolume(ticker.Volume),
            MarketCap = FormatVolume(ticker.QuoteVolume) // 使用USDT交易量作为市值参考
        };

        _logger.LogInformation("成功获取虚拟币详情: {Symbol}", symbol);
        return assetInfo;
    }

    /// <summary>
    /// 获取热门虚拟币（按24小时交易量排序）
    /// </summary>
    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        // 调用币安服务获取所有交易对的24小时统计
        var tickers = await _binanceService.GetAll24hrTickersAsync();

        if (tickers == null || tickers.Count == 0)
        {
            _logger.LogWarning("币安API返回数据为空");
            return new List<HotAsset>();
        }

        // 稳定币列表（用于过滤稳定币互换交易对）
        var stablecoins = new[] { "USDT", "USDC", "BUSD", "FDUSD", "DAI", "TUSD", "USDP" };

        // 筛选USDT交易对，排除稳定币互换（需同时满足：基础币种是稳定币 且 价格接近1.0），按24小时交易量排序，取前8个
        var hotAssets = tickers
            .Where(t => t.Symbol.EndsWith("USDT") && t.Symbol != "USDT")
            // 过滤稳定币互换交易对（基础币种是稳定币 且 价格接近1.0，同时满足才过滤）
            .Where(t =>
            {
                var baseCurrency = ExtractBaseCurrency(t.Symbol);
                var isStablecoin = stablecoins.Contains(baseCurrency);
                var isPriceNearOne = t.LastPrice >= 0.95m && t.LastPrice <= 1.05m;

                // 只有同时满足"是稳定币"且"价格接近1.0"才过滤掉（返回false）
                return !(isStablecoin && isPriceNearOne);
            })
            .OrderByDescending(t => t.QuoteVolume)
            .Take(8)
            .Select(t => new HotAsset
            {
                Name = ExtractBaseCurrency(t.Symbol),
                Code = t.Symbol,
                Market = "Binance",
                CurrentPrice = FormatPrice(t.LastPrice),
                ChangePercentage = FormatOpenClosePercentage(t.OpenPrice, t.LastPrice),
                MarketType = MarketType.Crypto,
                MetricValue = FormatVolume(t.QuoteVolume),
                SectorName = "加密货币"
            })
            .ToList();

        _logger.LogInformation("成功获取热门虚拟币: {Count} 个", hotAssets.Count);
        return hotAssets;
    }

    #region 辅助方法

    /// <summary>
    /// 获取所有交易对信息（使用 IMemoryCache 缓存，只获取 TRADING 状态）
    /// </summary>
    private async Task<List<BinanceSymbolInfo>> GetSymbolsAsync(CancellationToken cancellationToken)
    {
        // 尝试从缓存获取
        if (_memoryCache.TryGetValue(SYMBOLS_CACHE_KEY, out List<BinanceSymbolInfo>? cachedSymbols) && cachedSymbols != null)
        {
            return cachedSymbols;
        }

        // 调用币安服务获取交易所信息
        var exchangeInfo = await _binanceService.GetExchangeInfoAsync(cancellationToken);

        var tradingSymbols = exchangeInfo?.Symbols ?? new List<BinanceSymbolInfo>();

        // 设置缓存（1小时过期）
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
        };
        _memoryCache.Set(SYMBOLS_CACHE_KEY, tradingSymbols, cacheOptions);

        _logger.LogInformation("已缓存 {Count} 个 TRADING 状态的交易对", tradingSymbols.Count);
        return tradingSymbols;
    }

    /// <summary>
    /// 格式化价格显示
    /// </summary>
    private string FormatPrice(decimal price)
    {
        // 根据价格大小选择精度
        if (price >= 1000)
        {
            return price.ToString("N2"); // 1000+ 显示2位小数
        }
        else if (price >= 1)
        {
            return price.ToString("N4"); // 1-1000 显示4位小数
        }
        else
        {
            return price.ToString("N6"); // <1 显示6位小数
        }
    }

    /// <summary>
    /// 格式化百分比显示
    /// </summary>
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

    /// <summary>
    /// 格式化交易量显示（K, M, B）
    /// </summary>
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






