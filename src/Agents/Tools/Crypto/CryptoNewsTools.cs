using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.Agents.Tools.Models.Crypto.CoinDesk;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using System.Net.Http.Json;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币新闻数据工具实现（使用 CoinDesk News API v1）
/// </summary>
public sealed class CryptoNewsTools : INewsDataTools
{
    private readonly ILogger<CryptoNewsTools> _logger;
    private readonly HttpClient _httpClient;
    private const string COINDESK_API_BASE_URL = "https://data-api.coindesk.com/news/v1/search";

    public CryptoNewsTools(ILogger<CryptoNewsTools> logger)
    {
        _logger = logger;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    /// <summary>
    /// 获取虚拟币相关新闻（使用 CoinDesk News API v1）
    /// </summary>
    [Description("获取虚拟币相关的最新新闻")]
    public async Task<List<NewsItem>> GetNewsAsync(
        [Description("虚拟币代码（如BTC、ETH）")] string assetSymbol,
        int count = 10)
    {
        try
        {
            // 格式化币种代码
            var searchQuery = ExtractBaseCurrency(assetSymbol);

            // 构建请求 URL
            var url = $"{COINDESK_API_BASE_URL}?search_string={assetSymbol}&limit={count}&lang=EN&source_key=coindesk";

            _logger.LogInformation("正在获取虚拟币新闻（AI Tools用）: {Symbol} (query={Query})", assetSymbol, searchQuery);

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var newsResponse = await response.Content.ReadFromJsonAsync<CoinDeskNewsResponse>();

            if (newsResponse?.Data == null || newsResponse.Data.Count == 0)
            {
                _logger.LogWarning("未找到虚拟币新闻: {Symbol}", assetSymbol);
                return new List<NewsItem>();
            }

            // 映射到 NewsItem 模型
            var newsItems = newsResponse.Data
                .Select(article => new NewsItem
                {
                    Title = article.Title,
                    Source = article.Source?.Name ?? article.Authors,
                    Link = article.Url,
                    PublishTime = ConvertUnixTimestamp(article.CreatedOn),
                    Summary = article.Body
                })
                .ToList();

            _logger.LogInformation("成功获取虚拟币新闻: {Symbol}, 数量: {Count}", assetSymbol, newsItems.Count);

            return newsItems;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "调用 CoinDesk API 获取新闻失败: {Symbol}", assetSymbol);
            throw new InvalidOperationException($"获取虚拟币新闻失败: {assetSymbol}，请检查网络连接", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取虚拟币新闻时发生错误: {Symbol}", assetSymbol);
            throw;
        }
    }

    /// <summary>
    /// 转换 Unix 时间戳为本地时间字符串
    /// </summary>
    private string ConvertUnixTimestamp(long unixTimestamp)
    {
        var dateTime = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp).ToLocalTime();
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetNewsAsync);
    }
}





