using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

[Description("项目指标分析师的结构化分析结果，包含市值与供应量、流动性、波动性风险和市场结构评估")]
public sealed class CryptoMetricsAnalysisResult
{
    [Description("市值规模、流通率、供应量结构与排名评估")]
    public CryptoMarketCapAssessment MarketCap { get; set; } = new();

    [Description("订单簿深度、成交活跃度与交易所分布等流动性评估")]
    public CryptoLiquidityAssessment Liquidity { get; set; } = new();

    [Description("历史波动率、最大回撤、风险收益比（夏普比率）等风险评估")]
    public CryptoVolatilityRisk VolatilityRisk { get; set; } = new();

    [Description("买卖盘力量、近期成交倾向与整体市场结构判断")]
    public CryptoMarketStructure MarketStructure { get; set; } = new();
}

[Description("市值与供应量评估")]
public sealed class CryptoMarketCapAssessment
{
    [Range(1, 10)]
    [Description("市值规模与排名的综合评分，无数据时设为null")]
    public float? MarketCapScore { get; set; }

    [Description("流通量占总供应量比例及解锁压力评估")]
    public string? CirculatingAssessment { get; set; }
}

[Description("流动性评估")]
public sealed class CryptoLiquidityAssessment
{
    [Range(1, 10)]
    [Description("基于深度、成交量与交易所分布的流动性综合评分，无数据时设为null")]
    public float? LiquidityScore { get; set; }

    [Description("不同交易所交易量占比与分布特征说明，无数据时设为null")]
    public string? VolumeDistributionNotes { get; set; }
}

[Description("波动性风险评估")]
public sealed class CryptoVolatilityRisk
{
    [Range(1, 10)]
    [Description("波动率、回撤与风险收益比的综合评分，无数据时设为null")]
    public float? RiskScore { get; set; }

    [Description("统计周期内的最大回撤描述，无数据时设为null")]
    public string? MaxDrawdown { get; set; }
}

[Description("市场结构评估")]
public sealed class CryptoMarketStructure
{
    [Range(1, 10)]
    [Description("买卖盘力量与市场结构的综合评分，无数据时设为null")]
    public float? StructureScore { get; set; }

    [Description("当前市场结构的关键观察与风险提示")]
    public string? KeyObservations { get; set; }
}
