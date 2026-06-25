using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 时间周期
/// </summary>
public enum Period
{
    /// <summary>5分钟</summary>
    [Description("5m")]
    FiveMinutes,

    /// <summary>15分钟</summary>
    [Description("15m")]
    FifteenMinutes,

    /// <summary>30分钟</summary>
    [Description("30m")]
    ThirtyMinutes,

    /// <summary>1小时</summary>
    [Description("1h")]
    OneHour,

    /// <summary>2小时</summary>
    [Description("2h")]
    TwoHours,

    /// <summary>4小时</summary>
    [Description("4h")]
    FourHours,

    /// <summary>6小时</summary>
    [Description("6h")]
    SixHours,

    /// <summary>12小时</summary>
    [Description("12h")]
    TwelveHours,

    /// <summary>1天</summary>
    [Description("1d")]
    OneDay
}

/// <summary>
/// 合约持仓量数据
/// </summary>
[Description("加密货币期货合约总持仓量历史明细")]
public class OpenInterest
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    [Description("交易对符号")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前持仓总数量
    /// </summary>
    [Description("当前持仓代币总数")]
    public decimal CurrentOpenInterest { get; set; }

    /// <summary>
    /// 当前持仓总价值（USD）
    /// </summary>
    [Description("当前持仓总价值（USD）")]
    public decimal CurrentOpenInterestValue { get; set; }

    /// <summary>
    /// 当前数据时间戳
    /// </summary>
    [Description("当前记录时间戳（ms）")]
    public long CurrentTimestamp { get; set; }

    /// <summary>
    /// 平均持仓量
    /// </summary>
    [Description("周期平均持仓数量")]
    public decimal AverageOpenInterest { get; set; }

    /// <summary>
    /// 平均持仓价值（USD）
    /// </summary>
    [Description("周期平均持仓总价值（USD）")]
    public decimal AverageOpenInterestValue { get; set; }

    /// <summary>
    /// 历史持仓量数据点（按时间倒序，最新在前）
    /// </summary>
    [Description("历史持仓量变化明细数据列表")]
    public List<OpenInterestPoint> History { get; set; } = [];
}

/// <summary>
/// 单个持仓量数据点
/// </summary>
[Description("单个合约持仓量点")]
public class OpenInterestPoint
{
    /// <summary>
    /// 持仓总数量
    /// </summary>
    [Description("未平仓合约代币数量")]
    public decimal SumOpenInterest { get; set; }

    /// <summary>
    /// 持仓总价值（USD）
    /// </summary>
    [Description("未平仓合约总价值（USD）")]
    public decimal SumOpenInterestValue { get; set; }

    /// <summary>
    /// 数据时间戳
    /// </summary>
    [Description("数据记录时间戳（ms）")]
    public long Timestamp { get; set; }
}

/// <summary>
/// Period 枚举扩展方法
/// </summary>
public static class PeriodExtensions
{
    /// <summary>
    /// 获取 Period 枚举的 Description 特性值
    /// </summary>
    public static string GetDescription(this Period period)
    {
        var field = period.GetType().GetField(period.ToString());
        if (field == null) return "1h";

        var attribute = (DescriptionAttribute?)Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute));
        return attribute?.Description ?? "1h";
    }
}