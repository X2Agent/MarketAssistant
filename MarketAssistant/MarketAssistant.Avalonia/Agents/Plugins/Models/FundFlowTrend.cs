using System.Text.Json.Serialization;

namespace MarketAssistant.Plugins.Models;

/// <summary>
/// 资金流向趋势数据（最近10天）
/// </summary>
class FundFlowTrend
{
    // ================= 时间与基础指标 =================
    /// <summary>
    /// 日期 (格式 yyyy-MM-dd)
    /// </summary>
    [JsonPropertyName("t")]
    public string Date { get; set; } = "";

    /// <summary>
    /// 涨跌幅 (单位：%)
    /// </summary>
    [JsonPropertyName("zdf")]
    public decimal ChangePercent { get; set; }

    /// <summary>
    /// 换手率 (单位：%)
    /// </summary>
    [JsonPropertyName("hsl")]
    public decimal TurnoverRate { get; set; }

    // ================= 资金流量指标 =================
    /// <summary>
    /// 净流入金额 (单位：元)
    /// </summary>
    [JsonPropertyName("jlr")]
    public long NetInflow { get; set; }

    /// <summary>
    /// 净流入率 (单位：%)
    /// </summary>
    [JsonPropertyName("jlrl")]
    public decimal NetInflowRatio { get; set; }

    // ================= 主力资金 =================
    /// <summary>
    /// 主力净流入 (单位：元)
    /// </summary>
    [JsonPropertyName("zljlr")]
    public long MainForceInflow { get; set; }

    /// <summary>
    /// 主力净流入率 (单位：%)
    /// </summary>
    [JsonPropertyName("zljlrl")]
    public decimal MainForceInflowRatio { get; set; }

    // ================= 行业资金 =================
    /// <summary>
    /// 行业净流入 (单位：元)
    /// </summary>
    [JsonPropertyName("hyjlr")]
    public long IndustryInflow { get; set; }

    /// <summary>
    /// 行业净流入率 (单位：%)
    /// </summary>
    [JsonPropertyName("hyjlrl")]
    public decimal IndustryInflowRatio { get; set; }

    // ================= 辅助方法 =================
    /// <summary>
    /// 净流入金额转换为万元
    /// </summary>
    public decimal NetInflowToTenThousand() => NetInflow / 10000m;
}
