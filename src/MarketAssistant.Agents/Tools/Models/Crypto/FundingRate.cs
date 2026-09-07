using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币永续合约的资金费率历史趋势")]
public class FundingRateHistory
{
    [Description("交易对/代币符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("最新资金费率（%），正值表示多头支付空头（看多），负值相反")]
    public decimal CurrentRate { get; set; }

    [Description("当前费率结算时间戳（ms）")]
    public long CurrentFundingTime { get; set; }

    [Description("下次费率结算时间戳（ms）")]
    public long NextFundingTime { get; set; }

    [Description("周期平均资金费率（%）")]
    public decimal AverageRate { get; set; }

    [Description("历史资金费率数据明细列表")]
    public List<FundingRatePoint> History { get; set; } = [];
}

[Description("单个历史资金费率点")]
public class FundingRatePoint
{
    [Description("资金费率百分比（%）")]
    public decimal Rate { get; set; }

    [Description("结算时间戳（ms）")]
    public long FundingTime { get; set; }
}
