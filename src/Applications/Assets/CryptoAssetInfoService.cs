using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace MarketAssistant.Applications.Assets;

/// <summary>
/// 虚拟币资产信息服务实现（基于币安API）
/// API文档: https://developers.binance.com/docs/zh-CN/binance-spot-api-docs/rest-api
/// </summary>
public class CryptoAssetInfoService : IAssetInfoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CryptoAssetInfoService> _logger;
    private const string BINANCE_API_BASE_URL = "https://api.binance.com";
    
    // 缓存交易对信息（避免频繁请求）
    private List<BinanceSymbolInfo>? _symbolsCache;
    private DateTime _symbolsCacheTime;
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromHours(1);

    public CryptoAssetInfoService(ILogger<CryptoAssetInfoService> logger)
    {
        _logger = logger;
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

            // 获取所有交易对信息
            var symbols = await GetSymbolsAsync(cancellationToken);

            // 搜索匹配的交易对（只返回USDT交易对，最常用）
            var results = symbols
                .Where(s => s.Symbol.Contains(keyword) && s.QuoteAsset == "USDT" && s.Status == "TRADING")
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
            string symbol = FormatSymbol(code);

            // 调用币安24小时价格统计API
            var url = $"{BINANCE_API_BASE_URL}/api/v3/ticker/24hr?symbol={symbol}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var ticker = JsonSerializer.Deserialize<BinanceTicker24hr>(jsonContent);

            if (ticker == null)
            {
                throw new FriendlyException("解析币安API响应失败");
            }

            // 构建资产信息
            var assetInfo = new AssetInfo
            {
                Code = symbol,
                Name = ticker.Symbol.Replace("USDT", "").Replace("BTC", "").Replace("ETH", ""), // 提取基础币种
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
            // 获取所有交易对的24小时统计
            var url = $"{BINANCE_API_BASE_URL}/api/v3/ticker/24hr";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var jsonContent = await response.Content.ReadAsStringAsync();
            var tickers = JsonSerializer.Deserialize<List<BinanceTicker24hr>>(jsonContent);

            if (tickers == null || tickers.Count == 0)
            {
                _logger.LogWarning("币安API返回数据为空");
                return new List<HotAsset>();
            }

            // 筛选USDT交易对，按24小时交易量排序，取前8个
            var hotAssets = tickers
                .Where(t => t.Symbol.EndsWith("USDT") && t.Symbol != "USDT")
                .OrderByDescending(t => decimal.TryParse(t.QuoteVolume, out var vol) ? vol : 0)
                .Take(8)
                .Select(t => new HotAsset
                {
                    Name = t.Symbol.Replace("USDT", ""),
                    Code = t.Symbol,
                    Market = "Binance",
                    CurrentPrice = FormatPrice(t.LastPrice),
                    ChangePercentage = FormatPercentage(t.PriceChangePercent),
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
    /// 获取所有交易对信息（带缓存）
    /// </summary>
    private async Task<List<BinanceSymbolInfo>> GetSymbolsAsync(CancellationToken cancellationToken)
    {
        // 检查缓存
        if (_symbolsCache != null && (DateTime.Now - _symbolsCacheTime) < _cacheExpiry)
        {
            return _symbolsCache;
        }

        // 请求交易所信息
        var url = $"{BINANCE_API_BASE_URL}/api/v3/exchangeInfo";
        var response = await _httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var exchangeInfo = JsonSerializer.Deserialize<BinanceExchangeInfo>(jsonContent);

        _symbolsCache = exchangeInfo?.Symbols ?? new List<BinanceSymbolInfo>();
        _symbolsCacheTime = DateTime.Now;

        _logger.LogInformation("已缓存 {Count} 个交易对信息", _symbolsCache.Count);
        return _symbolsCache;
    }

    /// <summary>
    /// 格式化交易对代码
    /// </summary>
    private string FormatSymbol(string code)
    {
        code = code.Replace("crypto.", "", StringComparison.OrdinalIgnoreCase)
                   .Replace(" ", "")
                   .ToUpperInvariant();

        // 如果没有交易对后缀，默认添加USDT
        if (!code.Contains("USDT") && !code.Contains("BTC") && !code.Contains("ETH"))
        {
            code += "USDT";
        }

        return code;
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
    /// 币安24小时价格统计
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

    #endregion
}






