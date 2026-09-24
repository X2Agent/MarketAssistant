using MarketAssistant.Applications.AlertCenter;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 告警历史筛选纯逻辑：按级别与标的关键词过滤，无状态无 IO，可独立单测。
/// </summary>
public static class AlertHistoryFilter
{
    /// <summary>
    /// 判断告警是否匹配筛选条件。
    /// </summary>
    /// <param name="alert">待判断的告警。</param>
    /// <param name="level">级别过滤；null 表示不限级别。</param>
    /// <param name="keyword">标的关键词，匹配标的代码或告警标题（忽略大小写）；空表示不限。</param>
    public static bool Matches(AlertEvent alert, AlertLevel? level, string? keyword)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (level is { } requiredLevel && alert.Level != requiredLevel)
            return false;

        if (string.IsNullOrWhiteSpace(keyword))
            return true;

        return alert.Symbol.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               alert.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}
