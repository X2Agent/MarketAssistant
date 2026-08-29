using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币永续合约的多空持仓比率历史趋势")]
public class LongShortRatioHistory
{
    [Description("交易对符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("当前最新多头仓位账户占比（%）")]
    public decimal CurrentLongRatio { get; set; }

    [Description("当前最新空头仓位账户占比（%）")]
    public decimal CurrentShortRatio { get; set; }

    [Description("当前多空人数或持仓比率")]
    public decimal CurrentRatio { get; set; }

    [Description("周期平均多空比率")]
    public decimal AverageRatio { get; set; }

    [Description("历史多空比变化明细数据列表")]
    public List<LongShortRatioPoint> History { get; set; } = [];
}

[Description("单笔历史多空比数据点")]
public class LongShortRatioPoint
{
    [Description("多头占比（%）")]
    public decimal LongRatio { get; set; }

    [Description("空头占比（%）")]
    public decimal ShortRatio { get; set; }

    [Description("多空比率")]
    public decimal Ratio { get; set; }

    [Description("数据记录时间戳（ms）")]
    public long Timestamp { get; set; }
}
