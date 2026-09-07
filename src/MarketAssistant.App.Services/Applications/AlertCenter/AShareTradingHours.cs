namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// A 股交易时段判断（东八区，周一至周五 09:30–11:30 / 13:00–15:00）。
/// 用于价格类告警的静默窗口：非交易时段不评估，Critical 级别例外。
/// </summary>
public static class AShareTradingHours
{
    private static readonly TimeSpan MorningOpen = new(9, 30, 0);
    private static readonly TimeSpan MorningClose = new(11, 30, 0);
    private static readonly TimeSpan AfternoonOpen = new(13, 0, 0);
    private static readonly TimeSpan AfternoonClose = new(15, 0, 0);

    private static readonly Lazy<TimeZoneInfo> ChinaTimeZone = new(ResolveChinaTimeZone);

    /// <summary>判断给定 UTC 时间（默认当前）是否处于 A 股交易时段。</summary>
    public static bool IsTradingSession(DateTime? utcNow = null)
    {
        var china = ToChinaTime(utcNow ?? DateTime.UtcNow);

        if (china.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;

        var timeOfDay = china.TimeOfDay;
        return (timeOfDay >= MorningOpen && timeOfDay <= MorningClose) ||
               (timeOfDay >= AfternoonOpen && timeOfDay <= AfternoonClose);
    }

    private static DateTime ToChinaTime(DateTime utc)
    {
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc, ChinaTimeZone.Value);
        }
        catch (Exception)
        {
            // 时区数据异常时按固定 +8 偏移兜底，避免静默窗口整体失效
            return utc.AddHours(8);
        }
    }

    private static TimeZoneInfo ResolveChinaTimeZone()
    {
        // Windows 与 IANA 时区 ID 不同，按运行平台优先取系统标识
        var id = OperatingSystem.IsWindows() ? "China Standard Time" : "Asia/Shanghai";
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception)
        {
            return OperatingSystem.IsWindows()
                ? TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai")
                : TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        }
    }
}