using MarketAssistant.Agents.Plugins.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Browser;
using Microsoft.Extensions.AI;
using Microsoft.Playwright;
using System.ComponentModel;

namespace MarketAssistant.Agents.Plugins;

/// <summary>
/// 股票新闻插件（Agent Framework 版本）
/// 提供股票新闻获取和内容提取功能
/// </summary>
public class StockNewsPlugin
{
    private readonly IServiceProvider _serviceProvider;
    private readonly PlaywrightService _playwrightService;
    private readonly IChatClientFactory _chatClientFactory;

    public StockNewsPlugin(
        IServiceProvider serviceProvider,
        PlaywrightService playwrightService,
        IChatClientFactory chatClientFactory)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _playwrightService = playwrightService ?? throw new ArgumentNullException(nameof(playwrightService));
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
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
        catch (Exception ex)
        {
            throw new Exception($"处理新闻内容时发生错误: {ex.Message}", ex);
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
    /// 获取指定股票新闻列表（标题/来源/链接）
    /// </summary>
    /// <param name="stockSymbol"></param>
    /// <returns></returns>
    /// <exception cref="Exception"></exception>
    private async Task<IEnumerable<NewsItem>> GetNewsListAsync(string stockSymbol)
    {
        try
        {
            stockSymbol = StockSymbolConverter.ToClsFormat(stockSymbol).ToLower();

            var url = $"https://www.cls.cn/stock?code={stockSymbol}";

            // 使用PlaywrightService获取Browser实例
            return await _playwrightService.ExecuteWithPageAsync(async page =>
            {
                await page.GotoAsync(url);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                var newsElements = await page.QuerySelectorAllAsync("div.p-t-20.p-b-20.b-b-w-1.b-b-s-s.b-c-e6e7ea");

                var newsList = new List<NewsItem>();

                foreach (var newsElement in newsElements)
                {
                    var titleElement = await newsElement.QuerySelectorAsync("a.c-222.line3");
                    if (titleElement != null)
                    {
                        var title = await titleElement.InnerTextAsync();
                        var link = await titleElement.GetAttributeAsync("href");
                        if (!string.IsNullOrEmpty(link) && !link.StartsWith("http"))
                        {
                            link = $"https://www.cls.cn{link}";
                        }

                        // 获取新闻来源
                        string source = "";
                        var sourceElement = await newsElement.QuerySelectorAsync("div.f-r");
                        if (sourceElement != null)
                        {
                            source = await sourceElement.InnerTextAsync();
                        }

                        newsList.Add(new NewsItem()
                        {
                            Title = title,
                            Url = link ?? "",
                            Source = source
                        });
                    }
                }

                return newsList;
            });
        }
        catch (Exception ex)
        {
            throw new Exception($"处理新闻列表时发生错误: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// 获取指定股票的聚合新闻上下文（Tool Function）
    /// </summary>
    [Description("获取指定股票的聚合新闻上下文，一次返回最近且相关的新闻要点。默认返回精简要点，可通过 response_format 控制详细程度。")]
    public async Task<IEnumerable<NewsItem>> GetStockNewsContextAsync(
        [Description("股票代码，支持含前缀或仅数字")] string stockSymbol,
        [Description("返回的新闻条数上限，默认 5，建议 1-10")] int topK = 5,
        [Description("响应格式：concise | detailed。默认 concise 更省 token")] string responseFormat = "concise")
    {
        try
        {
            topK = Math.Clamp(topK, 1, 10);

            var list = await GetNewsListAsync(stockSymbol);
            var results = list.Take(topK).ToList();

            bool isDetailed = string.Equals(responseFormat, "detailed", StringComparison.OrdinalIgnoreCase);

            if (isDetailed)
            {
                foreach (var item in results)
                {
                    try
                    {
                        var content = await GetNewsContentAsync(item.Url);

                        // 简单截断作为摘要片段，避免长文本占用上下文
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            var normalized = content.Trim();
                            item.Summary = normalized.Length <= 500 ? normalized : normalized.Substring(0, 500);
                        }
                    }
                    catch
                    {
                        // 单条失败不影响整体，保持 Summary 为空
                    }
                }
            }

            return results;
        }
        catch (Exception ex)
        {
            // 以可读错误提示帮助代理调整调用策略
            throw new Exception($"获取聚合新闻上下文失败: {ex.Message}. 可尝试：降低 topK、将 response_format 设为 'concise' 或缩小时间范围。", ex);
        }
    }
}
