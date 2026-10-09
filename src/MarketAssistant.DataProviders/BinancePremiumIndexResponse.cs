using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// 币安溢价指数 API 响应模型。
/// 币安对 markPrice/lastFundingRate 恒返回字符串，非空 decimal 依赖该契约；
/// <see cref="StringToDecimalConverter"/> 对 null 抛错，兼任上游 schema 违约哨兵。
/// </summary>
public class BinancePremiumIndexResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("markPrice")]
    public decimal MarkPrice { get; set; }

    [JsonPropertyName("lastFundingRate")]
    public decimal LastFundingRate { get; set; }

    [JsonPropertyName("nextFundingTime")]
    public long NextFundingTime { get; set; }
}
