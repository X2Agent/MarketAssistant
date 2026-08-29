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
    public decimal MarkPrice { get; set; }

    [JsonPropertyName("lastFundingRate")]
    public decimal LastFundingRate { get; set; }

    [JsonPropertyName("nextFundingTime")]
    public long NextFundingTime { get; set; }
}
