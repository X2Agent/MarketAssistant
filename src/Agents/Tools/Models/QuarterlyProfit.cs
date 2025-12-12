using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 季度利润数据实体类
/// </summary>
public class QuarterlyProfit
{
    /// <summary>
    /// 截止日期yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    /// <summary>
    /// 营业收入（万元）
    /// </summary>
    [JsonPropertyName("income")]
    public string Income { get; set; } = "";

    /// <summary>
    /// 营业支出（万元）
    /// </summary>
    [JsonPropertyName("expend")]
    public string Expend { get; set; } = "";

    /// <summary>
    /// 营业利润（万元）
    /// </summary>
    [JsonPropertyName("profit")]
    public string Profit { get; set; } = "";

    /// <summary>
    /// 利润总额（万元）
    /// </summary>
    [JsonPropertyName("totalp")]
    public string TotalProfit { get; set; } = "";

    /// <summary>
    /// 净利润（万元）
    /// </summary>
    [JsonPropertyName("reprofit")]
    public string NetProfit { get; set; } = "";

    /// <summary>
    /// 基本每股收益(元/股)
    /// </summary>
    [JsonPropertyName("basege")]
    public string BasicEarningsPerShare { get; set; } = "";

    /// <summary>
    /// 稀释每股收益(元/股)
    /// </summary>
    [JsonPropertyName("ettege")]
    public string DilutedEarningsPerShare { get; set; } = "";

    /// <summary>
    /// 其他综合收益（万元）
    /// </summary>
    [JsonPropertyName("otherp")]
    public string OtherComprehensiveIncome { get; set; } = "";

    /// <summary>
    /// 综合收益总额（万元）
    /// </summary>
    [JsonPropertyName("totalcp")]
    public string TotalComprehensiveIncome { get; set; } = "";
}