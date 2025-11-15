using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 财务分析结果
/// </summary>
[Description("财务分析师的结构化分析结果，包含财务健康、盈利质量、现金流和风险预警")]
public sealed class FinancialAnalysisResult
{
    /// <summary>
    /// 财务健康评估
    /// </summary>
    [Description("公司财务健康状况评估，包括偿债能力、资产负债结构和整体稳健性")]
    public FinancialHealth HealthAssessment { get; set; } = new();

    /// <summary>
    /// 盈利质量分析
    /// </summary>
    [Description("公司盈利能力和质量分析，包括盈利能力、投入产出效率和利润质量")]
    public ProfitabilityQuality ProfitQuality { get; set; } = new();

    /// <summary>
    /// 现金流评估
    /// </summary>
    [Description("现金流状况评估，包括经营现金流、自由现金流和现金转换周期")]
    public CashFlowAssessment CashFlow { get; set; } = new();

    /// <summary>
    /// 财务风险预警
    /// </summary>
    [Description("财务风险识别和预警，包括主要风险指标、造假风险和关注点")]
    public FinancialRiskWarning RiskWarning { get; set; } = new();
}

/// <summary>
/// 财务健康评估
/// </summary>
[Description("财务健康状况评估")]
public sealed class FinancialHealth
{
    /// <summary>
    /// 流动比率
    /// </summary>
    [Range(0, 100)]
    [Description("流动比率，范围0-100，无数据时设为null")]
    public float? CurrentRatio { get; set; }

    /// <summary>
    /// 速动比率
    /// </summary>
    [Range(0, 100)]
    [Description("速动比率，范围0-100，无数据时设为null")]
    public float? QuickRatio { get; set; }

    /// <summary>
    /// 偿债能力评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("偿债能力综合评分，基于流动比率、速动比率等指标综合评估。评分标准：" + ScoringStandards.Strength)]
    public float SolvencyScore { get; set; }

    /// <summary>
    /// 偿债能力评估
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("偿债能力的简要评估说明")]
    public string SolvencyAssessment { get; set; } = string.Empty;

    /// <summary>
    /// 资产负债率（%）
    /// </summary>
    [Range(0, 1000)]
    [Description("资产负债率，单位：百分比，无数据时设为null")]
    public float? DebtRatio { get; set; }

    /// <summary>
    /// 资产负债率同比变化
    /// </summary>
    [Description("资产负债率同比变化趋势")]
    public TrendChange DebtRatioTrend { get; set; }

    /// <summary>
    /// 债务结构评估
    /// </summary>
    [Description("债务结构健康程度")]
    public DebtStructureAssessment DebtStructureAssessment { get; set; }

    /// <summary>
    /// 整体财务稳健性
    /// </summary>
    [Description("整体财务稳健性水平")]
    public FinancialStability OverallStability { get; set; }

    /// <summary>
    /// 财务稳健性评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("整体财务稳健性评分，综合考虑偿债能力、资产质量、现金流状况。评分标准：" + ScoringStandards.Quality)]
    public float StabilityScore { get; set; }

    /// <summary>
    /// 核心观点
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("对财务稳健性的核心观点说明")]
    public string CoreInsight { get; set; } = string.Empty;
}

/// <summary>
/// 盈利质量分析
/// </summary>
[Description("盈利能力和质量分析")]
public sealed class ProfitabilityQuality
{
    /// <summary>
    /// 毛利率（%）
    /// </summary>
    [Range(-100, 100)]
    [Description("毛利率，单位：百分比，无数据时设为null")]
    public float? GrossMargin { get; set; }

    /// <summary>
    /// 净利率（%）
    /// </summary>
    [Range(-100, 100)]
    [Description("净利率，单位：百分比，无数据时设为null")]
    public float? NetMargin { get; set; }

    /// <summary>
    /// 净利率同比变化
    /// </summary>
    [Description("净利率同比变化趋势")]
    public TrendChange NetMarginTrend { get; set; }

    /// <summary>
    /// 盈利趋势评估
    /// </summary>
    [Description("盈利趋势评估")]
    public ProfitTrendAssessment ProfitTrendAssessment { get; set; }

    /// <summary>
    /// ROE净资产收益率（%）
    /// </summary>
    [Range(-100, 1000)]
    [Description("ROE净资产收益率，单位：百分比，无数据时设为null")]
    public float? ROE { get; set; }

