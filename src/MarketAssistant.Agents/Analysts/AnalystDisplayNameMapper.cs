namespace MarketAssistant.Agents.Analysts;

/// <summary>
/// 分析师 Agent 的 ASCII 标识名（见 <see cref="AnalystNames"/>）与中文显示名（如"基本面分析师"）之间的映射。
/// 消息的 <c>AuthorName</c> 是 ASCII 名，聊天侧栏、Markdown 导出等面向用户的展示都需翻译为用户可读的显示名。
/// 新增分析师时需在此登记显示名，漏掉会静默回退为 ASCII 名（由 AnalystRegistrationConsistencyTest 拦截）。
/// </summary>
public static class AnalystDisplayNameMapper
{
    /// <summary>
    /// ASCII Agent Name → 中文显示名（与各分析师 YAML config.name / 类型 DisplayName 特性保持一致）
    /// </summary>
    private static readonly Dictionary<string, string> NameToDisplayName = new(StringComparer.Ordinal)
    {
        [AnalystNames.Fundamental] = "基本面分析师",
        [AnalystNames.Financial] = "财务分析师",
        [AnalystNames.Technical] = "技术分析师",
        [AnalystNames.Sentiment] = "市场情绪分析师",
        [AnalystNames.News] = "新闻事件分析师",
        [AnalystNames.CryptoMetrics] = "项目指标分析师",
        [AnalystNames.Coordinator] = "协调分析师",
        ["MarketAnalysisAssistant"] = "市场分析助手",
    };

    /// <summary>
    /// 将 ASCII Agent Name 翻译为中文显示名；未登记的名称原样返回
    /// </summary>
    public static string GetDisplayName(string? asciiName)
        => !string.IsNullOrEmpty(asciiName) && NameToDisplayName.TryGetValue(asciiName, out var displayName)
            ? displayName
            : asciiName ?? string.Empty;
}
