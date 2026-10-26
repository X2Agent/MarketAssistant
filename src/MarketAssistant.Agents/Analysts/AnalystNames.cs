namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 分析师 Agent 的 ASCII 名称常量，与 config/prompts 下各 YAML 的 name 一一对应。
/// 消息 AuthorName、结构化信号解析、展示层卡片分发均以此为准，避免魔法字符串分散漂移。
/// </summary>
public static class AnalystNames
{
    public const string Fundamental = "FundamentalAnalyst";
    public const string Financial = "FinancialAnalyst";
    public const string Technical = "TechnicalAnalyst";
    public const string Sentiment = "MarketSentimentAnalyst";
    public const string News = "NewsEventAnalyst";
    public const string CryptoMetrics = "CryptoMetricsAnalyst";
    public const string Coordinator = "CoordinatorAnalyst";

    /// <summary>系统说明消息的作者名（产物读取指引、维度缺失说明等）。</summary>
    public const string SystemNotice = "SystemNotice";
}
