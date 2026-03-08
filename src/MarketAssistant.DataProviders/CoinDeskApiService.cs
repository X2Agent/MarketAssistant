using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MarketAssistant.Agents.Tools.Models.Crypto.CoinDesk;

namespace MarketAssistant.Services.Data;

/// <summary>
/// CoinDesk API服务
/// </summary>
public sealed class CoinDeskApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CoinDeskApiService> _logger;

    public CoinDeskApiService(
        IHttpClientFactory httpClientFactory,
        ILogger<CoinDeskApiService> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 获取资产元数据（项目基本面信息）
    /// </summary>
    public async Task<CoinDeskMetadataResponse?> GetAssetMetadataAsync(
        string symbol,
        string quoteAsset = "USD",
        string language = "en-US",
        CancellationToken cancellationToken = default)
    {
        var upperSymbol = symbol.ToUpper();
        var url = $"/asset/v2/metadata?assets={upperSymbol}&asset_lookup_priority=SYMBOL&quote_asset={quoteAsset}&asset_language={language}";

        _logger.LogDebug("调用CoinDesk Metadata API: {Symbol}", upperSymbol);

        using var httpClient = _httpClientFactory.CreateClient("CoinDesk");
        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var metadata = await response.Content.ReadFromJsonAsync<CoinDeskMetadataResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        _logger.LogInformation("成功获取CoinDesk元数据: {Symbol}", upperSymbol);
        return metadata;
    }

    /// <summary>
    /// 搜索新闻
    /// </summary>
    public async Task<CoinDeskNewsResponse?> SearchNewsAsync(
        string searchString,
        int limit = 10,
        string lang = "EN",
        string sourceKey = "coindesk",
        CancellationToken cancellationToken = default)
    {
        var url = $"/news/v1/search?search_string={Uri.EscapeDataString(searchString)}&limit={limit}&lang={lang}&source_key={sourceKey}";

        _logger.LogDebug("调用CoinDesk News API: {SearchString}", searchString);

        using var httpClient = _httpClientFactory.CreateClient("CoinDesk");
        var response = await httpClient.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        var newsResponse = await response.Content.ReadFromJsonAsync<CoinDeskNewsResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        _logger.LogInformation("成功获取CoinDesk新闻，结果数: {Count}", newsResponse?.Data?.Count ?? 0);
        return newsResponse;
    }
}
