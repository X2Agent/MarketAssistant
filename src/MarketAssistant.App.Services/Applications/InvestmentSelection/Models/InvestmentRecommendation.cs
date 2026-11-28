using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.InvestmentSelection.Models;

/// <summary>
/// 投资标的推荐结果
/// </summary>
[Description("单个投资标的的推荐详情")]
public class InvestmentRecommendation
{
    /// <summary>
    /// 资产代码，原样使用候选数据中的代码（股票如 SH600000，虚拟币如 BTC）
    /// </summary>
    [Description("资产代码，必须原样使用候选数据中的代码字段，不得改写或追加后缀：股票如 SH600000、SZ000001，虚拟币如 BTC、ETH")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 资产名称
    /// </summary>
    [Description("资产名称")]
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
    [Range(0.00000001, 100000000)]
    [Description("目标价格，单位：元或USDT，范围0.00000001-100000000，低价币需保留足够小数位，无法预测时设为null")]
    public decimal? TargetPrice { get; set; }

    /// <summary>
    /// 止损价格
    /// </summary>
    [Range(0.00000001, 100000000)]
    [Description("止损价格，单位：元或USDT，范围0.00000001-100000000，低价币需保留足够小数位，无法预测时设为null")]
    public decimal? StopLoss { get; set; }

    /// <summary>
    /// 筛选快照中的现价，仅供界面与目标价/止损价对照；由 Executor 回填，不属于 LLM 输出契约
    /// </summary>
    [JsonIgnore]
    public decimal? CurrentPrice { get; set; }

    /// <summary>
    /// 筛选快照中的涨跌幅(%)，仅供界面展示；由 Executor 回填，不属于 LLM 输出契约
    /// </summary>
    [JsonIgnore]
    public decimal? ChangePercent { get; set; }
}

