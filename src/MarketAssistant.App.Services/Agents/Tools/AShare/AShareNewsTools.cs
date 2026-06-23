using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Models;
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
    ///
    /// 数据源：东方财富搜索 API（search-api-web.eastmoney.com）
    /// - 公开免费、无需签名/密钥
    /// - 返回 JSONP 格式（jQuery(...) 包装），需剥离外层
    /// - 替代原财联社 cls.cn 接口（sign 签名已失效）
    /// </summary>
    private async Task<List<NewsItem>> GetNewsListAsync(string assetSymbol, CancellationToken cancellationToken = default)
    {
        try
        {
            // 东方财富搜索 API 使用纯数字代码（如 600519）
            var digits = new string(assetSymbol.Where(char.IsDigit).ToArray());
            if (string.IsNullOrEmpty(digits))
                return new List<NewsItem>();

            // 构造 JSONP 请求参数（与 eastmoney.com 前端一致）
            var param = Uri.EscapeDataString(
                $"{{\"uid\":\"\",\"keyword\":\"{digits}\",\"type\":[\"cmsArticleWebOld\"],\"client\":\"web\",\"clientType\":\"web\",\"clientVersion\":\"curr\",\"param\":{{\"cmsArticleWebOld\":{{\"searchScope\":\"default\",\"sort\":\"default\",\"pageIndex\":1,\"pageSize\":20,\"preTag\":\"\",\"postTag\":\"\"}}}}}}");
            var url = $"search/jsonp?cb=jQuery&param={param}";

            using var httpClient = _httpClientFactory.CreateClient("EastMoneySearch");
            httpClient.Timeout = TimeSpan.FromSeconds(15);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Referer", "https://so.eastmoney.com/");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

            var response = await httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var jsonp = await response.Content.ReadAsStringAsync(cancellationToken);
            var json = UnwrapJsonp(jsonp);
            if (string.IsNullOrEmpty(json))
                return new List<NewsItem>();

            using var doc = JsonDocument.Parse(json);

            var newsList = new List<NewsItem>();

            // 数据路径：result.cmsArticleWebOld[]
            if (!doc.RootElement.TryGetProperty("result", out var result) ||
                result.ValueKind != JsonValueKind.Object ||
                !result.TryGetProperty("cmsArticleWebOld", out var articles) ||
                articles.ValueKind != JsonValueKind.Array)
                return newsList;

            foreach (var item in articles.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var titleEl) ? titleEl.GetString()?.Trim() ?? "" : "";
                if (string.IsNullOrEmpty(title))
                    continue;

                var source = item.TryGetProperty("mediaName", out var srcEl) ? srcEl.GetString()?.Trim() ?? "" : "";
                var link = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString()?.Trim() ?? "" : "";
                var content = item.TryGetProperty("content", out var cntEl) ? cntEl.GetString()?.Trim() ?? "" : "";
                var publishTime = item.TryGetProperty("date", out var dateEl) ? dateEl.GetString()?.Trim() ?? "" : "";

                newsList.Add(new NewsItem
                {
                    Title = title,
                    Link = link,
                    Source = source,
                    PublishTime = publishTime,
                    Summary = content
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
    /// 剥离 JSONP 包装（jQuery({...}) → {...}）
    /// </summary>
    public static string UnwrapJsonp(string jsonp)
    {
        if (string.IsNullOrEmpty(jsonp)) return string.Empty;

        var start = jsonp.IndexOf('(');
        var end = jsonp.LastIndexOf(')');
        if (start < 0 || end < 0 || end <= start)
            return jsonp;

        return jsonp.Substring(start + 1, end - start - 1);
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
