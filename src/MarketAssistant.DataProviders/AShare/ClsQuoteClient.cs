using System.Text.Json;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 财联社（cls.cn）行情与搜索客户端。
/// 仅负责 HTTP 访问与响应解析，不承载业务映射逻辑。
/// </summary>
public sealed class ClsQuoteClient
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ClsQuoteClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// 获取股票基础行情（<c>/quote/stock/basic</c>）。
    /// </summary>
    /// <param name="secuCode">CLS 格式股票代码（如 sh600519）。</param>
    /// <param name="fields">请求的字段列表（逗号分隔）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>行情数据；data 为空时返回 null。</returns>
    public async Task<ClsStockQuoteData?> GetStockQuoteAsync(
        string secuCode, string fields, CancellationToken cancellationToken = default)
    {
        var url = $"/quote/stock/basic?secu_code={secuCode}&fields={fields}&app=CailianpressWeb&os=web&sv=8.4.6";

        using var httpClient = _httpClientFactory.CreateClient("Cls");
        var response = await httpClient.GetStringAsync(url, cancellationToken);
        using var jsonDocument = JsonDocument.Parse(response);

        if (!jsonDocument.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind == JsonValueKind.Null)
            return null;

        return JsonSerializer.Deserialize<ClsStockQuoteData>(data.GetRawText(), AShareJsonOptions.Instance);
    }

    /// <summary>
    /// 按关键词搜索股票（<c>https://www.cls.cn/api/sw</c>，POST JSON）。
    /// </summary>
    /// <param name="keyword">搜索关键词（名称或代码）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>搜索结果列表；失败或无结果时返回空列表。</returns>
    public async Task<List<ClsStockSearchItem>> SearchStocksAsync(
        string keyword, CancellationToken cancellationToken = default)
    {
        // 第三方接口签名（sign/sv 为抓包固定值），服务端更新后需手动同步；
        // 若请求返回 401/403，应提示签名可能已过期。
        const string apiUrl = "https://www.cls.cn/api/sw?app=CailianpressWeb&os=web&sv=8.7.9&sign=b02d8f7bc4c45eeb3e86904203597da2";

        var body = new
        {
            type = "stock",
            keyword = keyword.Trim(),
            rn = 20,
            page = 0,
            os = "web",
            sv = "8.7.9",
            app = "CailianpressWeb"
        };

        using var httpClient = _httpClientFactory.CreateClient("Cls");
        // 超时由统一命名客户端配置 / resilience 管线管理，不在调用点单独设置

        using var content = new StringContent(
            JsonSerializer.Serialize(body),
            Encoding.UTF8,
            "application/json");

        var response = await httpClient.PostAsync(apiUrl, content, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(json);

        var stockList = new List<ClsStockSearchItem>();
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("stock", out var stock) &&
            stock.TryGetProperty("data", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var title = item.GetProperty("title").GetString() ?? "";
                var stockId = item.GetProperty("stock_id").GetString() ?? "";

                // 去除 <em> 高亮标签
                title = title.Replace("<em>", "").Replace("</em>", "").Trim();

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(stockId))
                    stockList.Add(new ClsStockSearchItem(title, stockId));
            }
        }

        return stockList;
    }
}