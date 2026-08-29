using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

[Description("协调分析师的综合判断结果，整合所有分析师意见、解决分歧、提供最终投资建议")]
public sealed class CoordinatorResult
{
    [Range(1, 10)]
    [Description("综合评分，基于所有分析师意见、冲突解决、搜索验证后的最终判断。评分标准：" + ScoringStandards.Quality)]
    public float OverallScore { get; set; }

    [Description("最终投资评级")]
    public InvestmentRating InvestmentRating { get; set; }

    [MinLength(5)]
    [MaxLength(50)]
    [Description("综合目标价格区间，例如：'45-50 元'，综合考虑基本面估值、技术目标位、资金推动、事件影响")]
    public string TargetPrice { get; set; } = string.Empty;

    [MinLength(10)]
    [MaxLength(100)]
    [Description("综合价格变化预期，例如：'综合判断预计上涨 8-12%'，基于证据，避免过度乐观或悲观")]
    public string PriceChangeExpectation { get; set; } = string.Empty;

    [Description("建议的投资时间维度")]
    public Duration TimeHorizon { get; set; }

    [MinLength(5)]
    [MaxLength(50)]
    [Description("投资时间维度的补充说明，例如：'中期 6-12 个月'")]
    public string TimeHorizonDescription { get; set; } = string.Empty;

    [Description("综合风险等级")]
    public Level RiskLevel { get; set; }

    [Range(0, 100)]
    [Description("置信度百分比，基于信息完整性和一致性。评分标准：" + ScoringStandards.Confidence)]
    public float ConfidencePercentage { get; set; }

    [Description("各维度评分，包含基本面、技术面、财务面、市场情绪和新闻事件的评分。评分范围 1-10。评分标准：" + ScoringStandards.Performance)]
    public AnalysisDimensionScores DimensionScores { get; set; } = new();

    [MinLength(1)]
    [MaxLength(3)]
    [Description("核心的投资驱动因素，结合最新信息，避免空洞描述")]
    public List<string> InvestmentHighlights { get; set; } = new();

    [MinLength(1)]
    [MaxLength(3)]
    [Description("主要的潜在风险，包含新识别的风险")]
    public List<string> RiskFactors { get; set; } = new();

    [MinLength(3)]
    [MaxLength(5)]
    [Description("可执行的操作建议，整合各分析师建议，提供具体方案（入场点、止损位、仓位管理）。例如：'建议在 45-48 元区间分批建仓（技术支撑+估值合理）'")]
    public List<string> OperationSuggestions { get; set; } = new();

    [MinLength(50)]
    [MaxLength(200)]
    [Description("总结所有分析师一致认同的关键观点")]
    public string ConsensusAnalysis { get; set; } = string.Empty;

    [MinLength(50)]
    [MaxLength(200)]
    [Description("明确指出分歧点和你的综合判断，如使用了搜索工具需说明搜索结果及其影响。例如：'基本面看涨20%，技术面看涨5%。经搜索验证，市场已提前反应业绩预期，综合判断为上涨10-12%'")]
    public string DisagreementAnalysis { get; set; } = string.Empty;

    [MinLength(10)]
    [MaxLength(50)]
    [Description("高度凝练投资结论")]
    public string Summary { get; set; } = string.Empty;

    [MinLength(6)]
    [MaxLength(10)]
    [Description("各专业分析师的自然语言分析中提取的最关键指标和数据点，数据具体、判断清晰、建议可行")]
    public List<KeyIndicator> KeyIndicators { get; set; } = new();

    [Description("分析质量自评估，包含数据完整度、分析师一致性和局限性说明")]
    public AnalysisQualityMetrics QualityMetrics { get; set; } = new();
}

[Description("从各分析师的自然语言分析中提取的关键指标和数据点")]
public sealed class KeyIndicator
{
    [MinLength(4)]
    [MaxLength(30)]
    [Description("指标来源分析师，枚举值：技术分析师/财务分析师/基本面分析师/市场情绪分析师/新闻事件分析师")]
    public string AnalystSource { get; set; } = string.Empty;

    [MinLength(3)]
    [MaxLength(20)]
    [Description("指标类别，枚举值：技术指标/财务数据/估值指标/市场数据/事件影响")]
    public string Category { get; set; } = string.Empty;

    [MinLength(2)]
    [MaxLength(30)]
    [Description("具体的指标或数据点名称。例如：MACD、RSI、ROE、PE、市场份额、主力资金流向")]
    public string Name { get; set; } = string.Empty;

    [MinLength(1)]
    [MaxLength(50)]
    [Description("具体数值或状态描述。例如：金叉、65、15.2%、25倍、行业第三、连续3日净流入")]
    public string Value { get; set; } = string.Empty;

    [MinLength(2)]
    [MaxLength(30)]
    [Description("明确的方向性判断。例如：买入、卖出、中性、健康、风险、合理、超买、超卖、强势")]
    public string Signal { get; set; } = string.Empty;

    [MinLength(5)]
    [MaxLength(100)]
    [Description("基于该指标的操作建议或关注点。例如：短期目标价50元、关注回调风险、继续持有、设置止损位42元")]
    public string Suggestion { get; set; } = string.Empty;
}

[Description("各维度评分详情")]
public sealed class AnalysisDimensionScores
{
    [Range(1, 10)]
    [Description("基本面评分")]
    public float Fundamental { get; set; }

    [Range(1, 10)]
    [Description("技术面评分")]
    public float Technical { get; set; }

    [Range(1, 10)]
    [Description("财务面评分")]
    public float Financial { get; set; }

    [Range(1, 10)]
    [Description("市场情绪评分")]
    public float Sentiment { get; set; }

    [Range(1, 10)]
    [Description("新闻事件评分")]
    public float News { get; set; }
}
