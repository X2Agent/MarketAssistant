using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

public class QuarterlyCashFlow
{
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    [JsonPropertyName("jyin")]
    public string OperatingCashflowIn { get; set; } = "";

    [JsonPropertyName("jyout")]
    public string OperatingCashflowOut { get; set; } = "";

    [JsonPropertyName("jyfinal")]
    public string OperatingCashflowNet { get; set; } = "";

    [JsonPropertyName("tzin")]
    public string InvestingCashflowIn { get; set; } = "";

    [JsonPropertyName("tzout")]
    public string InvestingCashflowOut { get; set; } = "";

    [JsonPropertyName("tzfinal")]
    public string InvestingCashflowNet { get; set; } = "";

    [JsonPropertyName("czin")]
    public string FinancingCashflowIn { get; set; } = "";

    [JsonPropertyName("czout")]
    public string FinancingCashflowOut { get; set; } = "";

    [JsonPropertyName("czfinal")]
    public string FinancingCashflowNet { get; set; } = "";

    [JsonPropertyName("hl")]
    public string ExchangeRateEffect { get; set; } = "";

    [JsonPropertyName("cashinc")]
    public string CashNetIncrease { get; set; } = "";

    [JsonPropertyName("cashs")]
    public string CashBeginning { get; set; } = "";

    [JsonPropertyName("cashe")]
    public string CashEnding { get; set; } = "";
}
