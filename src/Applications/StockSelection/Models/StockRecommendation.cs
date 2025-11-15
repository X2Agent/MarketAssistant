using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

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
    [RegularExpression(@"^(SH|SZ)\d{6}$", ErrorMessage = "股票代码格式错误，应为 SH000000 或 SZ000000")]
    [Description("股票代码，格式如 SH600000、SZ000001")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    [Description("股票名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// AI推荐评分 (0-100)
    /// </summary>
    [Range(0, 100)]
    [Description("AI推荐评分，范围0-100，不含引号，综合考虑财务、估值、市场表现等因素")]
    public float RecommendationScore { get; set; }

    /// <summary>
    /// 推荐理由（应包含具体的财务数据、估值指标、风险分析等）
    /// </summary>
    [MinLength(50)]
    [MaxLength(400)]
    [Description("详细推荐理由，必须包含：1)具体财务数据(如ROE 18.5%、PE 25倍) 2)估值判断(低估/合理/偏高) 3)需求匹配说明或新闻关联度。字数要求：100-200字")]
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 预期收益率 (%)
    /// </summary>
    [Range(-100, 1000)]
    [Description("预期收益率，单位：百分比，如15表示15%。可为正数或负数，无法预测时设为null")]
    public float? ExpectedReturn { get; set; }

    /// <summary>
    /// 风险等级
    /// </summary>
    [Description("风险等级：Low(低风险)、Medium(中风险)、High(高风险)")]
    public RiskLevel RiskLevel { get; set; }

    /// <summary>
    /// 建议持有期限（天）
    /// </summary>
    [Range(1, 3650)]
    [Description("建议持有天数，如短期30天、中期90天、长期180天，范围1-3650天，无法预测时设为null")]
    public int? RecommendedHoldingPeriod { get; set; }

    /// <summary>
    /// 建议仓位比例 (%)
    /// </summary>
    [Range(0, 100)]
    [Description("建议仓位比例，单位：百分比，如30表示30%，范围0-100，无法预测时设为null")]
    public float? RecommendedPosition { get; set; }

    /// <summary>
    /// 目标价格
    /// </summary>
    [Range(0.01, 100000)]
    [Description("目标价格，单位：元，范围0.01-100000，无法预测时设为null")]
    public decimal? TargetPrice { get; set; }

    /// <summary>
    /// 止损价格
    /// </summary>
    [Range(0.01, 100000)]
    [Description("止损价格，单位：元，范围0.01-100000，无法预测时设为null")]
    public decimal? StopLoss { get; set; }
}

