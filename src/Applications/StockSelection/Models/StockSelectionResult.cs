using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace MarketAssistant.Applications.StockSelection.Models;

/// <summary>
/// 股票选择结果
/// </summary>
[Description("股票选择分析结果，包含推荐股票列表和分析报告")]
public class StockSelectionResult
{
    /// <summary>
    /// 选股类型
    /// </summary>
    [Description("选股类型：user_request(用户需求分析) 或 news_based(新闻分析)")]
    public SelectionType SelectionType { get; set; }

    /// <summary>
    /// AI分析摘要
    /// </summary>
    [MinLength(50)]
    [MaxLength(500)]
    [Description("整体分析总结，概述核心观点和结论，字数要求：100-300字")]
    public string AnalysisSummary { get; set; } = string.Empty;

    /// <summary>
    /// 市场环境分析
    /// </summary>
    [MinLength(50)]
    [MaxLength(400)]
    [Description("市场环境分析，包括宏观环境、行业周期、市场情绪等，字数要求：100-200字")]
    public string MarketEnvironmentAnalysis { get; set; } = string.Empty;

    /// <summary>
    /// 推荐股票列表
    /// </summary>
    [Description("推荐股票列表，按推荐优先级从高到低排序。若无合适股票则返回空数组[]")]
    public List<StockRecommendation> Recommendations { get; set; } = new();

    /// <summary>
    /// 风险提示
    /// </summary>
    [MinLength(3)]
    [MaxLength(10)]
    [Description("投资风险警示，每条风险提示应清晰具体，建议3-5条，每条30-100字")]
    public List<string> RiskWarnings { get; set; } = new();

    /// <summary>
    /// 投资建议
    /// </summary>
    [MinLength(50)]
    [MaxLength(400)]
    [Description("综合投资建议，包括仓位管理、操作策略、注意事项等，字数要求：100-200字")]
    public string InvestmentAdvice { get; set; } = string.Empty;

    /// <summary>
    /// 置信度评分 (0-100)
    /// </summary>
    [Range(0, 100)]
    [Description("AI置信度评分，范围0-100，不含引号，数值越高表示信心越强")]
    public float ConfidenceScore { get; set; }
}

