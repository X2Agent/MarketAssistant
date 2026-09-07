using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

public class ShareholderCount
{
    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = "";

    [JsonPropertyName("gdzs")]
    public string TotalShareholders { get; set; } = "";

    [JsonPropertyName("agdhs")]
    public string AShareholderCount { get; set; } = "";

    [JsonPropertyName("bgdhs")]
    public string BShareholderCount { get; set; } = "";

    [JsonPropertyName("hgdhs")]
    public string HShareholderCount { get; set; } = "";

    [JsonPropertyName("yltgdhs")]
    public string CirculatingShareholderCount { get; set; } = "";

    [JsonPropertyName("wltgdhs")]
    public string NonCirculatingShareholderCount { get; set; } = "";
}
