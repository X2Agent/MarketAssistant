using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

[Description("基本面分析师的结构化分析结果，包含公司基本面、行业竞争和投资价值评估")]
public sealed class FundamentalAnalysisResult
{
    [Description("股票的基本信息，包括代码、名称、价格等")]
    public StockBasicInfo BasicInfo { get; set; } = new();

    [Description("公司的基本面状况，包括行业定位、核心业务、盈利能力和财务稳健性")]
    public CompanyFundamentals Fundamentals { get; set; } = new();

    [Description("行业生命周期、市场地位、竞争力和壁垒分析")]
    public IndustryCompetitiveness Competition { get; set; } = new();

    [Description("增长驱动因素、估值水平、投资评级和关键亮点风险")]
    public GrowthAndValue GrowthValue { get; set; } = new();
}

[Description("股票的基本信息")]
public sealed class StockBasicInfo
{
    [Description("股票代码，如 SH600000")]
    public string Symbol { get; set; } = string.Empty;

    [Description("公司名称")]
    public string Name { get; set; } = string.Empty;

    [Range(0.01, 100000)]
    [Description("实时股价或最新收盘价，单位：元")]
    public decimal CurrentPrice { get; set; }

    [Range(-100, 100)]
    [Description("日涨跌幅，单位：百分比")]
    public float DailyChangePercent { get; set; }

    [Description("日涨跌额，单位：元")]
    public decimal DailyChangeAmount { get; set; }
}

[Description("公司基本面状况")]
public sealed class CompanyFundamentals
{
    [MinLength(2)]
    [MaxLength(50)]
    [Description("所属行业分类")]
    public string Industry { get; set; } = string.Empty;

    [Range(1, 10)]
    [Description("行业成长性评分，基于行业增长率的量化评分。评分标准：" + ScoringStandards.Quality)]
    public float IndustryGrowthScore { get; set; }

    [MinLength(10)]
    [MaxLength(200)]
    [Description("核心业务及主要产品/服务的简要描述，不超过2个关键词")]
    public string CoreBusiness { get; set; } = string.Empty;

    [Range(1, 10)]
    [Description("业务质量评分，综合考虑业务模式、市场需求、竞争格局等因素。评分标准：" + ScoringStandards.Quality)]
    public float BusinessQualityScore { get; set; }

    [MinLength(20)]
    [MaxLength(200)]
    [Description("盈利能力概况，如'盈利能力强，毛利率和净利率均处于行业领先水平'，不需要具体数值")]
    public string ProfitabilityOverview { get; set; } = string.Empty;

    [Description("盈利趋势定性评估")]
    public ProfitabilityTrend ProfitabilityTrend { get; set; }

    [MinLength(20)]
    [MaxLength(200)]
    [Description("财务健康状况概况，如'财务稳健，负债率较低，现金流充裕'，不需要具体数值")]
    public string FinancialHealthOverview { get; set; } = string.Empty;

    [Description("现金流状况定性评估")]
    public CashFlowStatus CashFlowStatus { get; set; }
}

[Description("行业与竞争分析")]
public sealed class IndustryCompetitiveness
{
    [Description("行业生命周期阶段")]
    public IndustryLifecycle IndustryLifecycle { get; set; }

    [Range(1, 10)]
    [Description("对行业生命周期判断的确信度评分，基于行业数据完整性和判断依据的充分性。评分标准：" + ScoringStandards.Reliability)]
    public float LifecycleConfidenceScore { get; set; }

    [Description("公司在行业中的市场地位")]
    public MarketPosition MarketPosition { get; set; }

    [MinLength(5)]
    [MaxLength(100)]
    [Description("市场份额或行业排名的简要说明")]
    public string MarketShareDescription { get; set; } = string.Empty;

    [MinLength(10)]
    [MaxLength(200)]
    [Description("最主要的1-2项竞争优势，如技术/品牌/成本/渠道")]
    public string CoreCompetence { get; set; } = string.Empty;

    [Range(1, 10)]
    [Description("竞争力强度评分，基于竞争优势的持续性和差异化程度。评分标准：" + ScoringStandards.Strength)]
    public float CompetenceStrengthScore { get; set; }

    [Description("长期竞争壁垒水平")]
    public Level BarrierLevel { get; set; }

    [MinLength(5)]
    [MaxLength(100)]
    [Description("主要壁垒类型的简要说明")]
    public string BarrierDescription { get; set; } = string.Empty;
}

[Description("增长潜力与投资价值评估")]
public sealed class GrowthAndValue
{
    [MinLength(10)]
    [MaxLength(200)]
    [Description("1-2项主要增长来源的描述")]
    public string GrowthDrivers { get; set; } = string.Empty;

    [Range(1, 10)]
    [Description("增长持续性评分，基于增长驱动因素的稳定性和可预测性。评分标准：" + ScoringStandards.Strength)]
    public float GrowthSustainabilityScore { get; set; }

    [MinLength(10)]
    [MaxLength(200)]
    [Description("PE/PB/PS与行业均值的对比简述")]
    public string ValuationDescription { get; set; } = string.Empty;

    [Description("投资评级建议")]
    public InvestmentRating InvestmentRating { get; set; }

    [MinLength(10)]
    [MaxLength(200)]
    [Description("合理估值空间或目标价的简述")]
    public string ValuationTarget { get; set; } = string.Empty;

    [MinLength(1)]
    [MaxLength(2)]
    [Description("核心投资优势")]
    public List<string> InvestmentHighlights { get; set; } = new();

    [MinLength(10)]
    [MaxLength(200)]
    [Description("最主要的1项风险因素描述")]
    public string KeyRisk { get; set; } = string.Empty;
}
