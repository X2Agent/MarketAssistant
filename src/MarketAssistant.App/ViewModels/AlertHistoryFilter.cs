using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 告警历史筛选纯逻辑：按级别、市场与标的关键词过滤，无状态无 IO，可独立单测。
/// </summary>
public static class AlertHistoryFilter
{
    /// <summary>
    /// 判断告警是否匹配筛选条件。
    /// </summary>
    /// <param name="alert">待判断的告警。</param>
    /// <param name="level">级别过滤；null 表示不限级别。</param>
    /// <param name="keyword">标的关键词，匹配标的代码或告警标题（忽略大小写）；空表示不限。</param>
    /// <param name="marketType">
    /// 市场过滤；null 表示不限市场。双市场告警混杂时按市场定位，
    /// 避免用户在 200 条记录里翻找另一市场的告警。
    /// </param>
    public static bool Matches(AlertEvent alert, AlertLevel? level, string? keyword, MarketType? marketType = null)
    {
        ArgumentNullException.ThrowIfNull(alert);

        if (level is { } requiredLevel && alert.Level != requiredLevel)
            return false;

        if (marketType is { } requiredMarket && alert.MarketType != requiredMarket)
            return false;

        if (string.IsNullOrWhiteSpace(keyword))
            return true;

        return alert.Symbol.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
               alert.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// 市场筛选项。<see cref="MarketType"/> 为 null 表示全部市场。
/// </summary>
/// <param name="Text">下拉展示文案。</param>
/// <param name="MarketType">对应市场；null 表示不限。</param>
public sealed record MarketFilterOption(string Text, MarketType? MarketType);
