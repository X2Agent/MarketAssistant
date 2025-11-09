namespace MarketAssistant.Applications.StockSelection.Models;

/// <summary>
/// 股票推荐结果
/// </summary>
public class StockRecommendation
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// AI推荐评分 (0-100)
    /// </summary>
    public float RecommendationScore { get; set; }

    /// <summary>
    /// 推荐理由
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// 预期收益率 (%)
    /// </summary>
    public float? ExpectedReturn { get; set; }

    /// <summary>
    /// 风险等级
    /// </summary>
    public string RiskLevel { get; set; } = string.Empty;

    /// <summary>
    /// 建议持有期限（天）
    /// </summary>
    public int? RecommendedHoldingPeriod { get; set; }

    /// <summary>
    /// 建议仓位比例 (%)
    /// </summary>
    public float? RecommendedPosition { get; set; }

    /// <summary>
    /// 目标价格
    /// </summary>
    public decimal? TargetPrice { get; set; }

    /// <summary>
    /// 止损价格
    /// </summary>
    public decimal? StopLoss { get; set; }

    /// <summary>
    /// 相关新闻（热点新闻推荐时使用）
    /// </summary>
    public List<string> RelatedNews { get; set; } = new();

    /// <summary>
    /// 技术指标摘要
    /// </summary>
    public Dictionary<string, object> TechnicalIndicators { get; set; } = new();

    /// <summary>
    /// 基本面数据摘要
    /// </summary>
    public Dictionary<string, object> FundamentalData { get; set; } = new();
}

