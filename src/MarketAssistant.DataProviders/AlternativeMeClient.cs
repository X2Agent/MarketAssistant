using System.Globalization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// alternative.me 恐惧贪婪指数客户端。仅负责 HTTP 访问与解析，故障由上层按源隔离。
/// </summary>
public sealed class AlternativeMeClient(IHttpClientFactory httpClientFactory)
{
    private const string LatestPath = "/fng/";

    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;

    /// <summary>获取最新恐惧贪婪指数；正常空响应返回 null，请求或解析故障向外抛出。</summary>
    public async Task<(int Value, string Classification)?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var httpClient = _httpClientFactory.CreateClient("AlternativeMe");
        var json = await httpClient.GetStringAsync(LatestPath, cancellationToken);
        using var doc = JsonDocument.Parse(json);

        if (!doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
            || data.GetArrayLength() == 0)
        {
            return null;
        }

        var first = data[0];
        var rawValue = first.TryGetProperty("value", out var v) ? v.GetString() : null;
        var classification = first.TryGetProperty("value_classification", out var c) ? c.GetString() : null;

        if (!int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return null;

        return (value, classification ?? string.Empty);
    }
}
