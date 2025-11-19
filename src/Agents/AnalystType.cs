namespace MarketAssistant.Agents;

/// <summary>
/// 分析师类型枚举
/// 用于工厂创建对应的分析师代理
/// </summary>
public enum AnalystType
{
    /// <summary>
    /// 基本面分析师
    /// </summary>
    FundamentalAnalyst,

    /// <summary>
    /// 市场情绪分析师
    /// </summary>
    MarketSentimentAnalyst,

    /// <summary>
    /// 财务分析师
    /// </summary>
    FinancialAnalyst,

    /// <summary>
    /// 技术分析师
    /// </summary>
    TechnicalAnalyst,

    /// <summary>
    /// 新闻事件分析师
    /// </summary>
    NewsEventAnalyst,

    /// <summary>
    /// 协调分析师
    /// </summary>
    CoordinatorAnalyst
}


