using System.Globalization;

namespace MarketAssistant.Agents.TokenManagement;

/// <summary>
/// Token 用量的展示格式化（紧凑计数 + 运行摘要），供 UI 统一使用。
/// </summary>
public static class TokenUsageFormatter
{
    /// <summary>
    /// 紧凑计数：1234 → "1.2k"，1_234_567 → "1.23M"，小于 1000 时原样输出。
    /// </summary>
    public static string FormatCount(long tokens)
    {
        if (tokens >= 1_000_000)
        {
            return (tokens / 1_000_000.0).ToString("0.##", CultureInfo.InvariantCulture) + "M";
        }

        if (tokens >= 1_000)
        {
            return (tokens / 1_000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        }

        return tokens.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 运行级摘要，如"本次共调用 9 次模型 / 128.4k tokens"。无用量时返回空串。
    /// </summary>
    public static string FormatSummary(AnalysisTokenUsage? usage)
    {
        if (usage is null || !usage.HasValue)
        {
            return string.Empty;
        }

        var summary = $"本次共调用 {usage.CallCount} 次模型 / {FormatCount(usage.TotalTokens)} tokens";
        return usage.IsEstimated ? summary + "（含估算值）" : summary;
    }

    /// <summary>
    /// 运行级明细，如"输入 96.2k · 输出 32.2k"。无用量时返回空串。
    /// </summary>
    public static string FormatDetail(AnalysisTokenUsage? usage)
    {
        if (usage is null || !usage.HasValue)
        {
            return string.Empty;
        }

        return $"输入 {FormatCount(usage.InputTokens)} · 输出 {FormatCount(usage.OutputTokens)}";
    }
}
