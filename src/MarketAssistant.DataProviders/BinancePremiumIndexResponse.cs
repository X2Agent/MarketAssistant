using System.Text.Json.Serialization;

namespace MarketAssistant.DataProviders;

/// <summary>
/// 币安溢价指数 API 响应模型
/// </summary>
public class BinancePremiumIndexResponse
{
    [JsonPropertyName("symbol")]
    public string Symbol { get; set; } = string.Empty;

    [JsonPropertyName("markPrice")]
    public string MarkPrice { get; set; } = string.Empty;

    [JsonPropertyName("lastFundingRate")]
    public string LastFundingRate { get; set; } = string.Empty;

    [JsonPropertyName("nextFundingTime")]
    public long NextFundingTime { get; set; }
}