    /// <summary>
    /// ROA总资产回报率（%）
    /// </summary>
    [Range(-100, 1000)]
    [Description("ROA总资产回报率，单位：百分比，无数据时设为null")]
    public float? ROA { get; set; }

    /// <summary>
    /// 行业对比
    /// </summary>
    [Description("与行业平均水平对比")]
    public Level IndustryComparison { get; set; }

    /// <summary>
    /// 利润质量
    /// </summary>
    [Description("利润质量等级")]
    public Level ProfitQualityLevel { get; set; }

    /// <summary>
    /// 利润质量评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("利润质量评分，基于利润真实性、可持续性、现金流支撑度。评分标准：" + ScoringStandards.Quality)]
    public float ProfitQualityScore { get; set; }

    /// <summary>
    /// 利润来源可持续性说明
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("对利润来源可持续性及真实性的说明")]
    public string ProfitSustainability { get; set; } = string.Empty;
}

/// <summary>
/// 现金流评估
/// </summary>
[Description("现金流状况评估")]
public sealed class CashFlowAssessment
{
    /// <summary>
    /// 经营现金流净额（元）
    /// </summary>
    [Description("经营现金流净额，单位：元，无数据时设为null")]
    public decimal? OperatingCashFlow { get; set; }

    /// <summary>
    /// 经营现金流与净利润比值
    /// </summary>
    [Range(-100, 100)]
    [Description("经营现金流与净利润的比值，无数据时设为null")]
    public float? CashFlowToNetIncomeRatio { get; set; }

    /// <summary>
    /// 现金流质量评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("现金流质量评分，基于经营现金流与净利润匹配度。评分标准：" + ScoringStandards.Quality)]
    public float CashFlowQualityScore { get; set; }

    /// <summary>
    /// 自由现金流状态
    /// </summary>
    [Description("自由现金流状态")]
    public FreeCashFlowStatus FreeCashFlowStatus { get; set; }

    /// <summary>
    /// 自由现金流趋势
    /// </summary>
    [Description("自由现金流趋势")]
    public FreeCashFlowTrend FreeCashFlowTrend { get; set; }

    /// <summary>
    /// 自由现金流可持续性评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("自由现金流可持续性评分，基于现金流稳定性和增长性。评分标准：" + ScoringStandards.Strength)]
    public float FreeCashFlowSustainabilityScore { get; set; }

    /// <summary>
    /// 现金转换周期（天）
    /// </summary>
    [Range(-1000, 10000)]
    [Description("现金转换周期，单位：天，无数据时设为null")]
    public int? CashConversionCycle { get; set; }

    /// <summary>
    /// 现金转换周期同比变化
    /// </summary>
    [Description("现金转换周期同比变化")]
    public TrendChange CashConversionCycleTrend { get; set; }

    /// <summary>
    /// 效率简述
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("对现金转换效率的简要说明")]
    public string EfficiencyDescription { get; set; } = string.Empty;
}

/// <summary>
/// 财务风险预警
/// </summary>
[Description("财务风险识别和预警")]
public sealed class FinancialRiskWarning
{
    /// <summary>
    /// 主要风险指标列表
    /// </summary>
    [MinLength(1)]
    [MaxLength(2)]
    [Description("1-2个最异常或需关注的财务指标，如高负债、现金流压力等")]
    public List<string> KeyRiskIndicators { get; set; } = new();

    /// <summary>
    /// 财务造假风险等级
    /// </summary>
    [Description("财务造假风险等级")]
    public Level FraudRiskLevel { get; set; }

    /// <summary>
    /// 财务造假风险评分（1-10分）
    /// </summary>
    [Range(1, 10)]
    [Description("财务造假风险评分，分数越高风险越高，基于财务异常指标和关联交易风险。评分标准：" + ScoringStandards.Risk)]
    public float FraudRiskScore { get; set; }

    /// <summary>
    /// 风险判断依据
    /// </summary>
    [MinLength(10)]
    [MaxLength(200)]
    [Description("对财务造假风险的判断依据说明")]
    public string FraudRiskRationale { get; set; } = string.Empty;

    /// <summary>
    /// 建议关注点列表
    /// </summary>
    [MinLength(1)]
    [MaxLength(2)]
    [Description("1-2个财务层面需持续关注或改善的方面")]
    public List<string> MonitoringPoints { get; set; } = new();
}

