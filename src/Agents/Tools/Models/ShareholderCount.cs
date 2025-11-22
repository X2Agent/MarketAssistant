using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 股东数信息
/// </summary>
public class ShareholderCount
{
    /// <summary>
    /// 截止日期
    /// </summary>
    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = "";

    /// <summary>
    /// 股东总数
    /// </summary>
    [JsonPropertyName("gdzs")]
    public string TotalShareholders { get; set; } = "";

    /// <summary>
    /// A股东户数
    /// </summary>
    [JsonPropertyName("agdhs")]
    public string AShareholderCount { get; set; } = "";

    /// <summary>
    /// B股东户数
    /// </summary>
    [JsonPropertyName("bgdhs")]
    public string BShareholderCount { get; set; } = "";

    /// <summary>
    /// H股东户数
    /// </summary>
    [JsonPropertyName("hgdhs")]
    public string HShareholderCount { get; set; } = "";

    /// <summary>
    /// 已流通股东户数
    /// </summary>
    [JsonPropertyName("yltgdhs")]
    public string CirculatingShareholderCount { get; set; } = "";

    /// <summary>
    /// 未流通股东户数
    /// </summary>
    [JsonPropertyName("wltgdhs")]
    public string NonCirculatingShareholderCount { get; set; } = "";
}

