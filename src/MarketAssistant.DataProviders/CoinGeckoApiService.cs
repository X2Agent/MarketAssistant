using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;


namespace MarketAssistant.DataProviders;

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

    // 支持 API 返回的字符串数值/null 自动容错转换为 decimal?（CoinGeckoMarket 全为 decimal?）
    private static readonly JsonSerializerOptions CoinGeckoJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new StringToDecimalConverter() }
    };

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

        var url = $"coins/markets?{string.Join("&", queryParams)}";
        _logger.LogDebug("调用CoinGecko API: {Url}", url);

        return await ThrottledExecuteAsync(async httpClient =>
        {
            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var markets = await response.Content.ReadFromJsonAsync<List<CoinGeckoMarket>>(
                CoinGeckoJsonOptions,
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
        var url = $"coins/markets?vs_currency={vsCurrency}&ids={coinId}&order=market_cap_desc&sparkline=false&price_change_percentage={priceChangePercentage}";
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
        var url = $"coins/{coinId}/tickers?include_exchange_logo=false";
        _logger.LogDebug("调用CoinGecko Tickers API: {CoinId}", coinId);

        return await ThrottledExecuteAsync(async httpClient =>
            await httpClient.GetFromJsonAsync<CoinGeckoTickersResponse>(
                url,
                CoinGeckoJsonOptions,
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
        var url = $"search?query={Uri.EscapeDataString(query)}";
        _logger.LogDebug("调用CoinGecko Search API: {Query}", query);

        return await ThrottledExecuteAsync(async httpClient =>
        {
            var response = await httpClient.GetFromJsonAsync<CoinGeckoSearchResponse>(
                url,
                CoinGeckoJsonOptions,
                cancellationToken);

            _logger.LogInformation("CoinGecko搜索结果: {Count}个币种", response?.Coins?.Count ?? 0);
            return response;
        }, cancellationToken);
    }

    /// <summary>
    /// 根据币种符号获取完整项目数据（含描述、分类、市场数据、社区数据、开发者数据）
    /// 内部通过 /coins/list 建立 symbol→id 映射，再调用 /coins/{id} 获取详情
    /// </summary>
    public async Task<CoinGeckoCoinDetail?> GetCoinBySymbolAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
            throw new ArgumentException("币种符号不能为空", nameof(symbol));

        // 提取基础币种（BTCUSDT → BTC）
        var baseSymbol = MarketAssistant.Infrastructure.Core.CryptoSymbolConverter.ExtractBaseCurrency(symbol);
        if (string.IsNullOrEmpty(baseSymbol))
            baseSymbol = symbol.ToUpperInvariant();

        var coinId = await ResolveCoinIdAsync(baseSymbol, cancellationToken);
        if (string.IsNullOrEmpty(coinId))
        {
            _logger.LogWarning("无法解析 CoinGecko coinId: {Symbol}", baseSymbol);
            return null;
        }

        var url = $"coins/{coinId}?localization=true&tickers=false&market_data=true&community_data=true&developer_data=true&sparkline=false";
        _logger.LogDebug("调用CoinGecko /coins/{{id}}: {CoinId}", coinId);

        return await ThrottledExecuteAsync(async httpClient =>
        {
            var response = await httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            var detail = await response.Content.ReadFromJsonAsync<CoinGeckoCoinDetail>(
                CoinGeckoJsonOptions, cancellationToken);
            _logger.LogInformation("成功获取CoinGecko项目详情: {Name} ({Id})", detail?.Name, coinId);
            return detail;
        }, cancellationToken);
    }

    /// <summary>
    /// 解析 symbol → coinId（先查内置映射表，未命中则调用 /coins/list）
    /// </summary>
    private async Task<string?> ResolveCoinIdAsync(string baseSymbol, CancellationToken cancellationToken)
    {
        // 优先使用内置映射表（覆盖主流币种，避免一次 HTTP 请求）
        var mappedId = MarketAssistant.Infrastructure.Core.CryptoSymbolConverter.ToCoinGeckoId(baseSymbol);
        if (!string.IsNullOrEmpty(mappedId) && !string.Equals(mappedId, baseSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return mappedId;
        }

        // 未命中映射表，调用 /coins/list 查询
        try
        {
            return await ThrottledExecuteAsync(async httpClient =>
            {
                var response = await httpClient.GetAsync("coins/list", cancellationToken);
                response.EnsureSuccessStatusCode();

                var coins = await response.Content.ReadFromJsonAsync<List<CoinGeckoCoinListItem>>(
                    CoinGeckoJsonOptions, cancellationToken);

                var hit = coins?.FirstOrDefault(c =>
                    string.Equals(c.Symbol, baseSymbol, StringComparison.OrdinalIgnoreCase));
                return hit?.Id;
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "调用 /coins/list 解析 coinId 失败: {Symbol}", baseSymbol);
            return null;
        }
    }
}

/// <summary>
/// CoinGecko /coins/{id} 端点返回的完整币种详情
/// </summary>
public class CoinGeckoCoinDetail
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> Categories { get; set; } = [];
    public CoinGeckoCoinDescription? Description { get; set; }
    [JsonPropertyName("links")]
    public CoinGeckoCoinLinks? Links { get; set; }
    [JsonPropertyName("market_data")]
    public CoinGeckoCoinMarketData? MarketData { get; set; }
    [JsonPropertyName("community_data")]
    public CoinGeckoCoinCommunityData? CommunityData { get; set; }
    [JsonPropertyName("developer_data")]
    public CoinGeckoCoinDeveloperData? DeveloperData { get; set; }
    [JsonPropertyName("market_cap_rank")]
    public int? MarketCapRank { get; set; }
    [JsonPropertyName("coingecko_rank")]
    public int? CoingeckoRank { get; set; }
    [JsonPropertyName("coingecko_score")]
    public decimal? CoingeckoScore { get; set; }
    [JsonPropertyName("sentiment_votes_up_percentage")]
    public decimal? SentimentVotesUpPercentage { get; set; }
    [JsonPropertyName("sentiment_votes_down_percentage")]
    public decimal? SentimentVotesDownPercentage { get; set; }
}

public class CoinGeckoCoinDescription
{
    public string En { get; set; } = string.Empty;
}

public class CoinGeckoCoinLinks
{
    public List<string> Homepage { get; set; } = [];
    [JsonPropertyName("twitter_screen_name")]
    public string TwitterScreenName { get; set; } = string.Empty;
    [JsonPropertyName("subreddit_url")]
    public string SubredditUrl { get; set; } = string.Empty;
    [JsonPropertyName("repos_url")]
    public CoinGeckoCoinReposUrl? ReposUrl { get; set; }
}

public class CoinGeckoCoinReposUrl
{
    public List<string> Github { get; set; } = [];
    public List<string> Bitbucket { get; set; } = [];
}

public class CoinGeckoCoinMarketData
{
    [JsonPropertyName("current_price")]
    public Dictionary<string, decimal?> CurrentPrice { get; set; } = [];
    [JsonPropertyName("market_cap")]
    public Dictionary<string, decimal?> MarketCap { get; set; } = [];
    [JsonPropertyName("total_volume")]
    public Dictionary<string, decimal?> TotalVolume { get; set; } = [];
    [JsonPropertyName("circulating_supply")]
    public decimal? CirculatingSupply { get; set; }
    [JsonPropertyName("total_supply")]
    public decimal? TotalSupply { get; set; }
    [JsonPropertyName("max_supply")]
    public decimal? MaxSupply { get; set; }
    [JsonPropertyName("price_change_percentage_24h_in_currency")]
    public Dictionary<string, decimal?> PriceChangePercentage24hInCurrency { get; set; } = [];
    [JsonPropertyName("price_change_percentage_7d_in_currency")]
    public Dictionary<string, decimal?> PriceChangePercentage7dInCurrency { get; set; } = [];
    [JsonPropertyName("price_change_percentage_30d_in_currency")]
    public Dictionary<string, decimal?> PriceChangePercentage30dInCurrency { get; set; } = [];
}

public class CoinGeckoCoinCommunityData
{
    [JsonPropertyName("twitter_followers")]
    public long? TwitterFollowers { get; set; }
    [JsonPropertyName("reddit_subscribers")]
    public long? RedditSubscribers { get; set; }
    [JsonPropertyName("reddit_average_posts_24h")]
    public decimal? RedditAveragePosts24h { get; set; }
    [JsonPropertyName("reddit_average_comments_24h")]
    public decimal? RedditAverageComments24h { get; set; }
    [JsonPropertyName("reddit_accounts_active_48h")]
    public decimal? RedditAccountsActive48h { get; set; }
}

public class CoinGeckoCoinDeveloperData
{
    public int? Forks { get; set; }
    public int? Stars { get; set; }
    public int? Subscribers { get; set; }
    [JsonPropertyName("total_issues")]
    public int? TotalIssues { get; set; }
    [JsonPropertyName("pull_requests_contributors")]
    public int? PullRequestsContributors { get; set; }
    [JsonPropertyName("commit_count_4_weeks")]
    public int? CommitCount4Weeks { get; set; }
}

public class CoinGeckoCoinListItem
{
    public string Id { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
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

    [JsonPropertyName("current_price")]
    public decimal? CurrentPrice { get; set; }

    [JsonPropertyName("market_cap")]
    public decimal? MarketCap { get; set; }

    [JsonPropertyName("market_cap_rank")]
    public int? MarketCapRank { get; set; }

    [JsonPropertyName("fully_diluted_valuation")]
    public decimal? FullyDilutedValuation { get; set; }

    [JsonPropertyName("total_volume")]
    public decimal? TotalVolume { get; set; }

    [JsonPropertyName("high_24h")]
    public decimal? High24h { get; set; }

    [JsonPropertyName("low_24h")]
    public decimal? Low24h { get; set; }

    [JsonPropertyName("price_change_24h")]
    public decimal? PriceChange24h { get; set; }

    [JsonPropertyName("price_change_percentage_24h")]
    public decimal? PriceChangePercentage24h { get; set; }

    [JsonPropertyName("market_cap_change_24h")]
    public decimal? MarketCapChange24h { get; set; }

    [JsonPropertyName("market_cap_change_percentage_24h")]
    public decimal? MarketCapChangePercentage24h { get; set; }

    [JsonPropertyName("circulating_supply")]
    public decimal? CirculatingSupply { get; set; }

    [JsonPropertyName("total_supply")]
    public decimal? TotalSupply { get; set; }

    [JsonPropertyName("max_supply")]
    public decimal? MaxSupply { get; set; }

    [JsonPropertyName("ath")]
    public decimal? Ath { get; set; }

    [JsonPropertyName("ath_change_percentage")]
    public decimal? AthChangePercentage { get; set; }

    [JsonPropertyName("ath_date")]
    public DateTime? AthDate { get; set; }

    [JsonPropertyName("atl")]
    public decimal? Atl { get; set; }

    [JsonPropertyName("atl_change_percentage")]
    public decimal? AtlChangePercentage { get; set; }

    [JsonPropertyName("atl_date")]
    public DateTime? AtlDate { get; set; }

    [JsonPropertyName("last_updated")]
    public DateTime? LastUpdated { get; set; }

    [JsonPropertyName("price_change_percentage_7d_in_currency")]
    public decimal? PriceChangePercentage7dInCurrency { get; set; }

    [JsonPropertyName("price_change_percentage_30d_in_currency")]
    public decimal? PriceChangePercentage30dInCurrency { get; set; }
}
