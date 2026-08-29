using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

public enum Period
{
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

    [Description("12h")]
    TwelveHours,

    [Description("1d")]
    OneDay
}

[Description("加密货币期货合约总持仓量历史明细")]
public class OpenInterest
{
    [Description("交易对符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("当前持仓代币总数")]
    public decimal CurrentOpenInterest { get; set; }

    [Description("当前持仓总价值（USD）")]
    public decimal CurrentOpenInterestValue { get; set; }

    [Description("当前记录时间戳（ms）")]
    public long CurrentTimestamp { get; set; }

    [Description("周期平均持仓数量")]
    public decimal AverageOpenInterest { get; set; }

    [Description("周期平均持仓总价值（USD）")]
    public decimal AverageOpenInterestValue { get; set; }

    [Description("历史持仓量变化明细数据列表")]
    public List<OpenInterestPoint> History { get; set; } = [];
}

[Description("单个合约持仓量点")]
public class OpenInterestPoint
{
    [Description("未平仓合约代币数量")]
    public decimal SumOpenInterest { get; set; }

    [Description("未平仓合约总价值（USD）")]
    public decimal SumOpenInterestValue { get; set; }

    [Description("数据记录时间戳（ms）")]
    public long Timestamp { get; set; }
}
