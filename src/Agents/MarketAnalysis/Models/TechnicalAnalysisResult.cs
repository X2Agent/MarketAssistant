using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 技术分析结果
/// </summary>
[Description("技术分析师的结构化分析结果，包含图表形态、关键价位、技术指标和交易策略")]
public sealed class TechnicalAnalysisResult
{
    /// <summary>
    /// 图表形态与趋势
    /// </summary>
    [Description("图表形态与趋势分析，包括当前趋势、关键形态和时间框架")]
    public ChartPatternTrend PatternTrend { get; set; } = new();

    /// <summary>
    /// 关键价位分析
    /// </summary>
    [Description("关键价位分析，包括当前价格、支撑位、阻力位和突破概率")]
    public KeyPriceLevels PriceLevels { get; set; } = new();

    /// <summary>
    /// 技术指标综合解读
    /// </summary>
    [Description("技术指标综合解读，包括趋势指标、动量指标、成交量和指标一致性")]
    public TechnicalIndicators Indicators { get; set; } = new();

    /// <summary>
    /// 交易策略建议
    /// </summary>
    [Description("交易策略建议，包括技术面评级、操作方向、目标价位、止损位置和持仓周期")]
    public TradingStrategy Strategy { get; set; } = new();
}

/// <summary>
/// 图表形态与趋势
/// </summary>
[Description("图表形态与趋势分析")]
public sealed class ChartPatternTrend
{
    /// <summary>
    /// 当前趋势
    /// </summary>
    [Description("当前趋势方向")]
    public TrendDirection CurrentTrend { get; set; }

    /// <summary>
    /// 趋势强度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("趋势强度评分，基于趋势持续性和角度。评分标准：" + ScoringStandards.TrendStrength)]
    public float TrendStrengthScore { get; set; }

    /// <summary>
    /// 关键形态描述
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("识别出最具影响力的1-2个图表形态，如头肩顶/底、三角形、旗形、双顶/底等")]
    public string KeyPatterns { get; set; } = string.Empty;

    /// <summary>
    /// 形态可靠性评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("形态可靠性评分，基于形态完整性和成交量配合度。评分标准：" + ScoringStandards.Reliability)]
    public float PatternReliabilityScore { get; set; }

    /// <summary>
    /// 时间框架
    /// </summary>
    [Description("主要分析的时间框架")]
    public TimeFrame TimeFrame { get; set; }

    /// <summary>
    /// 时间框架一致性评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("与更长时间框架一致性评分，评估不同时间周期信号的一致性程度。评分标准：" + ScoringStandards.Reliability)]
    public float TimeFrameConsistencyScore { get; set; }
}

/// <summary>
/// 关键价位分析
/// </summary>
[Description("关键价位分析")]
public sealed class KeyPriceLevels
{
    /// <summary>
    /// 当前价格（元）
    /// </summary>
    [Range(0.01, 100000)]
    [Description("当前价格，单位：元")]
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 核心支撑位列表
    /// </summary>
    [MinLength(1)]
    [MaxLength(2)]
    [Description("1-2个核心支撑价格点位，单位：元")]
    public List<decimal> SupportLevels { get; set; } = new();

    /// <summary>
    /// 支撑强度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("支撑强度评分，基于历史触及次数和成交密集度。评分标准：" + ScoringStandards.Strength)]
    public float SupportStrengthScore { get; set; }

    /// <summary>
    /// 核心阻力位列表
    /// </summary>
    [MinLength(1)]
    [MaxLength(2)]
    [Description("1-2个核心阻力价格点位，单位：元")]
    public List<decimal> ResistanceLevels { get; set; } = new();

    /// <summary>
    /// 阻力强度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("阻力强度评分，基于历史触及次数和成交密集度。评分标准：" + ScoringStandards.Strength)]
    public float ResistanceStrengthScore { get; set; }

    /// <summary>
    /// 突破方向
    /// </summary>
    [Description("突破方向预测")]
    public BreakoutDirection BreakoutDirection { get; set; }

    /// <summary>
    /// 突破概率评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("突破概率评分，综合技术指标和量能判断突破概率。评分标准：" + ScoringStandards.Reliability)]
    public float BreakoutProbabilityScore { get; set; }
}

/// <summary>
/// 技术指标综合解读
/// </summary>
[Description("技术指标综合解读")]
public sealed class TechnicalIndicators
{
    /// <summary>
    /// 趋势指标信号
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("主要趋势指标信号，如MA多头/空头排列，MACD金叉/死叉/背离等")]
    public string TrendIndicatorSignals { get; set; } = string.Empty;

    /// <summary>
    /// 趋势指标可靠性评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("趋势指标信号可靠性评分，基于多个趋势指标的一致性和历史准确率。评分标准：" + ScoringStandards.Reliability)]
    public float TrendIndicatorReliabilityScore { get; set; }

    /// <summary>
    /// 动量指标信号
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("主要动量指标信号，如RSI超买/超卖/背离，KDJ金叉/死叉等")]
    public string MomentumIndicatorSignals { get; set; } = string.Empty;

    /// <summary>
    /// 动量指标可靠性评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("动量指标信号可靠性评分，基于指标位置和背离情况。评分标准：" + ScoringStandards.Reliability)]
    public float MomentumIndicatorReliabilityScore { get; set; }

    /// <summary>
    /// 成交量状态
    /// </summary>
    [Description("成交量状态")]
    public VolumeStatus VolumeStatus { get; set; }

    /// <summary>
    /// 量价关系评估
    /// </summary>
    [Description("量价关系评估")]
    public PriceVolumeRelationship PriceVolumeRelationship { get; set; }

    /// <summary>
    /// 指标一致性
    /// </summary>
    [Description("指标一致性")]
    public Level IndicatorConsistency { get; set; }

    /// <summary>
    /// 指标协同说明
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("不同指标间的信号协同程度说明")]
    public string IndicatorSynergyDescription { get; set; } = string.Empty;
}

/// <summary>
/// 交易策略建议
/// </summary>
[Description("交易策略建议")]
public sealed class TradingStrategy
{
    /// <summary>
    /// 技术面评级
    /// </summary>
    [Description("技术面评级")]
    public InvestmentRating TechnicalRating { get; set; }

    /// <summary>
    /// 操作方向
    /// </summary>
    [Description("操作方向")]
    public OperationRecommendation OperationDirection { get; set; }

    /// <summary>
    /// 目标价位下限（元）
    /// </summary>
    [Range(0.01, 100000)]
    [Description("目标价位区间下限，单位：元，无法预测时设为null")]
    public decimal? TargetPriceLow { get; set; }

    /// <summary>
    /// 目标价位上限（元）
    /// </summary>
    [Range(0.01, 100000)]
    [Description("目标价位区间上限，单位：元，无法预测时设为null")]
    public decimal? TargetPriceHigh { get; set; }

    /// <summary>
    /// 止损位置（元）
    /// </summary>
    [Range(0.01, 100000)]
    [Description("止损价格点位，单位：元，无法预测时设为null")]
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// 持仓周期
    /// </summary>
    [Description("持仓周期")]
    public Duration HoldingPeriod { get; set; }

    /// <summary>
    /// 风险等级
    /// </summary>
    [Description("风险等级")]
    public Level RiskLevel { get; set; }
}
