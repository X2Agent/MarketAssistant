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
public sealed class AShareNewsTools : INewsDataTools
{
    private readonly EastMoneyNewsClient _newsClient;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<AShareNewsTools> _logger;

    public AShareNewsTools(
        EastMoneyNewsClient newsClient,
        IChatClientFactory chatClientFactory,
        ILogger<AShareNewsTools> logger)
    {
        _newsClient = newsClient ?? throw new ArgumentNullException(nameof(newsClient));
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _logger = logger;
    }

    /// <summary>
    /// 根据新闻Url获取新闻详情
    /// </summary>
    private async Task<string> GetNewsContentAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var sr = new SmartReader.Reader(url);
            sr.Debug = false;

            var article = sr.GetArticle();
            if (article.IsReadable)
            {
                return article.TextContent;
            }

            // 使用 IChatClient 直接进行内容提取（Agent Framework 方式）
            return await ExtractArticleContentAsync(article.Content, cancellationToken);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "处理新闻内容失败: {Url}", url);
            throw new FriendlyException($"处理新闻内容时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 从HTML内容中提取文章正文（使用AI）
    /// </summary>
    private async Task<string> ExtractArticleContentAsync(string htmlContent, CancellationToken cancellationToken = default)
    {
        var chatClient = _chatClientFactory.CreateClient();

        var systemPrompt = @"你是一个专业的网页内容提取专家。请从HTML内容中提取出文章的主要内容。

要求：
1. 去除所有广告、导航栏、页脚等非文章内容
2. 保持文章的原始格式和段落结构
3. 仅返回正文文本内容，不需要其他信息

请直接返回提取的正文内容，不需要JSON格式或其他标记。";

        var userPrompt = $"HTML内容：\n{htmlContent}";

        var response = await chatClient.GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt)
            ],
            new ChatOptions
            {
                Temperature = 0.3f,
                MaxOutputTokens = 4096
            },
            cancellationToken);

        return response.Text ?? string.Empty;
    }

    /// <summary>
    /// 获取指定股票新闻列表（标题/来源/链接）。
    /// 数据源：东方财富搜索 API；HTTP 访问与 JSONP 解析由 <see cref="EastMoneyNewsClient"/> 负责。
    /// </summary>
    private async Task<List<NewsItem>> GetNewsListAsync(string assetSymbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // 东方财富搜索 API 使用纯数字代码（如 600519）
            var digits = new string(assetSymbol.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digits))
                return [];

            var articles = await _newsClient.SearchNewsAsync(digits, cancellationToken);

            return articles.Select(a => new NewsItem
            {
                Title = a.Title,
                Link = a.Link,
                Source = a.Source,
                PublishTime = a.PublishTime,
                Summary = a.Content
            }).ToList();
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取新闻列表失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取新闻列表时发生错误: {ex.Message}", ex);
        }
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