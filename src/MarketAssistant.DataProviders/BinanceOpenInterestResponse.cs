using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// 币安持仓量 API 响应模型
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
