using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币新闻数据工具实现（使用 CryptoCompare API 免费模式）
/// </summary>
public sealed class CryptoNewsTools : INewsDataTools
{
    private readonly ILogger<CryptoNewsTools> _logger;
    private readonly HttpClient _httpClient;
    private const string CRYPTOCOMPARE_API_BASE_URL = "https://min-api.cryptocompare.com/data/v2/news";

    public CryptoNewsTools(ILogger<CryptoNewsTools> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// 获取虚拟币相关新闻（使用 CryptoCompare API）
    /// </summary>
    [Description("获取虚拟币相关的最新新闻")]
    public async Task<List<NewsItem>> GetNewsAsync(
        [Description("虚拟币代码（如BTC、ETH）")] string assetSymbol, 
        int count = 10)
    {
        try
        {
            // 格式化币种代码
            var category = FormatSymbolForNews(assetSymbol);
            
            // 构建请求 URL（使用英文，给AI模型使用）
            var url = $"{CRYPTOCOMPARE_API_BASE_URL}/?categories={category}&lang=EN&sortOrder=latest";

            _logger.LogInformation("正在获取虚拟币新闻（AI Tools用）: {Symbol} (category={Category})", assetSymbol, category);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            var newsResponse = JsonSerializer.Deserialize<CryptoCompareNewsResponse>(content);

            if (newsResponse?.Data == null || newsResponse.Data.Count == 0)
            {
                _logger.LogWarning("未找到虚拟币新闻: {Symbol}", assetSymbol);
                return new List<NewsItem>();
            }

            // 映射到 NewsItem 模型并限制数量
            var newsItems = newsResponse.Data
                .Take(count)
                .Select(article => new NewsItem
                {
                    Title = article.Title ?? "无标题",
                    Source = article.Source ?? "未知来源",
                    Link = article.Url ?? "",
                    PublishTime = ConvertUnixTimestamp(article.PublishedOn),
                    Summary = TruncateText(article.Body ?? "", 300)
                })
                .ToList();

            _logger.LogInformation("成功获取虚拟币新闻: {Symbol}, 数量: {Count}", assetSymbol, newsItems.Count);

            return newsItems;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用 CryptoCompare API 获取新闻失败: {Symbol}", assetSymbol);
            throw new InvalidOperationException($"获取虚拟币新闻失败: {assetSymbol}，请检查网络连接", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取虚拟币新闻时发生错误: {Symbol}", assetSymbol);
            throw;
        }
    }

    /// <summary>
    /// 格式化币种代码为新闻分类
    /// </summary>
    private string FormatSymbolForNews(string symbol)
    {
        // 移除 USDT 后缀，转大写
        return symbol.ToUpper().Replace("USDT", "").Trim();
    }

    /// <summary>
    /// 转换 Unix 时间戳为本地时间字符串
    /// </summary>
    private string ConvertUnixTimestamp(long unixTimestamp)
    {
        var dateTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).ToLocalTime();
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    /// <summary>
    /// 截断文本到指定长度
    /// </summary>
    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text.Substring(0, maxLength) + "...";
    }

    #region CryptoCompare API 响应模型

    /// <summary>
    /// CryptoCompare 新闻响应模型
    /// </summary>
    private class CryptoCompareNewsResponse
    {
        [JsonPropertyName("Data")]
        public List<NewsArticle>? Data { get; set; }
    }

    /// <summary>
    /// 新闻文章模型
    /// </summary>
    private class NewsArticle
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("body")]
        public string Body { get; set; } = string.Empty;

        [JsonPropertyName("url")]
        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;

        [JsonPropertyName("published_on")]
        public long PublishedOn { get; set; }

        [JsonPropertyName("imageurl")]
        public string? ImageUrl { get; set; }

        [JsonPropertyName("categories")]
        public string? Categories { get; set; }
    }

    #endregion

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetNewsAsync);
    }
}





