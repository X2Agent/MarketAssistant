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

    /// <summary>生成时间展示文本。</summary>
    public string GeneratedAtText => GeneratedAt.ToLocalTime().ToString("HH:mm");
}
