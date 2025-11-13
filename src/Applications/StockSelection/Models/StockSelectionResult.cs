using System.ComponentModel;

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
    [Description("选股类型：user_request(用户需求) 或 news_based(新闻分析)")]
    public string SelectionType { get; set; } = string.Empty;

    /// <summary>
    /// AI分析摘要
    /// </summary>
    [Description("整体分析总结，概述核心观点和结论，建议100-300字")]
    public string AnalysisSummary { get; set; } = string.Empty;

    /// <summary>
    /// 市场环境分析
    /// </summary>
    [Description("当前市场环境和行业趋势分析，包括宏观环境、行业周期、市场情绪等，建议100-200字")]
    public string MarketEnvironmentAnalysis { get; set; } = string.Empty;

    /// <summary>
    /// 推荐股票列表
    /// </summary>
    [Description("推荐的股票列表，按推荐优先级排序")]
    public List<StockRecommendation> Recommendations { get; set; } = new();

    /// <summary>
    /// 风险提示
    /// </summary>
    [Description("投资风险警示列表，每条风险提示应清晰具体，建议3-5条")]
    public List<string> RiskWarnings { get; set; } = new();

    /// <summary>
    /// 投资建议
    /// </summary>
    [Description("综合投资建议，包括仓位管理、操作策略、注意事项等，建议100-200字")]
    public string InvestmentAdvice { get; set; } = string.Empty;

    /// <summary>
    /// 置信度评分 (0-100)
    /// </summary>
    [Description("AI对本次分析结果的置信度评分，范围0-100，数值越高表示信心越强")]
    public float ConfidenceScore { get; set; }
}

