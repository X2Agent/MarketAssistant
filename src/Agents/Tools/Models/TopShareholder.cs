using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 十大股东信息
/// </summary>
public class TopShareholder
{
    /// <summary>
    /// 公告日期
    /// </summary>
    [JsonPropertyName("ggrq")]
    public string AnnouncementDate { get; set; } = "";

    /// <summary>
    /// 截止日期
    /// </summary>
    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = "";

    /// <summary>
    /// 股东名称
    /// </summary>
    [JsonPropertyName("gdmc")]
    public string ShareholderName { get; set; } = "";

    /// <summary>
    /// 股东类型
    /// </summary>
    [JsonPropertyName("gdlx")]
    public string ShareholderType { get; set; } = "";

    /// <summary>
    /// 持股数量
    /// </summary>
    [JsonPropertyName("cgsl")]
    public string SharesHeld { get; set; } = "";

    /// <summary>
    /// 变动原因
    /// </summary>
    [JsonPropertyName("bdyy")]
    public string ChangeReason { get; set; } = "";

    /// <summary>
    /// 持股比例
    /// </summary>
    [JsonPropertyName("cgbl")]
    public string ShareholdingRatio { get; set; } = "";

    /// <summary>
    /// 股份性质
    /// </summary>
    [JsonPropertyName("gfxz")]
    public string ShareNature { get; set; } = "";

    /// <summary>
    /// 持股排名
    /// </summary>
    [JsonPropertyName("cgpm")]
    public string Ranking { get; set; } = "";
}

