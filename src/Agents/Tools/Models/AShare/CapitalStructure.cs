using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

/// <summary>
/// 公司股本结构
/// </summary>
public class CapitalStructure
{
    /// <summary>
    /// 变动日期
    /// </summary>
    [JsonPropertyName("bdrq")]
    public string ChangeDate { get; set; } = "";

    /// <summary>
    /// 公告�?
    /// </summary>
    [JsonPropertyName("ggr")]
    public string AnnouncementDate { get; set; } = "";

    /// <summary>
    /// 总股�?
    /// </summary>
    [JsonPropertyName("zgb")]
    public decimal? TotalShares { get; set; }

    /// <summary>
    /// 已上市流通A�?
    /// </summary>
    [JsonPropertyName("ysltag")]
    public decimal? CirculatingAShares { get; set; }

    /// <summary>
    /// 限售流通股�?
    /// </summary>
    [JsonPropertyName("xsltgf")]
    public decimal? RestrictedShares { get; set; }
}

