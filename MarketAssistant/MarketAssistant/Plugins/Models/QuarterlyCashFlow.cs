using System.Text.Json.Serialization;

namespace MarketAssistant.Plugins.Models;

/// <summary>
/// 季度现金流数据实体类
/// </summary>
public class QuarterlyCashFlow
{
    /// <summary>
    /// 截止日期yyyy-MM-dd
    /// </summary>
    [JsonPropertyName("date")]
    public string Date { get; set; } = "";

    /// <summary>
    /// 经营活动现金流入小计（万元）
    /// </summary>
    [JsonPropertyName("jyin")]
    public string OperatingCashflowIn { get; set; } = "";

    /// <summary>
    /// 经营活动现金流出小计（万元）
    /// </summary>
    [JsonPropertyName("jyout")]
    public string OperatingCashflowOut { get; set; } = "";

    /// <summary>
    /// 经营活动产生的现金流量净额（万元）
    /// </summary>
    [JsonPropertyName("jyfinal")]
    public string OperatingCashflowNet { get; set; } = "";

    /// <summary>
    /// 投资活动现金流入小计（万元）
    /// </summary>
    [JsonPropertyName("tzin")]
    public string InvestingCashflowIn { get; set; } = "";

    /// <summary>
    /// 投资活动现金流出小计（万元）
    /// </summary>
    [JsonPropertyName("tzout")]
    public string InvestingCashflowOut { get; set; } = "";

    /// <summary>
    /// 投资活动产生的现金流量净额（万元）
    /// </summary>
    [JsonPropertyName("tzfinal")]
    public string InvestingCashflowNet { get; set; } = "";

    /// <summary>
    /// 筹资活动现金流入小计（万元）
    /// </summary>
    [JsonPropertyName("czin")]
    public string FinancingCashflowIn { get; set; } = "";

    /// <summary>
    /// 筹资活动现金流出小计（万元）
    /// </summary>
    [JsonPropertyName("czout")]
    public string FinancingCashflowOut { get; set; } = "";

    /// <summary>
    /// 筹资活动产生的现金流量净额（万元）
    /// </summary>
    [JsonPropertyName("czfinal")]
    public string FinancingCashflowNet { get; set; } = "";

    /// <summary>
    /// 汇率变动对现金及现金等价物的影响（万元）
    /// </summary>
    [JsonPropertyName("hl")]
    public string ExchangeRateEffect { get; set; } = "";

    /// <summary>
    /// 现金及现金等价物净增加额（万元）
    /// </summary>
    [JsonPropertyName("cashinc")]
    public string CashNetIncrease { get; set; } = "";

    /// <summary>
    /// 期初现金及现金等价物余额（万元）
    /// </summary>
    [JsonPropertyName("cashs")]
    public string CashBeginning { get; set; } = "";

    /// <summary>
    /// 期末现金及现金等价物余额（万元）
    /// </summary>
    [JsonPropertyName("cashe")]
    public string CashEnding { get; set; } = "";
}