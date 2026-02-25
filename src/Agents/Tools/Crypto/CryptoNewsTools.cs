using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Data;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币新闻数据工具实现（使用 CoinDesk News API v1）
/// </summary>
public sealed class CryptoNewsTools : INewsDataTools
{
    private readonly ILogger<CryptoNewsTools> _logger;
    private readonly CoinDeskApiService _coinDeskService;

    public CryptoNewsTools(
        ILogger<CryptoNewsTools> logger,
        CoinDeskApiService coinDeskService)
    {
        _logger = logger;
        _coinDeskService = coinDeskService;
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
            // 提取基础币种（如 BTCUSDT → BTC）
            var searchQuery = ExtractBaseCurrency(assetSymbol);

            _logger.LogInformation("正在获取虚拟币新闻（AI Tools用）: {Symbol} (query={Query})", assetSymbol, searchQuery);

            var newsResponse = await _coinDeskService.SearchNewsAsync(searchQuery, count);

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
            throw new FriendlyException($"获取虚拟币新闻失败: {assetSymbol}，请检查网络连接", ex);
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





