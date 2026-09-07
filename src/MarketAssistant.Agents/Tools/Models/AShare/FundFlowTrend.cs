using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

class FundFlowTrend
{
    [JsonPropertyName("t")]
    public string Date { get; set; } = "";

    [JsonPropertyName("zdf")]
    public decimal ChangePercent { get; set; }

    [JsonPropertyName("hsl")]
    public decimal TurnoverRate { get; set; }

    [JsonPropertyName("jlr")]
    public long NetInflow { get; set; }

    [JsonPropertyName("jlrl")]
    public decimal NetInflowRatio { get; set; }

    [JsonPropertyName("zljlr")]
    public long MainForceInflow { get; set; }

    [JsonPropertyName("zljlrl")]
    public decimal MainForceInflowRatio { get; set; }

    [JsonPropertyName("hyjlr")]
    public long IndustryInflow { get; set; }

    [JsonPropertyName("hyjlrl")]
    public decimal IndustryInflowRatio { get; set; }

    public decimal NetInflowToTenThousand() => NetInflow / 10000m;
}
