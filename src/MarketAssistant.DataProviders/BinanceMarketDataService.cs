using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Web;

namespace MarketAssistant.DataProviders;

/// <summary>
/// 币安市场数据API服务（包含现货和期货公开端点）
/// </summary>
public sealed class BinanceMarketDataService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BinanceMarketDataService> _logger;
    private readonly IMemoryCache _memoryCache;

    /// <summary>
    /// 交易所信息缓存键
    /// </summary>
    private const string ExchangeInfoCacheKey = "BinanceExchangeInfo";

    /// <summary>
    /// 交易所信息回源闸门：冷启动并发首调只放一个请求出去（exchangeInfo weight=20），其余等结果共享缓存
    /// </summary>
    private readonly SemaphoreSlim _exchangeInfoGate = new(1, 1);

    private static readonly JsonSerializerOptions BinanceJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new StringToDecimalConverter() }
    };

    public BinanceMarketDataService(
        IHttpClientFactory httpClientFactory,
        ILogger<BinanceMarketDataService> logger,
        IMemoryCache memoryCache)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memoryCache = memoryCache ?? throw new ArgumentNullException(nameof(memoryCache));
    }

    /// <summary>
    /// 检查响应内容是否包含地区限制错误，并返回内容字符串
    /// </summary>
    private async Task<string> CheckAndReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken = default)
    {
        var content = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            if (content.Contains("restricted location", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError("币安服务在当前地区受限: {Content}", content);
                throw new FriendlyException(
                    "币安服务在当前地区不可用\n\n" +
                    "根据币安服务条款，您所在的地区无法访问此服务。\n\n" +
                    "建议解决方案：\n" +
                    "• 使用 VPN 或代理服务器\n" +
                    "• 在系统网络设置中配置代理\n" +
                    "• 联系币安客服确认您的地区是否被支持\n\n" +
                    "如果您认为这是错误提示，请联系客服处理。");
            }
        }

        response.EnsureSuccessStatusCode();
        return content;
    }

    #region 24小时价格统计

    public async Task<Binance24hrTicker?> Get24hrTickerAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v3/ticker/24hr?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}";
        _logger.LogDebug("调用币安API: {Url}", url);

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<Binance24hrTicker>(content, BinanceJsonSerializerOptions);
    }

    /// <summary>
    /// 批量获取多个交易对的24小时价格变动统计（最多100个）
    /// </summary>
    public async Task<List<Binance24hrTicker>> Get24hrTickersAsync(
        List<string> symbols,
        CancellationToken cancellationToken = default)
    {
        if (symbols == null || symbols.Count == 0)
            return new List<Binance24hrTicker>();

        if (symbols.Count > 100)
        {
            _logger.LogWarning("币安API批量查询最多支持100个交易对，已截取前100个");
            symbols = symbols.Take(100).ToList();
        }

        var symbolsArray = string.Join(",", symbols.Select(s => $"\"{s.ToUpperInvariant()}\""));
        var symbolsParam = $"[{symbolsArray}]";
        var encodedSymbols = HttpUtility.UrlEncode(symbolsParam);

        var url = $"/api/v3/ticker/24hr?symbols={encodedSymbols}&type=MINI";

        _logger.LogDebug("批量调用币安API: {Count}个交易对", symbols.Count);

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        var tickers = JsonSerializer.Deserialize<List<Binance24hrTicker>>(content, BinanceJsonSerializerOptions);
        _logger.LogInformation("成功获取币安24h行情数据，交易对数量: {Count}", tickers?.Count ?? 0);
        return tickers ?? new List<Binance24hrTicker>();
    }

    /// <summary>
    /// 获取所有交易对的24小时价格变动统计（MINI格式，权重80）
    /// </summary>
    public async Task<List<Binance24hrTicker>> GetAll24hrTickersAsync(
        CancellationToken cancellationToken = default)
    {
        var url = "/api/v3/ticker/24hr?type=MINI&symbolStatus=TRADING";
        _logger.LogDebug("调用币安API获取所有交易对24h行情");

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        var tickers = JsonSerializer.Deserialize<List<Binance24hrTicker>>(content, BinanceJsonSerializerOptions);
        _logger.LogInformation("成功获取所有币安24h行情数据，交易对数量: {Count}", tickers?.Count ?? 0);
        return tickers ?? new List<Binance24hrTicker>();
    }

    /// <summary>
    /// 获取所有交易对的24小时价格变动统计（FULL格式，含 PriceChangePercent，权重80）
    /// 用于 CoinGecko 不可用时的兜底数据源
    /// </summary>
    public async Task<List<Binance24hrTicker>> GetAll24hrTickersFullAsync(
        CancellationToken cancellationToken = default)
    {
        var url = "/api/v3/ticker/24hr?symbolStatus=TRADING";
        _logger.LogDebug("调用币安API获取所有交易对24h行情(FULL)");

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        var tickers = JsonSerializer.Deserialize<List<Binance24hrTicker>>(content, BinanceJsonSerializerOptions);
        _logger.LogInformation("成功获取所有币安24h行情数据(FULL)，交易对数量: {Count}", tickers?.Count ?? 0);
        return tickers ?? new List<Binance24hrTicker>();
    }

    #endregion

    #region 交易所信息

    /// <summary>
    /// 获取交易所信息（仅返回 TRADING 状态的交易对，使用 IMemoryCache 缓存1小时）
    /// </summary>
    public async Task<BinanceExchangeInfo?> GetExchangeInfoAsync(
        CancellationToken cancellationToken = default)
    {
        if (_memoryCache.TryGetValue(ExchangeInfoCacheKey, out BinanceExchangeInfo? cachedInfo) && cachedInfo != null)
        {
            _logger.LogDebug("从缓存获取交易所信息，交易对数量: {Count}", cachedInfo.Symbols?.Count ?? 0);
            return cachedInfo;
        }

        await _exchangeInfoGate.WaitAsync(cancellationToken);
        try
        {
            // 拿到闸门后再查一次：等闸门期间可能有先行者已完成回源
            if (_memoryCache.TryGetValue(ExchangeInfoCacheKey, out cachedInfo) && cachedInfo != null)
                return cachedInfo;

            var url = "/api/v3/exchangeInfo?symbolStatus=TRADING&showPermissionSets=false";
            _logger.LogDebug("调用币安交易所信息API");

            using var httpClient = _httpClientFactory.CreateClient("Binance");
            var response = await httpClient.GetAsync(url, cancellationToken);
            var content = await CheckAndReadResponseAsync(response, cancellationToken);

            var exchangeInfo = JsonSerializer.Deserialize<BinanceExchangeInfo>(content, BinanceJsonSerializerOptions);
            _logger.LogInformation("成功获取交易所信息，交易对数量: {Count}", exchangeInfo?.Symbols?.Count ?? 0);

            if (exchangeInfo != null)
            {
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1)
                };
                _memoryCache.Set(ExchangeInfoCacheKey, exchangeInfo, cacheOptions);
            }

            return exchangeInfo;
        }
        finally
        {
            _exchangeInfoGate.Release();
        }
    }

    /// <summary>
    /// 验证价格是否符合交易对的过滤器要求
    /// </summary>
    /// <param name="symbol">交易对符号（如 BTCUSDT）</param>
    /// <param name="price">待验证的价格</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>验证结果与错误信息，验证通过时 ErrorMessage 为 null</returns>
    public async Task<(bool IsValid, string? ErrorMessage)> ValidatePriceFilterAsync(
        string symbol,
        decimal price,
        CancellationToken ct = default)
    {
        var exchangeInfo = await GetExchangeInfoAsync(ct);
        if (exchangeInfo?.Symbols == null)
        {
            return (false, "获取交易所信息失败");
        }

        var symbolInfo = exchangeInfo.Symbols.FirstOrDefault(s =>
            string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase));
        if (symbolInfo == null)
        {
            return (false, "交易对不存在");
        }

        var priceFilter = symbolInfo.Filters?.FirstOrDefault(f =>
            string.Equals(f.FilterType, "PRICE_FILTER", StringComparison.OrdinalIgnoreCase));
        if (priceFilter == null)
        {
            // 无价格过滤器约束，视为有效
            return (true, null);
        }

        if (priceFilter.MinPrice.HasValue && price < priceFilter.MinPrice.Value)
        {
            return (false, $"价格 {price} 低于最小价格限制 {priceFilter.MinPrice.Value}");
        }

        if (priceFilter.MaxPrice.HasValue && price > priceFilter.MaxPrice.Value)
        {
            return (false, $"价格 {price} 超过最大价格限制 {priceFilter.MaxPrice.Value}");
        }

        if (priceFilter.TickSize.HasValue && priceFilter.TickSize.Value > 0)
        {
            var tickSize = priceFilter.TickSize.Value;
            var remainder = price % tickSize;
            if (remainder != 0)
            {
                return (false, $"价格 {price} 不符合价格步长 {tickSize} 的要求");
            }
        }

        return (true, null);
    }

    #endregion

    #region K线数据

    public async Task<JsonArray?> GetKlinesAsync(
        string symbol,
        string interval = "1d",
        int limit = 500,
        long? startTime = null,
        long? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v3/klines?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&interval={Uri.EscapeDataString(interval)}&limit={limit}";

        if (startTime.HasValue)
            url += $"&startTime={startTime.Value}";
        if (endTime.HasValue)
            url += $"&endTime={endTime.Value}";

        _logger.LogDebug("调用币安K线API: {Symbol} {Interval}", symbol, interval);

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        var klines = JsonSerializer.Deserialize<JsonArray>(content);
        _logger.LogInformation("成功获取K线数据: {Symbol}, 数量: {Count}", symbol, klines?.Count ?? 0);
        return klines;
    }

    #endregion

    #region 订单簿深度

    public async Task<JsonObject?> GetDepthAsync(
        string symbol,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v3/depth?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&limit={limit}";

        _logger.LogDebug("调用币安深度API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);
        return JsonSerializer.Deserialize<JsonObject>(content);
    }

    #endregion

    #region 最近交易

    public async Task<JsonArray?> GetRecentTradesAsync(
        string symbol,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v3/trades?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&limit={limit}";

        _logger.LogDebug("调用币安交易记录API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);
        return JsonSerializer.Deserialize<JsonArray>(content);
    }

    #endregion

    #region 期货API

    public async Task<BinancePremiumIndexResponse?> GetPremiumIndexAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var url = $"/fapi/v1/premiumIndex?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}";
        _logger.LogDebug("调用币安期货资金费率API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("BinanceFutures");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<BinancePremiumIndexResponse>(content, BinanceJsonSerializerOptions);
    }

    public async Task<List<BinanceFundingRateResponse>> GetFundingRateHistoryAsync(
        string symbol,
        int limit = 30,
        long? startTime = null,
        long? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"/fapi/v1/fundingRate?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&limit={limit}";

        if (startTime.HasValue)
            url += $"&startTime={startTime.Value}";
        if (endTime.HasValue)
            url += $"&endTime={endTime.Value}";

        _logger.LogDebug("调用币安期货历史资金费率API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("BinanceFutures");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<List<BinanceFundingRateResponse>>(content, BinanceJsonSerializerOptions)
               ?? new List<BinanceFundingRateResponse>();
    }

    /// <param name="endpoint">统计端点（枚举白名单，杜绝路径拼接）</param>
    public async Task<List<BinanceLongShortRatioResponse>> GetLongShortRatioAsync(
        LongShortRatioEndpoint endpoint,
        string symbol,
        string period = "5m",
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var endpointPath = endpoint switch
        {
            LongShortRatioEndpoint.GlobalLongShortAccountRatio => "globalLongShortAccountRatio",
            LongShortRatioEndpoint.TopLongShortAccountRatio => "topLongShortAccountRatio",
            LongShortRatioEndpoint.TopLongShortPositionRatio => "topLongShortPositionRatio",
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint, null)
        };
        var url = $"/futures/data/{endpointPath}?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&period={Uri.EscapeDataString(period)}&limit={limit}";
        _logger.LogDebug("调用币安期货多空比API: {Symbol} {Endpoint}", symbol, endpointPath);

        using var httpClient = _httpClientFactory.CreateClient("BinanceFutures");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<List<BinanceLongShortRatioResponse>>(content, BinanceJsonSerializerOptions)
               ?? new List<BinanceLongShortRatioResponse>();
    }

    public async Task<List<BinanceOpenInterestResponse>> GetOpenInterestHistAsync(
        string symbol,
        string period = "5m",
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var url = $"/futures/data/openInterestHist?symbol={Uri.EscapeDataString(symbol.ToUpperInvariant())}&period={Uri.EscapeDataString(period)}&limit={limit}";
        _logger.LogDebug("调用币安期货持仓量API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("BinanceFutures");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<List<BinanceOpenInterestResponse>>(content, BinanceJsonSerializerOptions)
               ?? new List<BinanceOpenInterestResponse>();
    }

    #endregion
}

/// <summary>
/// 多空比统计端点（币安 /futures/data 下三种口径的白名单）
/// </summary>
public enum LongShortRatioEndpoint
{
    /// <summary>全局账户多空比</summary>
    GlobalLongShortAccountRatio,

    /// <summary>顶级交易员账户多空比</summary>
    TopLongShortAccountRatio,

    /// <summary>顶级交易员持仓多空比</summary>
    TopLongShortPositionRatio
}
