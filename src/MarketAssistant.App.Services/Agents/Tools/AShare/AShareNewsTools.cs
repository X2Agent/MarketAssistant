using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<AShareNewsTools> _logger;

    public AShareNewsTools(
        IHttpClientFactory httpClientFactory,
        IChatClientFactory chatClientFactory,
        ILogger<AShareNewsTools> logger)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
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
    /// 获取指定股票新闻列表（标题/来源/链接）
    /// </summary>
    private async Task<IEnumerable<NewsItem>> GetNewsListAsync(string assetSymbol)
    {
        try
        {
            assetSymbol = StockSymbolConverter.ToClsFormat(assetSymbol).ToLower();

            var url = $"https://www.cls.cn/es/quotes/articles?app=CailianpressWeb&keyword={assetSymbol}&lastTime={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}&os=web&rn=10&sv=8.7.9&sign=fbb8361109191781631a4dd08d934207";

            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Referer", "https://www.cls.cn/");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            var response = await httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            var newsList = new List<NewsItem>();

            if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                return newsList;

            foreach (var item in data.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString()?.Trim() ?? "" : "";
                if (string.IsNullOrEmpty(title))
                    continue;

                var source = item.TryGetProperty("source", out var sourceEl) ? sourceEl.GetString()?.Trim() ?? "" : "";

                // 构建链接：外部文章用 external_link，站内文章拼接
                var link = "";
                var isExternal = item.TryGetProperty("is_external", out var extEl) && extEl.GetInt32() == 1;
                if (isExternal && item.TryGetProperty("external_link", out var linkEl))
                {
                    link = linkEl.GetString() ?? "";
                }
                else if (item.TryGetProperty("id", out var idEl))
                {
                    var id = idEl.GetString() ?? "";
                    if (!string.IsNullOrEmpty(id))
                        link = $"https://www.cls.cn/detail/xk/{id}";
                }

                // 发布时间
                var publishTime = "";
                if (item.TryGetProperty("ctime", out var ctimeEl) && ctimeEl.ValueKind == JsonValueKind.Number)
                {
                    var timestamp = ctimeEl.GetInt64();
                    publishTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).LocalDateTime.ToString("yyyy-MM-dd HH:mm");
                }

                newsList.Add(new NewsItem
                {
                    Title = title,
                    Link = link,
                    Source = source,
                    PublishTime = publishTime
                });
            }

            return newsList;
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
    public async Task<List<NewsItem>> GetNewsAsync(
        [Description("股票代码")] string assetSymbol,
        [Description("返回的新闻条数上限，默认 5，建议 1-10")] int count = 5)
    {
        try
        {
            count = Math.Clamp(count, 1, 10);

            var list = await GetNewsListAsync(assetSymbol);
            var results = list.Take(count).ToList();

            return results;
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取聚合新闻失败: {Symbol}", assetSymbol);
            throw new FriendlyException($"获取聚合新闻上下文失败: {ex.Message}", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetNewsAsync);
    }
}
