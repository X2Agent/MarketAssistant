using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 市场情绪分析结果
/// </summary>
[Description("市场情绪分析师的结构化分析结果，包含市场情绪、资金流向、投资者行为和短期策略")]
public sealed class MarketSentimentAnalysisResult
{
    /// <summary>
    /// 市场情绪评估
    /// </summary>
    [Description("市场情绪评估，包括主导情绪、恐慌与信心、整体氛围")]
    public MarketSentiment SentimentAssessment { get; set; } = new();

    /// <summary>
    /// 资金流向分析
    /// </summary>
    [Description("资金流向分析，包括主力资金、机构动向、北向资金和融资融券")]
    public CapitalFlow CapitalFlowAnalysis { get; set; } = new();

    /// <summary>
    /// 投资者行为分析
    /// </summary>
    [Description("投资者行为分析，包括行为偏差、散户特征、机构行为和风险偏好")]
    public InvestorBehavior BehaviorAnalysis { get; set; } = new();

    /// <summary>
    /// 短期市场洞察与策略
    /// </summary>
    [Description("短期市场洞察与策略，包括市场节奏、热点机会、操作建议和心理陷阱")]
    public ShortTermInsight ShortTermStrategy { get; set; } = new();
}

/// <summary>
/// 市场情绪评估
/// </summary>
[Description("市场情绪评估")]
public sealed class MarketSentiment
{
    /// <summary>
    /// 主导情绪
    /// </summary>
    [Description("主导情绪")]
    public DominantEmotion DominantEmotion { get; set; }

    /// <summary>
    /// 情绪强度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("情绪强度评分，基于情绪指标偏离程度。评分标准：" + ScoringStandards.EmotionIntensity)]
    public float EmotionIntensityScore { get; set; }

    /// <summary>
    /// VIX水平或情绪指数
    /// </summary>
    [Description("VIX水平或情绪指数的具体数值，无数据时可为空")]
    public string VIXLevel { get; set; } = string.Empty;

    /// <summary>
    /// 投资者信心水平
    /// </summary>
    [Description("投资者信心水平")]
    public Level InvestorConfidenceLevel { get; set; }

    /// <summary>
    /// 信心变化趋势
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("投资者信心变化趋势的简述")]
    public string ConfidenceTrendDescription { get; set; } = string.Empty;

    /// <summary>
    /// 整体氛围
    /// </summary>
    [Description("整体市场氛围")]
    public MarketAtmosphere OverallAtmosphere { get; set; }

    /// <summary>
    /// 氛围强度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("氛围强度评分，基于市场参与度和情绪蔓延程度。评分标准：" + ScoringStandards.EmotionIntensity)]
    public float AtmosphereIntensityScore { get; set; }
}

/// <summary>
/// 资金流向分析
/// </summary>
[Description("资金流向分析")]
public sealed class CapitalFlow
{
    /// <summary>
    /// 主力资金流向
    /// </summary>
    [Description("主力资金流向")]
    public CapitalFlowDirection MainCapitalFlow { get; set; }

    /// <summary>
    /// 主力资金金额（元）
    /// </summary>
    [Description("主力资金具体金额，单位：元，无数据时设为null")]
    public decimal? MainCapitalAmount { get; set; }

    /// <summary>
    /// 主力资金连续天数
    /// </summary>
    [Range(0, 1000)]
    [Description("主力资金流入或流出的连续天数，无数据时设为null")]
    public int? MainCapitalConsecutiveDays { get; set; }

    /// <summary>
    /// 机构动向
    /// </summary>
    [Description("机构动向")]
    public InstitutionTrend InstitutionTrend { get; set; }

    /// <summary>
    /// 机构持仓变化
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("机构持仓变化的简述")]
    public string InstitutionPositionChange { get; set; } = string.Empty;

    /// <summary>
    /// 北向资金流向
    /// </summary>
    [Description("北向资金流向")]
    public CapitalFlowDirection NorthboundCapitalFlow { get; set; }

    /// <summary>
    /// 北向资金金额（元）
    /// </summary>
    [Description("北向资金具体金额，单位：元，无数据时设为null")]
    public decimal? NorthboundCapitalAmount { get; set; }

    /// <summary>
    /// 北向资金占比（%）
    /// </summary>
    [Range(0, 100)]
    [Description("北向资金占比，单位：百分比，无数据时设为null")]
    public float? NorthboundCapitalPercentage { get; set; }

    /// <summary>
    /// 融资余额变化
    /// </summary>
    [Description("融资余额变化情况，无数据时可为空")]
    public string MarginFinancingChange { get; set; } = string.Empty;

    /// <summary>
    /// 融券余额变化
    /// </summary>
    [Description("融券余额变化情况，无数据时可为空")]
    public string MarginTradingChange { get; set; } = string.Empty;

    /// <summary>
    /// 杠杆率简述
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("杠杆率情况的简述")]
    public string LeverageDescription { get; set; } = string.Empty;
}

