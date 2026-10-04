using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.Briefing;

/// <summary>
/// 每日 AI 简报。按市场 + 本地日期唯一，当天生成后复用，重启不重复消耗 Token。
/// </summary>
public sealed class DailyBriefing
{
    public MarketType MarketType { get; init; }

    /// <summary>本地日期键（yyyy-MM-dd）。</summary>
    public string DateKey { get; init; } = string.Empty;

    /// <summary>简报正文（Markdown 文本）。</summary>
    public string Content { get; init; } = string.Empty;

    public DateTime GeneratedAt { get; init; }

    /// <summary>生成时纳入的自选数量（0 表示仅基于要闻生成）。</summary>
    public int FavoriteCount { get; init; }

    /// <summary>生成所用模型 ID，便于成本追溯。</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>本次生成的输入 Token 数；<see cref="Estimated"/> 为 true 时为本地估算值。</summary>
    public long InputTokens { get; init; }

    /// <summary>本次生成的输出 Token 数。</summary>
    public long OutputTokens { get; init; }

    /// <summary>Token 计数是否为本地估算值（提供商未返回精确用量）。</summary>
    public bool Estimated { get; init; }

    /// <summary>是否记录到任何 Token 用量（区分"未统计"与"统计为零"）。</summary>
    public bool HasTokenUsage => InputTokens > 0 || OutputTokens > 0;

    /// <summary>生成时间展示文本。</summary>
    public string GeneratedAtText => GeneratedAt.ToLocalTime().ToString("HH:mm");
}
