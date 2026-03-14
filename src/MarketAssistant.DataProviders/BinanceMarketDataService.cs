using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;
using System.Web;

namespace MarketAssistant.Services.Data;

/// <summary>
/// 币安市场数据API服务（包含现货和期货公开端点）
/// </summary>
public sealed class BinanceMarketDataService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BinanceMarketDataService> _logger;

    private static readonly JsonSerializerOptions BinanceJsonSerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new StringToDecimalConverter() }
    };

    public BinanceMarketDataService(
        IHttpClientFactory httpClientFactory,
        ILogger<BinanceMarketDataService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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

    /// <summary>
    /// 获取单个交易对的24小时价格变动统计
    /// </summary>
    public async Task<Binance24hrTicker?> Get24hrTickerAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v3/ticker/24hr?symbol={symbol.ToUpperInvariant()}";
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

    #endregion

    #region 交易所信息

    /// <summary>
    /// 获取交易所信息（仅返回 TRADING 状态的交易对）
    /// </summary>
    public async Task<BinanceExchangeInfo?> GetExchangeInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var url = "/api/v3/exchangeInfo?symbolStatus=TRADING&showPermissionSets=false";
        _logger.LogDebug("调用币安交易所信息API");

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        var exchangeInfo = JsonSerializer.Deserialize<BinanceExchangeInfo>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        _logger.LogInformation("成功获取交易所信息，交易对数量: {Count}", exchangeInfo?.Symbols?.Count ?? 0);
        return exchangeInfo;
    }

    #endregion

    #region K线数据

    /// <summary>
    /// 获取K线数据（OHLCV）
    /// </summary>
    public async Task<JsonArray?> GetKlinesAsync(
        string symbol,
        string interval = "1d",
        int limit = 500,
        long? startTime = null,
        long? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v3/klines?symbol={symbol.ToUpperInvariant()}&interval={interval}&limit={limit}";

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

    /// <summary>
    /// 获取订单簿深度数据
    /// </summary>
    public async Task<JsonObject?> GetDepthAsync(
        string symbol,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v3/depth?symbol={symbol.ToUpperInvariant()}&limit={limit}";

        _logger.LogDebug("调用币安深度API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);
        return JsonSerializer.Deserialize<JsonObject>(content);
    }

    #endregion

    #region 最近交易

    /// <summary>
    /// 获取最近交易记录
    /// </summary>
    public async Task<JsonArray?> GetRecentTradesAsync(
        string symbol,
        int limit = 500,
        CancellationToken cancellationToken = default)
    {
        var url = $"/api/v3/trades?symbol={symbol.ToUpperInvariant()}&limit={limit}";

        _logger.LogDebug("调用币安交易记录API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("Binance");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);
        return JsonSerializer.Deserialize<JsonArray>(content);
    }

    #endregion

    #region 期货API

    /// <summary>
    /// 获取资金费率
    /// </summary>
    public async Task<BinancePremiumIndexResponse?> GetPremiumIndexAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var url = $"/fapi/v1/premiumIndex?symbol={symbol.ToUpperInvariant()}";
        _logger.LogDebug("调用币安期货资金费率API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("BinanceFutures");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<BinancePremiumIndexResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    /// <summary>
    /// 获取历史资金费率
    /// </summary>
    public async Task<List<BinanceFundingRateResponse>> GetFundingRateHistoryAsync(
        string symbol,
        int limit = 30,
        long? startTime = null,
        long? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var url = $"/fapi/v1/fundingRate?symbol={symbol.ToUpperInvariant()}&limit={limit}";

        if (startTime.HasValue)
            url += $"&startTime={startTime.Value}";
        if (endTime.HasValue)
            url += $"&endTime={endTime.Value}";

        _logger.LogDebug("调用币安期货历史资金费率API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("BinanceFutures");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<List<BinanceFundingRateResponse>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new List<BinanceFundingRateResponse>();
    }

    /// <summary>
    /// 获取多空比数据
    /// </summary>
    public async Task<List<BinanceLongShortRatioResponse>> GetLongShortRatioAsync(
        string endpoint,
        string symbol,
        string period = "5m",
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var url = $"/futures/data/{endpoint}?symbol={symbol.ToUpperInvariant()}&period={period}&limit={limit}";
        _logger.LogDebug("调用币安期货多空比API: {Symbol} {Endpoint}", symbol, endpoint);

        using var httpClient = _httpClientFactory.CreateClient("BinanceFutures");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<List<BinanceLongShortRatioResponse>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new List<BinanceLongShortRatioResponse>();
    }

    /// <summary>
    /// 获取持仓量历史数据
    /// </summary>
    public async Task<List<BinanceOpenInterestResponse>> GetOpenInterestHistAsync(
        string symbol,
        string period = "5m",
        int limit = 30,
        CancellationToken cancellationToken = default)
    {
        var url = $"/futures/data/openInterestHist?symbol={symbol.ToUpperInvariant()}&period={period}&limit={limit}";
        _logger.LogDebug("调用币安期货持仓量API: {Symbol}", symbol);

        using var httpClient = _httpClientFactory.CreateClient("BinanceFutures");
        var response = await httpClient.GetAsync(url, cancellationToken);
        var content = await CheckAndReadResponseAsync(response, cancellationToken);

        return JsonSerializer.Deserialize<List<BinanceOpenInterestResponse>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
               ?? new List<BinanceOpenInterestResponse>();
    }

    #endregion
}
