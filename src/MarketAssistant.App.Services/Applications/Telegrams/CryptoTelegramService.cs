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

        if (apiResponse?.Data == null)
        {
            _logger.LogWarning("PANews API 返回数据为空，ErrorNo: {ErrorNo}, Message: {Message}", 
                apiResponse?.ErrorNo, apiResponse?.Message);
            return result;
        }

        // 从 flashNews 中提取所有 list 集合
        var allItems = apiResponse.Data.FlashNews
            .SelectMany(group => group.List)
            .ToList();

        _logger.LogInformation("PANews API 返回数据：共 {GroupCount} 个日期分组，总计 {ItemCount} 条快讯", 
            apiResponse.Data.FlashNews.Count, allItems.Count);

        if (allItems.Count == 0)
        {
            _logger.LogWarning("PANews API 未返回任何快讯");
            return result;
        }

        // 处理所有返回的快讯（已限制 20 条）
        foreach (var item in allItems.Take(20))
        {
            try
            {
                var timestamp = item.PublishTime ?? item.CTime ?? 0;
                var timeText = FormatUnixTime(timestamp);
                var title = item.Title ?? string.Empty;
                
                // 使用 desc 字段作为内容，如果为空则使用 title
                var content = !string.IsNullOrWhiteSpace(item.Desc) 
                    ? item.Desc 
                    : title;
                
                var url = $"https://www.panewslab.com/zh/articles/{item.Id}";
                
                var cryptos = ExtractCryptoSymbols(content, title);
                
                // type=2 并且 apppush=1 可能表示重要快讯
                var isImportant = item.AppPush == 1;

                _logger.LogDebug("处理快讯项：Title={Title}, Time={Time}, Author={Author}", 
                    title, timeText, item.Author?.Name);

                result.Add(new Telegram
                {
                    Time = timeText,
                    Title = title,
                    Content = content,
                    Url = url,
                    Symbols = cryptos,
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

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// 格式化 Unix 时间戳为本地时间
    /// </summary>
    private static string FormatUnixTime(long unixSeconds)
    {
        if (unixSeconds == 0)
            return string.Empty;

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("HH:mm:ss");
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
        [JsonPropertyName("errno")]
        public int ErrorNo { get; set; }

        [JsonPropertyName("msg")]
        public string? Message { get; set; }

        [JsonPropertyName("data")]
        public PanewsData? Data { get; set; }
    }

    /// <summary>
    /// PANews 数据容器
    /// </summary>
    private class PanewsData
    {
        [JsonPropertyName("flashNews")]
        public List<FlashNewsGroup> FlashNews { get; set; } = new();

        [JsonPropertyName("tag")]
        public List<object> Tag { get; set; } = new();
    }

    /// <summary>
    /// PANews 快讯分组（按日期）
    /// </summary>
    private class FlashNewsGroup
    {
        [JsonPropertyName("date")]
        public string? Date { get; set; }

        [JsonPropertyName("week")]
        public string? Week { get; set; }

        [JsonPropertyName("month")]
        public string? Month { get; set; }

        [JsonPropertyName("unix")]
        public long Unix { get; set; }

        [JsonPropertyName("list")]
        public List<PanewsFlashItem> List { get; set; } = new();
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

        [JsonPropertyName("desc")]
        public string? Desc { get; set; }

        [JsonPropertyName("publishTime")]
        public long? PublishTime { get; set; }

        [JsonPropertyName("type")]
        public int? Type { get; set; }

        [JsonPropertyName("img")]
        public string? Img { get; set; }

        [JsonPropertyName("readnum")]
        public int? ReadNum { get; set; }

        [JsonPropertyName("tags")]
        public object? Tags { get; set; }

        [JsonPropertyName("author")]
        public PanewsAuthor? Author { get; set; }

        [JsonPropertyName("collection")]
        public int? Collection { get; set; }

        [JsonPropertyName("like")]
        public int? Like { get; set; }

        [JsonPropertyName("lovenum")]
        public int? LoveNum { get; set; }

        [JsonPropertyName("status")]
        public int? Status { get; set; }

        [JsonPropertyName("topTime")]
        public long? TopTime { get; set; }

        [JsonPropertyName("apppush")]
        public int? AppPush { get; set; }

        [JsonPropertyName("ctime")]
        public long? CTime { get; set; }
    }

    /// <summary>
    /// PANews 作者信息
    /// </summary>
    private class PanewsAuthor
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("img")]
        public string? Img { get; set; }

        [JsonPropertyName("tag")]
        public string? Tag { get; set; }

        [JsonPropertyName("brief")]
        public string? Brief { get; set; }

        [JsonPropertyName("follow")]
        public int? Follow { get; set; }
    }
}

