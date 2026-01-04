using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Telegrams;

/// <summary>
/// 虚拟币市场快讯服务，调用 PANews 快讯API（中文）
/// 数据来源：https://www.panewslab.com/zh/newsflash
/// </summary>
public class CryptoTelegramService : ITelegramService
{
    private readonly ILogger<CryptoTelegramService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // PANews 快讯API - rn=条数, lid=语言(1=中文), apppush=0
    private const string ApiUrl = "https://api.panewslab.com/webapi/flashnews?rn=20&lid=1&apppush=0";

    public CryptoTelegramService(
        ILogger<CryptoTelegramService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 通过 PANews API 获取虚拟币市场实时快讯（中文）
    /// </summary>
    public async Task<List<Telegram>> GetTelegraphsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<Telegram>();
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            using var request = new HttpRequestMessage(HttpMethod.Get, ApiUrl);
            request.Headers.TryAddWithoutValidation("Accept", "*/*");
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en-GB;q=0.8,en;q=0.7");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36");
            request.Headers.TryAddWithoutValidation("Origin", "https://www.panewslab.com");
            request.Headers.TryAddWithoutValidation("Referer", "https://www.panewslab.com/");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("获取 PANews 快讯失败，状态码: {StatusCode}", (int)response.StatusCode);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var apiResponse = JsonSerializer.Deserialize<PanewsResponse>(json, JsonOptions);

            if (apiResponse?.Data == null || apiResponse.Data.Count == 0)
            {
                _logger.LogWarning("PANews API 返回数据为空");
                return result;
            }

            // 处理所有返回的快讯（已限制 20 条）
            foreach (var item in apiResponse.Data)
            {
                try
                {
                    var timeText = FormatUnixTime(item.PublishTime);
                    var title = item.Title ?? string.Empty;
                    var content = CleanHtmlContent(item.Content);
                    var url = !string.IsNullOrEmpty(item.Link) 
                        ? item.Link 
                        : $"https://www.panewslab.com/zh/articledetails/{item.Id}.html";
                    var cryptos = ExtractCryptoSymbols(content, title);
                    var isImportant = item.ImportLevel > 0; // importlevel > 0 表示重要快讯

                    result.Add(new Telegram
                    {
                        Time = timeText,
                        Title = title,
                        Content = content,
                        Url = url,
                        Stocks = cryptos,  // 存储相关币种符号
                        IsImportant = isImportant
                    });
                }
                catch (Exception mapEx)
                {
                    _logger.LogWarning(mapEx, "映射 PANews 快讯项失败: {Message}", mapEx.Message);
                }
            }

            _logger.LogInformation("成功获取 {Count} 条虚拟币快讯（PANews）", result.Count);
            return result;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("获取虚拟币快讯超时");
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTelegraphsAsync 调用 PANews API 异常: {Message}", ex.Message);
            return result;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// 格式化 Unix 时间戳为本地时间
    /// </summary>
    private static string FormatUnixTime(long? unixSeconds)
    {
        if (unixSeconds == null || unixSeconds == 0)
            return string.Empty;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds.Value).ToLocalTime().ToString("HH:mm:ss");
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// 从HTML内容中提取纯文本标题（取第一句话）
    /// </summary>
    private static string ExtractTitle(string? htmlContent, int maxLength = 50)
    {
        if (string.IsNullOrWhiteSpace(htmlContent))
            return string.Empty;

        // 清除HTML标签
        var text = System.Text.RegularExpressions.Regex.Replace(htmlContent, "<.*?>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        if (string.IsNullOrEmpty(text))
            return string.Empty;

        // 提取第一句话作为标题
        var sentences = text.Split(new[] { '。', '！', '？', '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
        var title = sentences.Length > 0 ? sentences[0].Trim() : text;

        // 限制长度
        if (title.Length > maxLength)
            title = title.Substring(0, maxLength) + "...";

        return title;
    }

    /// <summary>
    /// 清理HTML内容为纯文本
    /// </summary>
    private static string CleanHtmlContent(string? htmlContent, int maxLength = 200)
    {
        if (string.IsNullOrWhiteSpace(htmlContent))
            return string.Empty;

        // 清除HTML标签
        var text = System.Text.RegularExpressions.Regex.Replace(htmlContent, "<.*?>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

        // 限制长度
        if (text.Length > maxLength)
            text = text.Substring(0, maxLength) + "...";

        return text;
    }

    /// <summary>
    /// 从文本中提取加密货币符号
    /// </summary>
    private static List<string> ExtractCryptoSymbols(string content, string title)
    {
        var symbols = new HashSet<string>();
        var text = $"{title} {content}".ToUpperInvariant();

        // 常见加密货币符号及其变体
        var cryptoPatterns = new Dictionary<string, string[]>
        {
            { "BTC", new[] { "BTC", "比特币", "BITCOIN" } },
            { "ETH", new[] { "ETH", "以太坊", "ETHEREUM" } },
            { "USDT", new[] { "USDT", "泰达币", "TETHER" } },
            { "BNB", new[] { "BNB", "币安币", "BINANCE" } },
            { "SOL", new[] { "SOL", "SOLANA", "索拉纳" } },
            { "XRP", new[] { "XRP", "瑞波币", "RIPPLE" } },
            { "ADA", new[] { "ADA", "艾达币", "CARDANO" } },
            { "DOGE", new[] { "DOGE", "狗狗币", "DOGECOIN" } },
            { "MATIC", new[] { "MATIC", "POLYGON", "马蹄" } },
            { "DOT", new[] { "DOT", "波卡", "POLKADOT" } },
            { "AVAX", new[] { "AVAX", "雪崩", "AVALANCHE" } },
            { "SHIB", new[] { "SHIB", "柴犬币" } },
            { "LTC", new[] { "LTC", "莱特币", "LITECOIN" } },
            { "UNI", new[] { "UNI", "UNISWAP" } },
            { "LINK", new[] { "LINK", "CHAINLINK" } }
        };

        foreach (var (symbol, patterns) in cryptoPatterns)
        {
            if (patterns.Any(p => text.Contains(p)))
            {
                symbols.Add(symbol);
            }
        }

        return symbols.OrderBy(s => s).ToList();
    }

    /// <summary>
    /// PANews API 响应模型
    /// </summary>
    private class PanewsResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("data")]
        public List<PanewsFlashItem> Data { get; set; } = new();

        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }
    }

    /// <summary>
    /// PANews 快讯条目
    /// </summary>
    private class PanewsFlashItem
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("link")]
        public string? Link { get; set; }

        [JsonPropertyName("posttime")]
        public long? PublishTime { get; set; }

        [JsonPropertyName("importlevel")]
        public int ImportLevel { get; set; }  // 0=普通，>0=重要

        [JsonPropertyName("source")]
        public string? Source { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("icon")]
        public string? Icon { get; set; }

        [JsonPropertyName("readnum")]
        public int? ReadNum { get; set; }

        [JsonPropertyName("isimportant")]
        public bool? IsImportant { get; set; }
    }
}

