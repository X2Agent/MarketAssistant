using System.Text.Json;
using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders.AShare;

/// <summary>
/// 新浪 K 线历史数据条（<c>quotes.sina.cn/cn/api/json_v2.php/CN_MarketDataService.getKLineData</c>）。
/// </summary>
/// <param name="Day">K 线时间（日线为 <c>yyyy-MM-dd</c>，分钟线为 <c>yyyy-MM-dd HH:mm:ss</c>）。</param>
/// <remarks>
/// 数值字段为字符串，由 <see cref="AShareJsonOptions"/> 容错转换。
/// 实测：分钟线响应含 <paramref name="Amount"/>，日/周/月响应不含（为 null）。
/// </remarks>
public sealed record SinaKLineBar(
    [property: JsonPropertyName("day")] string Day,
    [property: JsonPropertyName("open")] decimal Open,
    [property: JsonPropertyName("high")] decimal High,
    [property: JsonPropertyName("low")] decimal Low,
    [property: JsonPropertyName("close")] decimal Close,
    [property: JsonPropertyName("volume")] decimal Volume,
    [property: JsonPropertyName("amount")] decimal? Amount);

/// <summary>
/// 新浪 K 线客户端：拉取单只 A 股（含基金/ETF）的日/周/月/分钟 K 线历史。
/// </summary>
/// <remarks>
/// <para>
/// 作为智兔 K 线的兜底数据源：智兔 <c>/hs/history</c> 不覆盖基金/ETF（实测返回
/// 404 <c>{"error":"not found"}</c>），而本接口对股票、ETF、LOF、货币基金均返回数据。
/// </para>
/// <para>
/// <b>实测边界（2026-10 本机验证，50 连续 + 30 并发 0 失败，无限流、无需 Referer）</b>：
/// </para>
/// <list type="bullet">
/// <item><paramref name="MaxBarCount"/>：datalen 1~1900 正常，≥2000 返回空（count=0）；</item>
/// <item>datalen 传 0 会返回<b>全历史</b>，因此调用方必须传正整数，本客户端钳制到 [1, 1900]；</item>
/// <item>无效代码或非法 scale 时响应为 JSON <c>null</c>（非数组），按「无数据」返回空列表；</item>
/// <item>scale 取值：240=日、1200=周、7200=月、5/15=分钟。</item>
/// </list>
/// </remarks>
public sealed class SinaKLineClient(IHttpClientFactory httpClientFactory)
{
    /// <summary>datalen 上限：超过该值接口返回空（实测 1900 正常、2000 为空）。</summary>
    public const int MaxBarCount = 1900;

    private readonly IHttpClientFactory _httpClientFactory =
        httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));

    /// <summary>
    /// 拉取 K 线历史（按时间升序由上游保证，调用方仍需自行排序防御）。
    /// </summary>
    /// <param name="symbol">新浪格式代码（如 <c>sh511010</c>、<c>sz300308</c>）。</param>
    /// <param name="scale">周期（分钟数）：240=日、1200=周、7200=月、5/15=分钟。</param>
    /// <param name="count">需要的 K 线根数，钳制到 [1, <see cref="MaxBarCount"/>]。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>K 线条目；响应为 <c>null</c>（无效代码/非法周期）或解析失败时返回空列表。</returns>
    public async Task<List<SinaKLineBar>> GetKLineBarsAsync(
        string symbol,
        int scale,
        int count,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        var dataLength = Math.Clamp(count, 1, MaxBarCount);
        var path = $"/cn/api/json_v2.php/CN_MarketDataService.getKLineData" +
                   $"?symbol={symbol}&scale={scale}&ma=no&datalen={dataLength}";

        using var httpClient = _httpClientFactory.CreateClient("SinaKLine");
        using var response = await httpClient.GetAsync(path, cancellationToken);
        response.EnsureSuccessStatusCode();

        // 该接口 Content-Type 谎报 charset=gbk（实测 body 为 UTF-8），
        // 直接 GetStringAsync 会按 gbk 解码失败；按字节读取后显式 UTF-8 解码。
        var responseBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var responseContent = System.Text.Encoding.UTF8.GetString(responseBytes);

        // 无效代码/非法 scale 时响应为 JSON null，反序列化得到 null 而非空列表
        return JsonSerializer.Deserialize<List<SinaKLineBar>>(responseContent, AShareJsonOptions.Instance) ?? [];
    }
}
