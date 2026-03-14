using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;


namespace MarketAssistant.Services.Data;

/// <summary>
/// CoinGecko API服务（含客户端限流，免费版 30 次/分钟）
/// </summary>
public sealed class CoinGeckoApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoinGeckoApiService> _logger;

    private static readonly SemaphoreSlim Throttle = new(1, 1);
    private static DateTime _lastRequestTime = DateTime.MinValue;
    private const int MinRequestIntervalMs = 2500;

    public CoinGeckoApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<CoinGeckoApiService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 限流执行：确保请求间隔不低于 MinRequestIntervalMs（约 24 次/分钟）
    /// </summary>
    private async Task<T> ThrottledExecuteAsync<T>(Func<HttpClient, Task<T>> action, CancellationToken cancellationToken)
    {
        await Throttle.WaitAsync(cancellationToken);
        try
        {
            var elapsed = (DateTime.UtcNow - _lastRequestTime).TotalMilliseconds;
            if (elapsed < MinRequestIntervalMs)
                await Task.Delay((int)(MinRequestIntervalMs - elapsed), cancellationToken);

            using var httpClient = _httpClientFactory.CreateClient("CoinGecko");
            var result = await action(httpClient);
            _lastRequestTime = DateTime.UtcNow;
            return result;
        }
        finally
        {
            Throttle.Release();
        }
    }

    /// <summary>
    /// 获取市场数据（支持筛选）
    /// </summary>
    public async Task<List<CoinGeckoMarket>> GetCoinsMarketsAsync(
        string vsCurrency = "usd",
        string? category = null,
        string? order = "market_cap_desc",
        int perPage = 100,
        int page = 1,
        string? priceChangePercentage = null,
        CancellationToken cancellationToken = default)
    {
        var queryParams = new List<string>
        {
            $"vs_currency={vsCurrency}",
            $"order={order}",
            $"per_page={perPage}",
            $"page={page}",
            "sparkline=false"
        };

        if (!string.IsNullOrWhiteSpace(category))
            queryParams.Add($"category={category}");
        if (!string.IsNullOrWhiteSpace(priceChangePercentage))
            queryParams.Add($"price_change_percentage={priceChangePercentage}");

        var url = $"/coins/markets?{string.Join("&", queryParams)}";
        _logger.LogDebug("调用CoinGecko API: {Url}", url);

        return await ThrottledExecuteAsync(async httpClient =>
        {
            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var markets = await response.Content.ReadFromJsonAsync<List<CoinGeckoMarket>>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            _logger.LogInformation("成功获取CoinGecko市场数据，币种数量: {Count}", markets?.Count ?? 0);
            return markets ?? new List<CoinGeckoMarket>();
        }, cancellationToken);
    }

    /// <summary>
    /// 获取单币种市场数据（含价格变化百分比）
    /// </summary>
    public async Task<JsonArray?> GetCoinMarketDataAsync(
        string coinId,
        string vsCurrency = "usd",
        string priceChangePercentage = "24h,7d,30d",
        CancellationToken cancellationToken = default)
    {
        var url = $"/coins/markets?vs_currency={vsCurrency}&ids={coinId}&order=market_cap_desc&sparkline=false&price_change_percentage={priceChangePercentage}";
        _logger.LogDebug("调用CoinGecko单币种API: {CoinId}", coinId);

        return await ThrottledExecuteAsync(async httpClient =>
            await httpClient.GetFromJsonAsync<JsonArray>(url, cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// 获取币种在各交易所的交易对数据
    /// </summary>
    public async Task<CoinGeckoTickersResponse?> GetCoinTickersAsync(
        string coinId,
        CancellationToken cancellationToken = default)
    {
        var url = $"/coins/{coinId}/tickers?include_exchange_logo=false";
        _logger.LogDebug("调用CoinGecko Tickers API: {CoinId}", coinId);

        return await ThrottledExecuteAsync(async httpClient =>
            await httpClient.GetFromJsonAsync<CoinGeckoTickersResponse>(
                url,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken),
            cancellationToken);
    }

    /// <summary>
    /// 搜索币种
    /// </summary>
    public async Task<CoinGeckoSearchResponse?> SearchCoinsAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var url = $"/search?query={Uri.EscapeDataString(query)}";
        _logger.LogDebug("调用CoinGecko Search API: {Query}", query);

        return await ThrottledExecuteAsync(async httpClient =>
        {
            var response = await httpClient.GetFromJsonAsync<CoinGeckoSearchResponse>(
                url,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                cancellationToken);

            _logger.LogInformation("CoinGecko搜索结果: {Count}个币种", response?.Coins?.Count ?? 0);
            return response;
        }, cancellationToken);
    }
}

/// <summary>
/// CoinGecko市场数据模型
/// </summary>
public class CoinGeckoMarket
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public decimal? Current_Price { get; set; }
    public decimal? Market_Cap { get; set; }
    public int? Market_Cap_Rank { get; set; }
    public decimal? Fully_Diluted_Valuation { get; set; }
    public decimal? Total_Volume { get; set; }
    public decimal? High_24h { get; set; }
    public decimal? Low_24h { get; set; }
    public decimal? Price_Change_24h { get; set; }
    public decimal? Price_Change_Percentage_24h { get; set; }
    public decimal? Market_Cap_Change_24h { get; set; }
    public decimal? Market_Cap_Change_Percentage_24h { get; set; }
    public decimal? Circulating_Supply { get; set; }
    public decimal? Total_Supply { get; set; }
    public decimal? Max_Supply { get; set; }
    public decimal? Ath { get; set; }
    public decimal? Ath_Change_Percentage { get; set; }
    public DateTime? Ath_Date { get; set; }
    public decimal? Atl { get; set; }
    public decimal? Atl_Change_Percentage { get; set; }
    public DateTime? Atl_Date { get; set; }
    public DateTime? Last_Updated { get; set; }

    public decimal? Price_Change_Percentage_7d_In_Currency { get; set; }
    public decimal? Price_Change_Percentage_30d_In_Currency { get; set; }
}
