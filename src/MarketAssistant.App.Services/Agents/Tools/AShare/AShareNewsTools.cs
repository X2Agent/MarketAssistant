using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.AShare;

/// <summary>
/// A股新闻数据工具实现
/// </summary>
public sealed class AShareNewsTools(
    EastMoneyNewsClient newsClient,
    ILogger<AShareNewsTools> logger) : INewsDataTools
{
    private readonly EastMoneyNewsClient _newsClient = newsClient;
    private readonly ILogger<AShareNewsTools> _logger = logger;

    /// <summary>
    /// 获取指定股票新闻列表（标题/来源/链接）。
    /// 数据源：东方财富搜索 API；HTTP 访问与 JSONP 解析由 <see cref="EastMoneyNewsClient"/> 负责。
    /// </summary>
    private async Task<List<NewsItem>> GetNewsListAsync(string assetSymbol, CancellationToken cancellationToken = default)
    {
        // 东方财富搜索 API 使用纯数字代码（如 600519）
        var digits = StockSymbolConverter.ToDigitsOnly(assetSymbol);
        if (string.IsNullOrEmpty(digits))
            throw new FriendlyException($"股票代码格式不正确: {assetSymbol}");

        var articles = await _newsClient.SearchNewsAsync(digits, cancellationToken);

        return [.. articles.Select(a => new NewsItem
        {
            Title = a.Title,
            Link = a.Link,
            Source = a.Source,
            PublishTime = a.PublishTime,
            Summary = a.Content
        })];
    }

    /// <summary>
    /// 获取指定股票的聚合新闻上下文（Tool Function）
    /// </summary>
    [Description("获取指定股票的聚合新闻上下文，一次返回最近且相关的新闻要点。默认返回精简要点，可通过 response_format 控制详细程度。")]
    public Task<List<NewsItem>> GetNewsAsync(
        [Description("股票代码")] string assetSymbol,
        [Description("返回的新闻条数上限，默认 5，建议 1-10")] int count = 5,
        CancellationToken cancellationToken = default)
    {
        count = Math.Clamp(count, 1, 10);

        return ToolExecutor.ExecuteAsync(
            operationName: "获取聚合新闻",
            assetSymbol: assetSymbol,
            logger: _logger,
            action: async ct =>
            {
                var list = await GetNewsListAsync(assetSymbol, ct);
                return list.Take(count).ToList();
            },
            cancellationToken: cancellationToken);
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetNewsAsync);
    }
}