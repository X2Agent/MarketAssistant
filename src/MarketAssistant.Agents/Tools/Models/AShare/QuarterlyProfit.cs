using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

public class QuarterlyProfit
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("income")]
    public string Income { get; set; } = "";

    [JsonPropertyName("expend")]
    public string Expend { get; set; } = "";

    [JsonPropertyName("profit")]
    public string Profit { get; set; } = "";

    [JsonPropertyName("totalp")]
    public string TotalProfit { get; set; } = "";

    [JsonPropertyName("reprofit")]
    public string NetProfit { get; set; } = "";

    [JsonPropertyName("basege")]
    public string BasicEarningsPerShare { get; set; } = "";

    [JsonPropertyName("ettege")]
    public string DilutedEarningsPerShare { get; set; } = "";

    [JsonPropertyName("otherp")]
    public string OtherComprehensiveIncome { get; set; } = "";

    [JsonPropertyName("totalcp")]
    public string TotalComprehensiveIncome { get; set; } = "";
}
