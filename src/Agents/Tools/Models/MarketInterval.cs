using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models;

/// <summary>
/// 市场行情 K 线时间间隔
/// </summary>
public enum MarketInterval
{
    [Description("1s")]
    OneSecond,
    [Description("1m")]
    OneMinute,
    [Description("3m")]
    ThreeMinutes,
    [Description("5m")]
    FiveMinutes,
    [Description("15m")]
    FifteenMinutes,
    [Description("30m")]
    ThirtyMinutes,
    [Description("1h")]
    OneHour,
    [Description("2h")]
    TwoHours,
    [Description("4h")]
    FourHours,
    [Description("6h")]
    SixHours,
    [Description("8h")]
    EightHours,
    [Description("12h")]
    TwelveHours,
    [Description("1d")]
    OneDay,
    [Description("3d")]
    ThreeDays,
    [Description("1w")]
    OneWeek,
    [Description("1M")]
    OneMonth
}
