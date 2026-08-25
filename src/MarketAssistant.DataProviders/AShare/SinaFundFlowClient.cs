using System.Text.Json;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 新浪财经个股资金流排行客户端（<c>MoneyFlow.ssl_bkzj_ssggzj</c>）。
/// 仅负责 HTTP 访问与响应解析（GBK 编码流式解码），不承载业务映射逻辑。
/// </summary>
public sealed class SinaFundFlowClient
{
    private const string TopNetInflowPath =
        "/quotes_service/api/json_v2.php/MoneyFlow.ssl_bkzj_ssggzj?page=1&num={0}&sort=netamount&asc=0";

    private readonly IHttpClientFactory _httpClientFactory;

    static SinaFundFlowClient()
    {
        // 新浪接口返回 Content-Type: application/json; charset=gbk，
        // .NET 默认不支持 GBK 编码，需注册 CodePagesEncodingProvider（幂等）。
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
    }

    public SinaFundFlowClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// 获取个股资金流排行（按净流入降序）。
    /// </summary>
    /// <param name="count">返回条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>资金流条目列表；无数据时返回空列表。</returns>
    public async Task<List<SinaFundFlowItem>> GetTopNetInflowAsync(
        int count, CancellationToken cancellationToken = default)
    {
        var url = string.Format(TopNetInflowPath, count);

        using var httpClient = _httpClientFactory.CreateClient("SinaFinance");
        using var stream = await httpClient.GetStreamAsync(url, cancellationToken);
        using var reader = new StreamReader(stream, System.Text.Encoding.GetEncoding("gbk"));
        var json = await reader.ReadToEndAsync(cancellationToken);
        using var jsonDocument = JsonDocument.Parse(json);

        var items = new List<SinaFundFlowItem>();
        if (jsonDocument.RootElement.ValueKind != JsonValueKind.Array)
            return items;

        foreach (var element in jsonDocument.RootElement.EnumerateArray())
        {
            var symbol = ParseString(element, "symbol");
            if (symbol.Length < 2)
                continue;

            items.Add(new SinaFundFlowItem(
                Symbol: symbol,
                Name: ParseString(element, "name"),
                Price: ParseString(element, "trade"),
                ChangeRatio: ParseDouble(element, "changeratio"),
                NetAmount: ParseDouble(element, "netamount")));
        }

        return items;
    }

    /// <summary>
    /// 从 JSON 元素安全解析字符串值。新浪接口在股票停牌/退市时，
    /// 部分字段会返回 false 而非字符串，直接 GetString() 会抛异常。
    /// </summary>
    private static string ParseString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return "";

        return property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    }

    /// <summary>
    /// 从 JSON 元素解析 double 值，兼容字符串和数字两种类型。
    /// 新浪接口返回的数值字段多为字符串类型。
    /// </summary>
    private static double ParseDouble(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
            return 0;

        return property.ValueKind switch
        {
            JsonValueKind.String => double.TryParse(property.GetString(), out var value) ? value : 0,
            JsonValueKind.Number => property.GetDouble(),
            _ => 0
        };
    }
}
