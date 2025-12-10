using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 新闻事件分析结果
/// </summary>
[Description("新闻事件分析师的结构化分析结果，包含事件解读、影响评估和投资启示")]
public sealed class NewsEventAnalysisResult
{
    /// <summary>
    /// 事件解读与定性
    /// </summary>
    [Description("事件解读与定性，包括事件类型、概要、信息来源和事件性质")]
    public EventInterpretation EventAnalysis { get; set; } = new();

    /// <summary>
    /// 影响评估与市场反应
    /// </summary>
    [Description("影响评估与市场反应，包括基本面影响、情绪影响、影响范围、市场反应和资金流向")]
    public ImpactAssessment ImpactEvaluation { get; set; } = new();

    /// <summary>
    /// 投资启示与建议
    /// </summary>
    [Description("投资启示与建议，包括投资影响、应对策略、关注重点和风险提示")]
    public InvestmentInsight InvestmentGuidance { get; set; } = new();
}

/// <summary>
/// 事件解读与定性
/// </summary>
[Description("事件解读与定性")]
public sealed class EventInterpretation
{
    /// <summary>
    /// 事件类型
    /// </summary>
    [Description("事件类型")]
    public EventType EventType { get; set; }

    /// <summary>
    /// 事件概要
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("简要描述事件核心内容，不超过30字")]
    public string EventSummary { get; set; } = string.Empty;

    /// <summary>
    /// 信息来源
    /// </summary>
    [Description("信息来源")]
    public InformationSource InformationSource { get; set; }

    /// <summary>
    /// 信息可信度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("信息可信度评分，基于信息来源的权威性。评分标准：" + ScoringStandards.Credibility)]
    public float CredibilityScore { get; set; }

    /// <summary>
    /// 事件性质
    /// </summary>
    [Description("事件性质")]
    public EventNature EventNature { get; set; }

    /// <summary>
    /// 事件重要性评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("事件重要性评分，基于影响范围和影响程度综合判断。评分标准：" + ScoringStandards.Reliability)]
    public float ImportanceScore { get; set; }
}

/// <summary>
/// 影响评估与市场反应
/// </summary>
[Description("影响评估与市场反应")]
public sealed class ImpactAssessment
{
    /// <summary>
    /// 基本面影响
    /// </summary>
    [Description("基本面影响方向")]
    public ImpactDirection FundamentalImpact { get; set; }

    /// <summary>
    /// 基本面影响程度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("基本面影响程度评分，基于对公司盈利、现金流、竞争地位的影响程度。评分标准：" + ScoringStandards.Reliability)]
    public float FundamentalImpactScore { get; set; }

    /// <summary>
    /// 基本面影响逻辑
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("基本面具体影响逻辑的简述")]
    public string FundamentalImpactLogic { get; set; } = string.Empty;

    /// <summary>
    /// 情绪影响
    /// </summary>
    [Description("情绪影响方向")]
    public ImpactDirection SentimentImpact { get; set; }

    /// <summary>
    /// 情绪强度评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("情绪强度评分，基于市场情绪波动幅度和传播速度。评分标准：" + ScoringStandards.EmotionIntensity)]
    public float SentimentIntensityScore { get; set; }

    /// <summary>
    /// 市场情绪预期变化
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("市场情绪预期变化的简述")]
    public string SentimentChangeExpectation { get; set; } = string.Empty;

    /// <summary>
    /// 影响范围
    /// </summary>
    [Description("影响范围")]
    public ImpactScope ImpactScope { get; set; }

    /// <summary>
    /// 影响持续时长
    /// </summary>
    [Description("影响持续时长")]
    public Duration ImpactDuration { get; set; }

    /// <summary>
    /// 具体预期时间
    /// </summary>
    [MinLength(5)]
    [MaxLength(100)]
    [Description("影响持续的具体预期时间描述")]
    public string ExpectedTimeframe { get; set; } = string.Empty;

    /// <summary>
    /// 市场预期反应
    /// </summary>
    [Description("市场预期反应")]
    public MarketReactionExpectation MarketExpectedReaction { get; set; }

    /// <summary>
    /// 股价预期变化
    /// </summary>
    [Description("股价预期变化")]
    public PriceChangeExpectation PriceChangeExpectation { get; set; }

    /// <summary>
    /// 资金流向预期
    /// </summary>
    [Description("资金流向预期")]
    public CapitalFlowDirection CapitalFlowExpectation { get; set; }

    /// <summary>
    /// 资金规模预估
    /// </summary>
    [MinLength(10)]
    [MaxLength(100)]
    [Description("资金规模预估的简述")]
    public string CapitalScaleEstimate { get; set; } = string.Empty;
}

/// <summary>
/// 投资启示与建议
/// </summary>
[Description("投资启示与建议")]
public sealed class InvestmentInsight
{
    /// <summary>
    /// 投资影响评估
    /// </summary>
    [Description("投资影响评估")]
    public InvestmentImpactAssessment InvestmentImpactAssessment { get; set; }

    /// <summary>
    /// 核心投资逻辑
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("核心投资逻辑的简述")]
    public string CoreInvestmentLogic { get; set; } = string.Empty;

    /// <summary>
    /// 应对策略建议
    /// </summary>
    [Description("应对策略建议")]
    public OperationRecommendation ResponseStrategy { get; set; }

    /// <summary>
    /// 具体操作建议
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("具体操作建议，如关注点、入场/出场时机")]
    public string SpecificActionAdvice { get; set; } = string.Empty;

    /// <summary>
    /// 关注重点列表
    /// </summary>
    [MinLength(1)]
    [MaxLength(2)]
    [Description("需要持续关注的后续发展或潜在催化剂")]
    public List<string> FocusPoints { get; set; } = new();

    /// <summary>
    /// 关键风险提示
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("最主要且需要规避的1个风险因素")]
    public string KeyRiskAlert { get; set; } = string.Empty;
}
