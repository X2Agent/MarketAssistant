using System.ComponentModel;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 单位分析师的结构化信号快照（回测与分析师绩效追踪的数据基础）。
/// 由 <see cref="AnalystSignalParser"/> 从各分析师的结构化 JSON 输出提取；
/// 未提供的维度保持 null，不做推断填充；解析失败的分析师不出现在列表中。
/// </summary>
[Description("单位分析师的结构化信号快照，用于回测与绩效统计")]
public sealed class AnalystSignalSnapshot
{
    /// <summary>分析师 ASCII 名称（Agent Name），如 TechnicalAnalyst。</summary>
    [Description("分析师 ASCII 名称（Agent Name）")]
    public string AnalystName { get; set; } = string.Empty;

    /// <summary>投资评级；分析师未给出明确评级时为 null。</summary>
    [Description("投资评级；未给出明确评级时为 null")]
    public InvestmentRating? Rating { get; set; }

    /// <summary>操作方向建议（买入/观望/卖出）；未给出时为 null。</summary>
    [Description("操作方向建议（买入/观望/卖出）；未给出时为 null")]
    public OperationRecommendation? Operation { get; set; }

    /// <summary>目标价区间下沿；未给出时为 null。</summary>
    [Description("目标价区间下沿；未给出时为 null")]
    public decimal? TargetPriceLow { get; set; }

    /// <summary>目标价区间上沿；未给出时为 null。</summary>
    [Description("目标价区间上沿；未给出时为 null")]
    public decimal? TargetPriceHigh { get; set; }

    /// <summary>建议止损价；未给出时为 null。</summary>
    [Description("建议止损价；未给出时为 null")]
    public decimal? StopLossPrice { get; set; }
}
