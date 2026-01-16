using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Crypto.Binance;

/// <summary>
/// 币安持仓�?API 响应模型
/// </summary>
public class BinanceOpenInterestResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("sumOpenInterest")]
    public string SumOpenInterest { get; set; } = string.Empty;

    [JsonPropertyName("sumOpenInterestValue")]
    public string SumOpenInterestValue { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
}
