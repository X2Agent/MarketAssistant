using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

[Description("公司股本结构")]
public class CapitalStructure
{
    [Description("变动日期")]
    [JsonPropertyName("bdrq")]
    public string ChangeDate { get; set; } = string.Empty;

    [Description("公告日")]
    [JsonPropertyName("ggr")]
    public string AnnouncementDate { get; set; } = string.Empty;

    [Description("总股本")]
    [JsonPropertyName("zgb")]
    public decimal? TotalShares { get; set; }

    [Description("已上市流通A股")]
    [JsonPropertyName("ysltag")]
    public decimal? CirculatingAShares { get; set; }

    [Description("限售流通股份")]
    [JsonPropertyName("xsltgf")]
    public decimal? RestrictedShares { get; set; }
}
