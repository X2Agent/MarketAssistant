namespace MarketAssistant.Agents;

/// <summary>
/// 快速选股策略枚举
/// </summary>
public enum QuickSelectionStrategy
{
    /// <summary>
    /// 价值股
    /// </summary>
    ValueStocks,

    /// <summary>
    /// 成长股
    /// </summary>
    GrowthStocks,

    /// <summary>
    /// 活跃股
    /// </summary>
    ActiveStocks,

    /// <summary>
    /// 大盘股
    /// </summary>
    LargeCap,

    /// <summary>
    /// 小盘股
    /// </summary>
    SmallCap,

    /// <summary>
    /// 高股息股
    /// </summary>
    Dividend
}

/// <summary>
/// 快速选股策略信息
/// </summary>
public class QuickSelectionStrategyInfo
{
    /// <summary>
    /// 策略类型
    /// </summary>
    public QuickSelectionStrategy Strategy { get; set; }

    /// <summary>
    /// 策略名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 策略描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 适用场景
    /// </summary>
    public string Scenario { get; set; } = string.Empty;

    /// <summary>
    /// 风险等级
    /// </summary>
    public string RiskLevel { get; set; } = string.Empty;
}

/// <summary>
/// 股票推荐请求
/// </summary>
public class StockRecommendationRequest
{
    /// <summary>
    /// 用户需求描述
    /// </summary>
    public string UserRequirements { get; set; } = string.Empty;

    /// <summary>
    /// 投资金额
    /// </summary>
    public decimal? InvestmentAmount { get; set; }

    /// <summary>
    /// 风险偏好：conservative(保守), moderate(稳健), aggressive(激进)
    /// </summary>
    public string RiskPreference { get; set; } = "moderate";

    /// <summary>
    /// 投资期限（天）
    /// </summary>
    public int? InvestmentHorizon { get; set; }

    /// <summary>
    /// 偏好行业
    /// </summary>
    public List<string> PreferredSectors { get; set; } = new();

    /// <summary>
    /// 排除行业
    /// </summary>
    public List<string> ExcludedSectors { get; set; } = new();
}

/// <summary>
/// 热点新闻选股请求
/// </summary>
public class NewsBasedSelectionRequest
{
    /// <summary>
    /// 用户提供的新闻内容
    /// </summary>
    public string NewsContent { get; set; } = string.Empty;

    /// <summary>
    /// 最大推荐股票数量
    /// </summary>
    public int MaxRecommendations { get; set; } = 10;

    /// <summary>
    /// 最低热度评分
    /// </summary>
    public float MinHotspotScore { get; set; } = 60;
}

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

/// <summary>
/// 综合推荐结果
/// </summary>
public class CombinedRecommendationResult
{
    /// <summary>
    /// 基于用户需求的推荐结果
    /// </summary>
    public StockSelectionResult? UserBasedResult { get; set; }

    /// <summary>
    /// 基于新闻热点的推荐结果
    /// </summary>
    public StockSelectionResult? NewsBasedResult { get; set; }

    /// <summary>
    /// 综合分析和建议
    /// </summary>
    public string CombinedAnalysis { get; set; } = string.Empty;

    /// <summary>
    /// 总体置信度
    /// </summary>
    public float OverallConfidence { get; set; }

    /// <summary>
    /// 生成时间
    /// </summary>
    public DateTime GeneratedAt { get; set; } = DateTime.Now;
}

/// <summary>
/// 新闻热点摘要
/// </summary>
public class NewsHotspotSummary
{
    /// <summary>
    /// 热点主题
    /// </summary>
    public string Topic { get; set; } = string.Empty;

    /// <summary>
    /// 热度评分
    /// </summary>
    public float HotspotScore { get; set; }

    /// <summary>
    /// 影响的行业
    /// </summary>
    public List<string> AffectedSectors { get; set; } = new();

    /// <summary>
    /// 摘要描述
    /// </summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// 市场情绪
    /// </summary>
    public string MarketSentiment { get; set; } = "neutral";
}
