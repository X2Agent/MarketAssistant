using AngleSharp.Html.Parser;
using MarketAssistant.Applications.Assets;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;

namespace MarketAssistant.Applications.Telegrams;

/// <summary>
/// 虚拟币市场快讯服务，调用 Odaily 星球日报官方快讯 API（中文，唯一来源）
/// 数据来源：https://www.odaily.news/zh-CN/newsflash
/// </summary>
public class CryptoTelegramService(
    ILogger<CryptoTelegramService> logger,
    IHttpClientFactory httpClientFactory,
    ICryptoAliasRegistry aliasRegistry) : ITelegramService
{
    private readonly ILogger<CryptoTelegramService> _logger = logger;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ICryptoAliasRegistry _aliasRegistry = aliasRegistry;

    // size=100 是服务端上限（传更大值仍只返回 100 条），实测覆盖约 17 小时，其中约一半为重要快讯。
    private const string RequestUri = "api/v1/newsflash?page=1&size=100&lang=zh-cn";

    // 正文几乎都以该固定前缀开头，对读者与模型都是噪声。
    private const string ContentPrefix = "Odaily星球日报讯";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// 通过 Odaily API 获取虚拟币市场实时快讯（中文）。
    /// 接口服务端会忽略 isImportant 参数，故在本地只保留编辑标记为重要的快讯；
    /// 非 2xx 与 HTTP 200 承载的业务错误（success=false）都会抛出，空列表仅表示确实没有快讯。
    /// </summary>
    public async Task<List<Telegram>> GetTelegraphsAsync(CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("CryptoTelegram");
        var payload = await client.GetFromJsonAsync<OdailyResponse>(RequestUri, JsonOptions, cancellationToken);

        if (payload is not { Success: true, Data.List: { } flashes })
        {
            throw new InvalidOperationException(
                $"Odaily 快讯接口返回错误: code={payload?.Code}, msg={payload?.Msg}");
        }

        var parser = new HtmlParser();
        var result = new List<Telegram>();
        foreach (var flash in flashes.Where(flash => flash.IsImportant))
        {
            var content = parser.ParseDocument(flash.Content).Body!.TextContent.Trim();
            if (content.StartsWith(ContentPrefix, StringComparison.Ordinal))
            {
                content = content[ContentPrefix.Length..].Trim();
            }

            result.Add(new Telegram
            {
                Time = DateTimeOffset.FromUnixTimeMilliseconds(flash.PublishTimestamp).ToLocalTime().ToString("HH:mm:ss"),
                Title = flash.Title,
                Content = content,
                Url = flash.Link,
                Symbols = await ExtractCryptoSymbolsAsync(flash.Title, content, cancellationToken),
                IsImportant = flash.IsImportant
            });
        }

        _logger.LogInformation("成功获取 {Count} 条虚拟币快讯（Odaily）", result.Count);
        return result;
    }

    /// <summary>
    /// 从新闻文本中提取关联的加密货币符号（基于词边界正则匹配，动态从 CryptoAliasRegistry 获取）
    /// </summary>
    private async Task<List<string>> ExtractCryptoSymbolsAsync(
        string title, string content, CancellationToken cancellationToken)
    {
        var patterns = await _aliasRegistry.GetMatchPatternsAsync(cancellationToken);
        var text = $"{title} {content}";
        return [.. patterns
            .Where(kv => kv.Value.IsMatch(text))
            .Select(kv => kv.Key)
            .OrderBy(s => s)];
    }

    private sealed class OdailyResponse
    {
        public int Code { get; set; }

        public string? Msg { get; set; }

        public bool Success { get; set; }

        public OdailyData? Data { get; set; }
    }

    private sealed class OdailyData
    {
        public List<OdailyFlash>? List { get; set; }
    }

    private sealed class OdailyFlash
    {
        public string Title { get; set; } = string.Empty;

        public string Content { get; set; } = string.Empty;

        public bool IsImportant { get; set; }

        public long PublishTimestamp { get; set; }

        public string Link { get; set; } = string.Empty;
    }
}