/// <summary>
/// 投资者行为分析
/// </summary>
[Description("投资者行为分析")]
public sealed class InvestorBehavior
{
    /// <summary>
    /// 主要行为偏差
    /// </summary>
    [Description("主要行为偏差类型")]
    public BehaviorBias MainBehaviorBias { get; set; }

    /// <summary>
    /// 行为偏差严重程度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("行为偏差严重程度评分，基于偏差对市场影响程度和持续时间。评分标准：" + ScoringStandards.Reliability)]
    public float BiasSeverityScore { get; set; }

    /// <summary>
    /// 散户特征
    /// </summary>
    [Description("散户特征")]
    public RetailInvestorCharacteristics RetailInvestorCharacteristics { get; set; }

    /// <summary>
    /// 散户活跃度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("散户活跃度评分，基于交易量和账户活跃度。评分标准：" + ScoringStandards.Reliability)]
    public float RetailActivityScore { get; set; }

    /// <summary>
    /// 机构行为一致性
    /// </summary>
    [Description("机构行为一致性")]
    public BehaviorConsistency InstitutionBehaviorConsistency { get; set; }

    /// <summary>
    /// 机构主要动向
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("机构主要动向的简述")]
    public string InstitutionMainTrend { get; set; } = string.Empty;

    /// <summary>
    /// 风险偏好
    /// </summary>
    [Description("风险偏好")]
    public RiskPreference RiskPreference { get; set; }

    /// <summary>
    /// 风险偏好变化
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("风险偏好变化的简述")]
    public string RiskPreferenceChange { get; set; } = string.Empty;
}

/// <summary>
/// 短期市场洞察与策略
/// </summary>
[Description("短期市场洞察与策略")]
public sealed class ShortTermInsight
{
    /// <summary>
    /// 市场节奏
    /// </summary>
    [Description("市场节奏")]
    public MarketRhythm MarketRhythm { get; set; }

    /// <summary>
    /// 市场节奏判断
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("对市场节奏的判断简述")]
    public string MarketRhythmRationale { get; set; } = string.Empty;

    /// <summary>
    /// 当前热点板块
    /// </summary>
    [MinLength(5)]
    [MaxLength(100)]
    [Description("当前热点板块列举")]
    public string HotSectors { get; set; } = string.Empty;

    /// <summary>
    /// 热点持续性评估
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("对热点持续性的评估")]
    public string HotnessSustainabilityAssessment { get; set; } = string.Empty;

    /// <summary>
    /// 短线机会简述
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("短线/波段/套利机会的简述")]
    public string ShortTermOpportunities { get; set; } = string.Empty;

    /// <summary>
    /// 操作建议
    /// </summary>
    [Description("操作建议")]
    public OperationRecommendation OperationRecommendation { get; set; }

    /// <summary>
    /// 仓位建议
    /// </summary>
    [Description("仓位建议")]
    public PositionRecommendation PositionRecommendation { get; set; }

    /// <summary>
    /// 最佳时机或条件
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("具体时间点或条件的描述")]
    public string BestTiming { get; set; } = string.Empty;

    /// <summary>
    /// 目标价格范围
    /// </summary>
    [MinLength(5)]
    [MaxLength(100)]
    [Description("目标价格范围描述")]
    public string TargetPriceRange { get; set; } = string.Empty;

    /// <summary>
    /// 止损位置
    /// </summary>
    [MinLength(5)]
    [MaxLength(100)]
    [Description("止损位置描述")]
    public string StopLossPosition { get; set; } = string.Empty;

    /// <summary>
    /// 需规避的心理陷阱
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("最需规避的1个心理陷阱")]
    public string PsychologicalTrapToAvoid { get; set; } = string.Empty;
}
