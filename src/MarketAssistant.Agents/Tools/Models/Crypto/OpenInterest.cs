using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 时间周期
/// </summary>
public enum Period
{
    /// <summary>
    /// 5分钟
    /// </summary>
    [Description("5m")]
    FiveMinutes,

    /// <summary>
    /// 15分钟
    /// </summary>
    [Description("15m")]
    FifteenMinutes,

    /// <summary>
    /// 30分钟
    /// </summary>
    [Description("30m")]
    ThirtyMinutes,

    /// <summary>
    /// 1小时
    /// </summary>
    [Description("1h")]
    OneHour,

    /// <summary>
    /// 2小时
    /// </summary>
    [Description("2h")]
    TwoHours,

    /// <summary>
    /// 4小时
    /// </summary>
    [Description("4h")]
    FourHours,

    /// <summary>
    /// 6小时
    /// </summary>
    [Description("6h")]
    SixHours,

    /// <summary>
    /// 12小时
    /// </summary>
    [Description("12h")]
    TwelveHours,

    /// <summary>
    /// 1�?
    /// </summary>
    [Description("1d")]
    OneDay
}

/// <summary>
/// 合约持仓量数�?
/// </summary>
public class OpenInterest
{
    /// <summary>
    /// 交易对符�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前持仓总数�?
    /// </summary>
    public decimal CurrentOpenInterest { get; set; }

    /// <summary>
    /// 当前持仓总价值（USD�?
    /// </summary>
    public decimal CurrentOpenInterestValue { get; set; }

    /// <summary>
    /// 当前数据时间�?
    /// </summary>
    public long CurrentTimestamp { get; set; }

    /// <summary>
    /// 平均持仓�?
    /// </summary>
    public decimal AverageOpenInterest { get; set; }

    /// <summary>
    /// 平均持仓价值（USD�?
    /// </summary>
    public decimal AverageOpenInterestValue { get; set; }

    /// <summary>
    /// 历史持仓量数据点（按时间倒序，最新在前）
    /// </summary>
    public List<OpenInterestPoint> History { get; set; } = [];
}

/// <summary>
/// 单个持仓量数据点
/// </summary>
public class OpenInterestPoint
{
    /// <summary>
    /// 持仓总数�?
    /// </summary>
    public decimal SumOpenInterest { get; set; }

    /// <summary>
    /// 持仓总价值（USD�?
    /// </summary>
    public decimal SumOpenInterestValue { get; set; }

    /// <summary>
    /// 数据时间�?
    /// </summary>
    public long Timestamp { get; set; }
}

/// <summary>
/// Period 枚举扩展方法
/// </summary>
public static class PeriodExtensions
{
    /// <summary>
    /// 获取 Period 枚举�?Description 特性�?
    /// </summary>
    public static string GetDescription(this Period period)
    {
        var field = period.GetType().GetField(period.ToString());
        if (field == null) return "1h";

        var attribute = (DescriptionAttribute?)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
        return attribute?.Description ?? "1h";
    }
}
