using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 资金费率历史数据
/// </summary>
[Description("加密货币永续合约的资金费率历史趋势")]
public class FundingRateHistory
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    [Description("交易对/代币符号")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前最新资金费率（%）
    /// </summary>
    /// <remarks>
    /// 正值表示多头支付空头（市场看多），负值相反
    /// </remarks>
    [Description("最新资金费率（%），正值表示多头支付空头（看多），负值相反")]
    public decimal CurrentRate { get; set; }

    /// <summary>
    /// 当前费率结算时间（Unix 时间戳毫秒）
    /// </summary>
    [Description("当前费率结算时间戳（ms）")]
    public long CurrentFundingTime { get; set; }

    /// <summary>
    /// 下次费率结算时间（Unix 时间戳毫秒）
    /// </summary>
    [Description("下次费率结算时间戳（ms）")]
    public long NextFundingTime { get; set; }

    /// <summary>
    /// 平均资金费率（%）
    /// </summary>
    [Description("周期平均资金费率（%）")]
    public decimal AverageRate { get; set; }

    /// <summary>
    /// 历史费率数据点（按时间倒序，最新的在前）
    /// </summary>
    [Description("历史资金费率数据明细列表")]
    public List<FundingRatePoint> History { get; set; } = [];
}

/// <summary>
/// 单个资金费率数据点
/// </summary>
[Description("单个历史资金费率点")]
public class FundingRatePoint
{
    /// <summary>
    /// 资金费率（%）
    /// </summary>
    [Description("资金费率百分比（%）")]
    public decimal Rate { get; set; }

    /// <summary>
    /// 费率结算时间（Unix 时间戳）
    /// </summary>
    [Description("结算时间戳（ms）")]
    public long FundingTime { get; set; }
}