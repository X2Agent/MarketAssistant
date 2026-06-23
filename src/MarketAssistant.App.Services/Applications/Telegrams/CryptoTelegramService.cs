using MarketAssistant.Applications.Assets;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MarketAssistant.Applications.Telegrams;

/// <summary>
/// 虚拟币市场快讯服务，调用 PANews 快讯API（中文）
/// 数据来源：https://www.panewslab.com/zh/newsflash
/// </summary>
public class CryptoTelegramService : ITelegramService
{
    private readonly ILogger<CryptoTelegramService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICryptoAliasRegistry _aliasRegistry;

    private const string ApiUrl =
        "https://universal-api.panewslab.com/articles?type=NEWS&isShowInList=true&take=20&skip=0&isImportant=true";

    public CryptoTelegramService(
        ILogger<CryptoTelegramService> logger,
        IHttpClientFactory httpClientFactory,
        ICryptoAliasRegistry aliasRegistry)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _aliasRegistry = aliasRegistry;
    }

    /// <summary>
    /// 通过 PANews API 获取虚拟币市场实时快讯（中文）
    /// </summary>
    public async Task<List<Telegram>> GetTelegraphsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Telegram>();

        var client = _httpClientFactory.CreateClient("CryptoTelegram");

        using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("获取 PANews 快讯失败，状态码: {StatusCode}", (int)response.StatusCode);
            return result;
        }

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var articles = JsonSerializer.Deserialize<List<PanewsArticle>>(json, JsonOptions);

        if (articles is not { Count: > 0 })
        {
            _logger.LogWarning("PANews API 返回数据为空");
            return result;
        }

        _logger.LogInformation("PANews API 返回 {Count} 条快讯", articles.Count);

        foreach (var article in articles)
        {
            try
            {
                var title = article.Title ?? string.Empty;
                var content = !string.IsNullOrWhiteSpace(article.Desc) ? article.Desc : title;

                result.Add(new Telegram
                {
                    Time = FormatIsoTime(article.PublishedAt),
                    Title = title,
                    Content = content,
                    Url = $"https://www.panewslab.com/zh/articles/{article.Id}",
                    Symbols = await ExtractCryptoSymbolsAsync(title, content, cancellationToken),
                    IsImportant = article.IsImportant == true
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "映射 PANews 快讯项失败: {Message}", ex.Message);
            }
        }

        _logger.LogInformation("成功获取 {Count} 条虚拟币快讯（PANews）", result.Count);
        return result;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// 格式化 ISO 8601 时间为本地时间字符串
    /// </summary>
    private static string FormatIsoTime(string? isoTime)
    {
        if (string.IsNullOrWhiteSpace(isoTime))
            return string.Empty;

        try
        {
            return DateTimeOffset.Parse(isoTime).ToLocalTime().ToString("HH:mm:ss");
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 从新闻文本中提取关联的加密货币符号（基于词边界正则匹配，动态从 CryptoAliasRegistry 获取）
    /// </summary>
    private async Task<List<string>> ExtractCryptoSymbolsAsync(
        string title, string content, CancellationToken cancellationToken)
    {
        var patterns = await _aliasRegistry.GetMatchPatternsAsync(cancellationToken);
        var text = $"{title} {content}";
        return patterns
            .Where(kv => kv.Value.IsMatch(text))
            .Select(kv => kv.Key)
            .OrderBy(s => s)
            .ToList();
    }

    // ─────────────────────────────────────────────────────────────
    // PANews API 响应模型
    // ─────────────────────────────────────────────────────────────

    private class PanewsArticle
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("publishedAt")]
        public string? PublishedAt { get; set; }

        [JsonPropertyName("isImportant")]
        public bool? IsImportant { get; set; }

        [JsonPropertyName("author")]
        public PanewsAuthor? Author { get; set; }
    }

    private class PanewsAuthor
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}

