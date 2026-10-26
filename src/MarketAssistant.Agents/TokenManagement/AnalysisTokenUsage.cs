using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.TokenManagement;

/// <summary>
/// 单个 Agent 在一次分析运行中的 Token 用量累计。
/// </summary>
public sealed class TokenUsageEntry
{
    public string AgentName { get; set; } = string.Empty;

    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    /// <summary>该 Agent 的模型调用次数（含结构化输出修复重试）。</summary>
    public int CallCount { get; set; }

    /// <summary>是否至少有一次调用为本地估算值（而非提供商返回的精确值）。</summary>
    public bool IsEstimated { get; set; }

    [JsonIgnore]
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// 一次分析运行的 Token 用量汇总：总计 + 按 Agent 的明细。
/// 随 <c>MarketAnalysisReport</c> 一并持久化，供 UI 展示与成本分析。
/// </summary>
public sealed class AnalysisTokenUsage
{
    public long InputTokens { get; set; }

    public long OutputTokens { get; set; }

    /// <summary>本次运行的模型调用总次数。</summary>
    public int CallCount { get; set; }

    /// <summary>是否至少有一次调用为本地估算值（此时总量不精确）。</summary>
    public bool IsEstimated { get; set; }

    public List<TokenUsageEntry> Entries { get; set; } = [];

    [JsonIgnore]
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>是否记录到任何用量（用于区分"未统计"与"统计为零"）。</summary>
    [JsonIgnore]
    public bool HasValue => CallCount > 0 || InputTokens > 0 || OutputTokens > 0;

    /// <summary>
    /// 按 Agent 名称查询用量明细，未记录时返回 <c>null</c>。
    /// </summary>
    public TokenUsageEntry? EntryFor(string? agentName)
    {
        if (string.IsNullOrEmpty(agentName))
        {
            return null;
        }

        foreach (var entry in Entries)
        {
            if (string.Equals(entry.AgentName, agentName, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }
}
