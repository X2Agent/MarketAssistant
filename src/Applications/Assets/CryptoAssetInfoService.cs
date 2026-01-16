using MarketAssistant.Applications.Assets.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 虚拟币资产信息服务实现（基于币安API）
/// API文档: https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api
/// </summary>
public class CryptoAssetInfoService : IAssetInfoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CryptoAssetInfoService> _logger;
    private readonly IMemoryCache _memoryCache;
    private const string BINANCE_API_BASE_URL = "https://api.binance.com";
    private const string SYMBOLS_CACHE_KEY = "BinanceSymbols";

    public CryptoAssetInfoService(ILogger<CryptoAssetInfoService> logger, IMemoryCache memoryCache)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// 搜索虚拟币（支持名称和代码）
    /// </summary>
    public async Task<List<(string Name, string Code)>> SearchAsync(string keyword, CancellationToken cancellationToken = default)
    {
        try
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "虚拟币搜索失败: {Keyword}", keyword);
            return new List<(string Name, string Code)>();
        }
    }

    /// <summary>
    /// 获取虚拟币详细信息
    /// </summary>
    public async Task<AssetInfo> GetAssetInfoAsync(string code, string market = "", CancellationToken cancellationToken = default)
    {
        try
        {
            // 格式化交易对代码
            string symbol = ToBinanceFormat(code);

            // 调用币安24小时价格统计API
            var url = $"{BINANCE_API_BASE_URL}/api/v3/ticker/24hr?symbol={symbol}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var ticker = await response.Content.ReadFromJsonAsync<BinanceTicker24hr>(cancellationToken);

            if (ticker == null)
            {
                throw new FriendlyException("解析币安API响应失败");
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
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "获取虚拟币详情失败 - 网络错误: {Code}", code);
            throw new FriendlyException($"获取虚拟币详情失败: 网络连接错误", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取虚拟币详情失败: {Code}", code);
            throw new FriendlyException($"获取虚拟币详情失败: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取热门虚拟币（按24小时交易量排序）
    /// </summary>
    public async Task<List<HotAsset>> GetHotAssetsAsync()
    {
        try
        {
            // 获取所有交易对的24小时统计（使用MINI类型减少数据量）
            var url = $"{BINANCE_API_BASE_URL}/api/v3/ticker/24hr?type=MINI";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var tickers = await response.Content.ReadFromJsonAsync<List<BinanceTicker24hrMini>>();

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
                    var isPriceNearOne = false;

                    if (decimal.TryParse(t.LastPrice, out var price))
                    {
                        isPriceNearOne = price >= 0.95m && price <= 1.05m;
                    }

                    // 只有同时满足"是稳定币"且"价格接近1.0"才过滤掉（返回false）
                    return !(isStablecoin && isPriceNearOne);
                })
                .OrderByDescending(t => decimal.TryParse(t.QuoteVolume, out var vol) ? vol : 0)
                .Take(8)
                .Select(t => new HotAsset
                {
                    Name = ExtractBaseCurrency(t.Symbol),
                    Code = t.Symbol,
                    Market = "Binance",
                    CurrentPrice = FormatPrice(t.LastPrice),
                    ChangePercentage = FormatOpenClosePercentage(t.OpenPrice, t.LastPrice),
                    MarketType = MarketType.Crypto,
                    HeatIndex = FormatVolume(t.QuoteVolume), // 使用交易量作为热度
                    SectorName = "加密货币" // 虚拟币暂不区分板块
                })
                .ToList();

            _logger.LogInformation("成功获取热门虚拟币: {Count} 个", hotAssets.Count);
            return hotAssets;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取热门虚拟币失败: {Message}", ex.Message);
            throw new FriendlyException($"获取热门虚拟币失败: {ex.Message}", ex);
        }
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

        // 请求交易所信息（使用 symbolStatus 参数过滤 TRADING 状态）
        var url = $"{BINANCE_API_BASE_URL}/api/v3/exchangeInfo?symbolStatus=TRADING&showPermissionSets=false";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var exchangeInfo = await response.Content.ReadFromJsonAsync<BinanceExchangeInfo>(cancellationToken);

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
    private string FormatPrice(string? price)
    {
        if (string.IsNullOrEmpty(price) || !decimal.TryParse(price, out var value))
        {
            return "0.00";
        }

        // 根据价格大小选择精度
        if (value >= 1000)
        {
            return value.ToString("N2"); // 1000+ 显示2位小数
        }
        else if (value >= 1)
        {
            return value.ToString("N4"); // 1-1000 显示4位小数
        }
        else
        {
            return value.ToString("N6"); // <1 显示6位小数
        }
    }

    /// <summary>
    /// 格式化百分比显示
    /// </summary>
    private string FormatPercentage(string? percent)
    {
        if (string.IsNullOrEmpty(percent) || !decimal.TryParse(percent, out var value))
        {
            return "0.00%";
        }

        return $"{value:F2}%";
    }

    /// <summary>
    /// 根据开盘价和收盘价计算涨跌幅（用于MINI数据）
    /// </summary>
    private string FormatOpenClosePercentage(string? openPrice, string? lastPrice)
    {
        if (string.IsNullOrEmpty(openPrice) || string.IsNullOrEmpty(lastPrice) ||
            !decimal.TryParse(openPrice, out var open) || !decimal.TryParse(lastPrice, out var last) ||
            open == 0)
        {
            return "0.00%";
        }

        var changePercent = ((last - open) / open) * 100;
        return $"{changePercent:F2}%";
    }

    /// <summary>
    /// 格式化交易量显示（K, M, B）
    /// </summary>
    private string FormatVolume(string? volume)
    {
        if (string.IsNullOrEmpty(volume) || !decimal.TryParse(volume, out var value))
        {
            return "0";
        }

        if (value >= 1_000_000_000)
        {
            return $"{(value / 1_000_000_000):N2}B";
        }
        else if (value >= 1_000_000)
        {
            return $"{(value / 1_000_000):N2}M";
        }
        else if (value >= 1_000)
        {
            return $"{(value / 1_000):N2}K";
        }
        else
        {
            return $"{value:N2}";
        }
    }

    #endregion

    #region 币安API响应模型

    /// <summary>
    /// 币安交易所信息响应
    /// </summary>
    private class BinanceExchangeInfo
    {
        public List<BinanceSymbolInfo>? Symbols { get; set; }
    }

    /// <summary>
    /// 币安交易对信息
    /// </summary>
    private class BinanceSymbolInfo
    {
        public string Symbol { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string BaseAsset { get; set; } = string.Empty;
        public string QuoteAsset { get; set; } = string.Empty;
    }

    /// <summary>
    /// 币安24小时价格统计（完整版）
    /// </summary>
    private class BinanceTicker24hr
    {
        public string Symbol { get; set; } = string.Empty;
        public string? LastPrice { get; set; }
        public string? PriceChange { get; set; }
        public string? PriceChangePercent { get; set; }
        public string? Volume { get; set; }
        public string? QuoteVolume { get; set; }
        public string? HighPrice { get; set; }
        public string? LowPrice { get; set; }
    }

    /// <summary>
    /// 币安24小时价格统计（精简版 - 用于GetHotAssets）
    /// </summary>
    private class BinanceTicker24hrMini
    {
        public string Symbol { get; set; } = string.Empty;
        public string? OpenPrice { get; set; }
        public string? HighPrice { get; set; }
        public string? LowPrice { get; set; }
        public string? LastPrice { get; set; }
        public string? Volume { get; set; }
        public string? QuoteVolume { get; set; }
    }

    #endregion
}






