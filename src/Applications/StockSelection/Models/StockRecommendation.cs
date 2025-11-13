using System.ComponentModel;

namespace MarketAssistant.Applications.StockSelection.Models;

/// <summary>
/// 股票推荐结果
/// </summary>
[Description("单只股票的推荐详情")]
public class StockRecommendation
{
    /// <summary>
    /// 股票代码
    /// </summary>
    [Description("股票代码，如 sh600000、sz000001")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    [Description("股票名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// AI推荐评分 (0-100)
    /// </summary>
    [Description("AI推荐评分，范围0-100，综合考虑财务、估值、市场表现等因素")]
    public float RecommendationScore { get; set; }

    /// <summary>
    /// 推荐理由（应包含具体的财务数据、估值指标、风险分析等）
    /// </summary>
    [Description("详细推荐理由，必须包含：1)具体财务数据(如ROE 18.5%、PE 25倍) 2)估值判断(低估/合理/偏高) 3)需求匹配说明或新闻关联度，建议100-200字")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 预期收益率 (%)
    /// </summary>
    [Description("预期收益率（百分比），可为正数或负数，可选字段")]
    public float? ExpectedReturn { get; set; }

    /// <summary>
    /// 风险等级（低风险、中风险、高风险）
    /// </summary>
    [Description("风险等级，必须是以下之一：低风险、中风险、高风险")]
    public string RiskLevel { get; set; } = string.Empty;

    /// <summary>
    /// 建议持有期限（天）
    /// </summary>
    [Description("建议持有天数，如短期30天、中期90天、长期180天，可选字段")]
    public int? RecommendedHoldingPeriod { get; set; }

    /// <summary>
    /// 建议仓位比例 (%)，可选字段
    /// </summary>
    [Description("建议仓位比例（百分比），范围0-100，可选字段")]
    public float? RecommendedPosition { get; set; }

    /// <summary>
    /// 目标价格，可选字段
    /// </summary>
    [Description("目标价格（元），可选字段，无法预测时设为null")]
    public decimal? TargetPrice { get; set; }

    /// <summary>
    /// 止损价格，可选字段
    /// </summary>
    [Description("止损价格（元），可选字段，无法预测时设为null")]
    public decimal? StopLoss { get; set; }
}

