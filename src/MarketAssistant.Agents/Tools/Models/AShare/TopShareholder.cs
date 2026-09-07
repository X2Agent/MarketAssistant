using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

public class TopShareholder
{
    [JsonPropertyName("ggrq")]
    public string AnnouncementDate { get; set; } = "";

    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = "";

    [JsonPropertyName("gdmc")]
    public string ShareholderName { get; set; } = "";

    [JsonPropertyName("gdlx")]
    public string ShareholderType { get; set; } = "";

    [JsonPropertyName("cgsl")]
    public string SharesHeld { get; set; } = "";

    [JsonPropertyName("bdyy")]
    public string ChangeReason { get; set; } = "";

    [JsonPropertyName("cgbl")]
    public string ShareholdingRatio { get; set; } = "";

    [JsonPropertyName("gfxz")]
    public string ShareNature { get; set; } = "";

    [JsonPropertyName("cgpm")]
    public string Ranking { get; set; } = "";
}
