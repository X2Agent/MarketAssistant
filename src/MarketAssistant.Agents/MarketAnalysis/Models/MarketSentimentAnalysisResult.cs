using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

[Description("市场情绪分析师的结构化分析结果，包含市场情绪、资金流向、投资者行为和短期策略")]
public sealed class MarketSentimentAnalysisResult
{
    [Description("市场情绪评估，包括主导情绪、恐慌与信心、整体氛围")]
    public MarketSentiment SentimentAssessment { get; set; } = new();

    [Description("资金流向分析，包括主力资金、机构动向、北向资金和融资融券")]
    public CapitalFlow CapitalFlowAnalysis { get; set; } = new();

    [Description("投资者行为分析，包括行为偏差、散户特征、机构行为和风险偏好")]
    public InvestorBehavior BehaviorAnalysis { get; set; } = new();

    [Description("短期市场洞察与策略，包括市场节奏、热点机会、操作建议和心理陷阱")]
    public ShortTermInsight ShortTermStrategy { get; set; } = new();
}

[Description("市场情绪评估")]
public sealed class MarketSentiment
{
    [Description("主导情绪")]
    public DominantEmotion DominantEmotion { get; set; }

    [Range(1, 10)]
    [Description("情绪强度评分，基于情绪指标偏离程度。评分标准：" + ScoringStandards.EmotionIntensity)]
    public float EmotionIntensityScore { get; set; }

    [Description("VIX水平或情绪指数的具体数值，无数据时可为空")]
    public string VIXLevel { get; set; } = string.Empty;

    [Description("投资者信心水平")]
    public Level InvestorConfidenceLevel { get; set; }

    [MinLength(10)]
    [MaxLength(100)]
    [Description("投资者信心变化趋势的简述")]
    public string ConfidenceTrendDescription { get; set; } = string.Empty;

    [Description("整体市场氛围")]
    public MarketAtmosphere OverallAtmosphere { get; set; }

    [Range(1, 10)]
    [Description("氛围强度评分，基于市场参与度和情绪蔓延程度。评分标准：" + ScoringStandards.EmotionIntensity)]
    public float AtmosphereIntensityScore { get; set; }
}

[Description("资金流向分析")]
public sealed class CapitalFlow
{
    [Description("主力资金流向")]
    public CapitalFlowDirection MainCapitalFlow { get; set; }

    [Description("主力资金具体金额，单位：元，无数据时设为null")]
    public decimal? MainCapitalAmount { get; set; }

    [Range(0, 1000)]
    [Description("主力资金流入或流出的连续天数，无数据时设为null")]
    public int? MainCapitalConsecutiveDays { get; set; }

    [Description("机构动向")]
    public InstitutionTrend InstitutionTrend { get; set; }

    [MinLength(10)]
    [MaxLength(100)]
    [Description("机构持仓变化的简述")]
    public string InstitutionPositionChange { get; set; } = string.Empty;

    [Description("北向资金流向")]
    public CapitalFlowDirection NorthboundCapitalFlow { get; set; }

    [Description("北向资金具体金额，单位：元，无数据时设为null")]
    public decimal? NorthboundCapitalAmount { get; set; }

    [Range(0, 100)]
    [Description("北向资金占比，单位：百分比，无数据时设为null")]
    public float? NorthboundCapitalPercentage { get; set; }

    [Description("融资余额变化情况，无数据时可为空")]
    public string MarginFinancingChange { get; set; } = string.Empty;

    [Description("融券余额变化情况，无数据时可为空")]
    public string MarginTradingChange { get; set; } = string.Empty;

    [MinLength(10)]
    [MaxLength(100)]
    [Description("杠杆率情况的简述")]
    public string LeverageDescription { get; set; } = string.Empty;
}

[Description("投资者行为分析")]
public sealed class InvestorBehavior
{
    [Description("主要行为偏差类型")]
    public BehaviorBias MainBehaviorBias { get; set; }

    [Range(1, 10)]
    [Description("行为偏差严重程度评分，基于偏差对市场影响程度和持续时间。评分标准：" + ScoringStandards.Reliability)]
    public float BiasSeverityScore { get; set; }

    [Description("散户特征")]
    public RetailInvestorCharacteristics RetailInvestorCharacteristics { get; set; }

    [Range(1, 10)]
    [Description("散户活跃度评分，基于交易量和账户活跃度。评分标准：" + ScoringStandards.Reliability)]
    public float RetailActivityScore { get; set; }

    [Description("机构行为一致性")]
    public BehaviorConsistency InstitutionBehaviorConsistency { get; set; }

    [MinLength(10)]
    [MaxLength(100)]
    [Description("机构主要动向的简述")]
    public string InstitutionMainTrend { get; set; } = string.Empty;

    [Description("风险偏好")]
    public RiskPreference RiskPreference { get; set; }

    [MinLength(10)]
    [MaxLength(100)]
    [Description("风险偏好变化的简述")]
    public string RiskPreferenceChange { get; set; } = string.Empty;
}

[Description("短期市场洞察与策略")]
public sealed class ShortTermInsight
{
    [Description("市场节奏")]
    public MarketRhythm MarketRhythm { get; set; }

    [MinLength(10)]
    [MaxLength(100)]
    [Description("对市场节奏的判断简述")]
    public string MarketRhythmRationale { get; set; } = string.Empty;

    [MinLength(5)]
    [MaxLength(100)]
    [Description("当前热点板块列举")]
    public string HotSectors { get; set; } = string.Empty;

    [MinLength(10)]
    [MaxLength(100)]
    [Description("对热点持续性的评估")]
    public string HotnessSustainabilityAssessment { get; set; } = string.Empty;

    [MinLength(10)]
    [MaxLength(200)]
    [Description("短线/波段/套利机会的简述")]
    public string ShortTermOpportunities { get; set; } = string.Empty;

    [Description("操作建议")]
    public OperationRecommendation OperationRecommendation { get; set; }

    [Description("仓位建议")]
    public PositionRecommendation PositionRecommendation { get; set; }

    [MinLength(10)]
    [MaxLength(100)]
    [Description("具体时间点或条件的描述")]
    public string BestTiming { get; set; } = string.Empty;

    [MinLength(5)]
    [MaxLength(100)]
    [Description("目标价格范围描述")]
    public string TargetPriceRange { get; set; } = string.Empty;

    [MinLength(5)]
    [MaxLength(100)]
    [Description("止损位置描述")]
    public string StopLossPosition { get; set; } = string.Empty;

    [MinLength(10)]
    [MaxLength(100)]
    [Description("最需规避的1个心理陷阱")]
    public string PsychologicalTrapToAvoid { get; set; } = string.Empty;
}
