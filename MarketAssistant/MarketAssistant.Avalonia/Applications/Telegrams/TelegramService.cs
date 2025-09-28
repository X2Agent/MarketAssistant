using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Telegrams;

public class TelegramService
{
    private readonly ILogger<TelegramService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public TelegramService(ILogger<TelegramService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// 通过同花顺快讯API获取实时新闻数据
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>实时新闻列表</returns>
    public async Task<List<Telegram>> GetTelegraphsAsync(CancellationToken cancellationToken = default)
    {
        // var url = "https://news.10jqka.com.cn/realtimenews.html";
        var result = new List<Telegram>();
        try
        {
            var client = _httpClientFactory.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://news.10jqka.com.cn/tapp/news/push/stock/?page=1&track=website&pagesize=20");
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("获取同花顺快讯API失败，状态码: {StatusCode}", (int)response.StatusCode);
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var api = JsonSerializer.Deserialize<ThsNewsResponse>(json, JsonOptions);
            if (api?.Data?.List == null)
            {
                _logger.LogWarning("快讯API解析为空或结构不匹配");
                return result;
            }

            foreach (var item in api.Data.List)
            {
                try
                {
                    var timeText = TryFormatUnixTime(item.Ctime);
                    var title = item.Title ?? string.Empty;
                    var content = !string.IsNullOrWhiteSpace(item.Short) ? item.Short! : (item.Digest ?? string.Empty);
                    var url = item.Url ?? item.AppUrl ?? item.ShareUrl ?? string.Empty;
                    var isImportant = ParseImportance(item.Import) || ParseColorImportant(item.Color);
                    var stocks = (item.Stock ?? new List<ThsNewsStock>())
                        .Select(s => s.Name?.Trim())
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!)
                        .ToList();

                    result.Add(new Telegram
                    {
                        Time = timeText,
                        Title = title,
                        Content = content,
                        Url = url,
                        Stocks = stocks,
                        IsImportant = isImportant
                    });
                }
                catch (Exception mapEx)
                {
                    _logger.LogWarning(mapEx, "映射快讯项失败: {Message}", mapEx.Message);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GetTelegraphsAsync 调用API异常: {Message}", ex.Message);
            return result;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    private static string TryFormatUnixTime(string? unixSeconds)
    {
        if (long.TryParse(unixSeconds, out var seconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds).ToLocalTime().ToString("HH:mm:ss");
            }
            catch
            {
            }
        }
        return string.Empty;
    }

    private static bool ParseImportance(string? import)
    {
        if (string.IsNullOrWhiteSpace(import)) return false;
        return int.TryParse(import, out var val) && val > 0;
    }

    private static bool ParseColorImportant(string? color)
    {
        // 某些返回用颜色标记重要性，保底处理
        return color == "2" || color == "3";
    }

    private class ThsNewsResponse
    {
        public string? Code { get; set; }
        public string? Msg { get; set; }
        public string? Time { get; set; }
        public ThsNewsData? Data { get; set; }
    }

    private class ThsNewsData
    {
        public List<ThsNewsItem>? List { get; set; }
    }

    private class ThsNewsItem
    {
        public string? Id { get; set; }
        public string? Seq { get; set; }
        public string? Title { get; set; }
        public string? Digest { get; set; }
        public string? Url { get; set; }
        public string? AppUrl { get; set; }
        public string? ShareUrl { get; set; }
        public string? Color { get; set; }
        public string? Tag { get; set; }
        public List<object>? Tags { get; set; }
        public string? Ctime { get; set; }
        public string? Rtime { get; set; }
        public string? Source { get; set; }
        public string? PicUrl { get; set; }
        public string? Nature { get; set; }
        public List<ThsNewsStock>? Stock { get; set; }
        public List<object>? Field { get; set; }
        public string? Short { get; set; }
        public string? Import { get; set; }
        public List<object>? TagInfo { get; set; }
    }

    private class ThsNewsStock
    {
        public string? Name { get; set; }
        public string? StockCode { get; set; }
        public string? StockMarket { get; set; }
    }
}
