using System.Text.Json;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 东方财富搜索新闻条目（<c>search-api-web.eastmoney.com</c>，result.cmsArticleWebOld[]）。
/// </summary>
public sealed class EastMoneyNewsArticle
{
    /// <summary>新闻标题。</summary>
    public string Title { get; init; } = "";
    /// <summary>媒体来源。</summary>
    public string Source { get; init; } = "";
    /// <summary>原文链接。</summary>
    public string Link { get; init; } = "";
    /// <summary>内容摘要。</summary>
    public string Content { get; init; } = "";
    /// <summary>发布时间文本。</summary>
    public string PublishTime { get; init; } = "";
}

/// <summary>
/// 东方财富搜索新闻客户端。公开免费、无需签名，返回 JSONP 需剥离外层包装。
/// </summary>
public sealed class EastMoneyNewsClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public EastMoneyNewsClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// 按纯数字股票代码搜索相关新闻。
    /// </summary>
    /// <param name="digitsCode">纯数字代码（如 600519）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新闻列表；无结果时返回空列表。</returns>
    public async Task<List<EastMoneyNewsArticle>> SearchNewsAsync(
        string digitsCode, CancellationToken cancellationToken = default)
    {
        // 构造 JSONP 请求参数（与 eastmoney.com 前端一致，等价 JSON 载荷）
        var payloadJson = JsonSerializer.Serialize(new
        {
            uid = "",
            keyword = digitsCode,
            type = new[] { "cmsArticleWebOld" },
            client = "web",
            clientType = "web",
            clientVersion = "curr",
            param = new
            {
                cmsArticleWebOld = new
                {
                    searchScope = "default",
                    sort = "default",
                    pageIndex = 1,
                    pageSize = 20,
                    preTag = "",
                    postTag = ""
                }
            }
        });
        var param = Uri.EscapeDataString(payloadJson);
        var url = $"search/jsonp?cb=jQuery&param={param}";
        using var httpClient = _httpClientFactory.CreateClient("EastMoneySearch");
        // 超时由统一命名客户端配置 / resilience 管线管理，不在调用点单独设置

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Referer", "https://so.eastmoney.com/");
        request.Headers.TryAddWithoutValidation("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var jsonp = await response.Content.ReadAsStringAsync(cancellationToken);
        var json = UnwrapJsonp(jsonp);
        if (string.IsNullOrEmpty(json))
            return [];

        using var doc = JsonDocument.Parse(json);

        var newsList = new List<EastMoneyNewsArticle>();

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

            newsList.Add(new EastMoneyNewsArticle
            {
                Title = title,
                Source = item.TryGetProperty("mediaName", out var srcEl) ? srcEl.GetString()?.Trim() ?? "" : "",
                Link = item.TryGetProperty("url", out var urlEl) ? urlEl.GetString()?.Trim() ?? "" : "",
                Content = item.TryGetProperty("content", out var cntEl) ? cntEl.GetString()?.Trim() ?? "" : "",
                PublishTime = item.TryGetProperty("date", out var dateEl) ? dateEl.GetString()?.Trim() ?? "" : ""
            });
        }

        return newsList;
    }

    /// <summary>
    /// 剥离 JSONP 包装（jQuery({...}) → {...}）。
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
}