namespace MarketAssistant.Applications.StockSelection.Models;

/// <summary>
/// 股票选择结果
/// </summary>
public class StockSelectionResult
{
    /// <summary>
    /// 请求ID
    /// </summary>
    public string RequestId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 生成时间
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    /// <summary>
    /// 选股类型
    /// </summary>
    public string SelectionType { get; set; } = string.Empty; // "user_request" 或 "news_based"

    /// <summary>
    /// AI分析摘要
    /// </summary>
    public string AnalysisSummary { get; set; } = string.Empty;

    /// <summary>
    /// 市场环境分析
    /// </summary>
    public string MarketEnvironmentAnalysis { get; set; } = string.Empty;

    /// <summary>
    /// 推荐股票列表
    /// </summary>
    public List<StockRecommendation> Recommendations { get; set; } = new();

    /// <summary>
    /// 风险提示
    /// </summary>
    public List<string> RiskWarnings { get; set; } = new();

    /// <summary>
    /// 投资建议
    /// </summary>
    public string InvestmentAdvice { get; set; } = string.Empty;

    /// <summary>
    /// 置信度评分 (0-100)
    /// </summary>
    public float ConfidenceScore { get; set; }
}

