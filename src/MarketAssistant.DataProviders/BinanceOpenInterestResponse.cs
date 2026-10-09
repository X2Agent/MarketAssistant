using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// 币安持仓量 API 响应模型。
/// 币安对 sumOpenInterest/sumOpenInterestValue 恒返回字符串，非空 decimal 依赖该契约；
/// <see cref="StringToDecimalConverter"/> 对 null 抛错，兼任上游 schema 违约哨兵。
/// </summary>
public class BinanceOpenInterestResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("sumOpenInterest")]
    public decimal SumOpenInterest { get; set; }

    [JsonPropertyName("sumOpenInterestValue")]
    public decimal SumOpenInterestValue { get; set; }

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
