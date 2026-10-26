using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// 币安资金费率 API 响应模型。
/// 币安对 fundingRate 恒返回字符串，非空 decimal 依赖该契约；
/// <see cref="StringToDecimalConverter"/> 对 null 抛错，兼任上游 schema 违约哨兵。
/// </summary>
public class BinanceFundingRateResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("fundingRate")]
    public decimal FundingRate { get; set; }

    [JsonPropertyName("fundingTime")]
    public long FundingTime { get; set; }
}
