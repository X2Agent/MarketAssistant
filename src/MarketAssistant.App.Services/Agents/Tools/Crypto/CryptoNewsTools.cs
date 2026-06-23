using System.ServiceModel.Syndication;
using System.Xml;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币新闻数据工具实现（使用 CoinTelegraph RSS 免费源）
///
/// 设计说明：
/// 原 CoinDesk News API v1 已启用企业鉴权（401），免费 Personal 计划仅含历史价格数据。
/// 切换到 CoinTelegraph RSS（https://cointelegraph.com/rss）：
/// - 优点：免费、稳定、无需密钥
/// - 缺点：无法精确搜索，需在客户端按 assetSymbol 过滤标题/摘要
/// </summary>
public sealed class CryptoNewsTools : INewsDataTools
{
    private const string RssFeedUrl = "https://cointelegraph.com/rss";

    private readonly ILogger<CryptoNewsTools> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICryptoAliasRegistry _aliasRegistry;

    public CryptoNewsTools(
        ILogger<CryptoNewsTools> logger,
        IHttpClientFactory httpClientFactory,
        ICryptoAliasRegistry aliasRegistry)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _aliasRegistry = aliasRegistry;
    }

    /// <summary>
    /// 获取虚拟币相关新闻（从 CoinTelegraph RSS 拉取并按币种过滤）
    /// </summary>
    [Description("获取虚拟币相关的最新新闻")]
    public Task<List<NewsItem>> GetNewsAsync(
        [Description("虚拟币代码（如BTC、ETH）")] string assetSymbol,
        int count = 10,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = ExtractBaseCurrency(assetSymbol);
        _logger.LogInformation("正在获取虚拟币新闻（RSS）: {Symbol} (query={Query})", assetSymbol, searchQuery);

        return ToolExecutor.ExecuteAsync(
            operationName: "获取虚拟币新闻",
            assetSymbol: assetSymbol,
            logger: _logger,
            action: async ct =>
            {
                var allItems = await FetchRssFeedAsync(ct);
                var filtered = await FilterBySymbolAsync(allItems, searchQuery, count, ct);

                _logger.LogInformation("成功获取虚拟币新闻: {Symbol}, 数量: {Count}/{Total}", assetSymbol, filtered.Count, allItems.Count);
                return filtered;
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// 拉取并解析 RSS Feed
    /// </summary>
    private async Task<List<NewsItem>> FetchRssFeedAsync(CancellationToken cancellationToken)
    {
        using var httpClient = _httpClientFactory.CreateClient("CryptoNewsRss");
        httpClient.Timeout = TimeSpan.FromSeconds(15);

        using var response = await httpClient.GetAsync(RssFeedUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var xmlReader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            IgnoreWhitespace = true
        });

        var feed = SyndicationFeed.Load(xmlReader);
        var items = new List<NewsItem>();

        foreach (var item in feed.Items)
        {
            var title = item.Title?.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(title))
                continue;

            var link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? item.Id ?? "";
            var summary = item.Summary?.Text?.Trim() ?? "";
            var publishTime = item.PublishDate.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

            items.Add(new NewsItem
            {
                Title = title,
                Link = link,
                Source = "CoinTelegraph",
                PublishTime = publishTime,
                Summary = summary
            });
        }

        return items;
    }

    /// <summary>
    /// 按币种符号过滤新闻（标题或摘要中包含符号或常见别名）
    /// </summary>
    private async Task<List<NewsItem>> FilterBySymbolAsync(
        List<NewsItem> items, string symbol, int count, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(symbol))
            return items.Take(count).ToList();

        var aliases = await _aliasRegistry.GetAliasesAsync(symbol, cancellationToken);

        var filtered = items
            .Where(item =>
            {
                var title = item.Title?.ToUpperInvariant() ?? "";
                var summary = item.Summary?.ToUpperInvariant() ?? "";
                return aliases.Any(a => title.Contains(a) || summary.Contains(a));
            })
            .Take(count)
            .ToList();

        return filtered.Count > 0 ? filtered : items.Take(count).ToList();
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetNewsAsync);
    }
}
