using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.Crypto.Binance;

/// <summary>
/// 币安资金费率 API 响应模型
/// </summary>
public class BinanceFundingRateResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("fundingRate")]
    public string FundingRate { get; set; } = string.Empty;

    [JsonPropertyName("fundingTime")]
    public long FundingTime { get; set; }
}
