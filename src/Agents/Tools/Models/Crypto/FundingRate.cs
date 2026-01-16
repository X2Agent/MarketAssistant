namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 资金费率历史数据
/// </summary>
public class FundingRateHistory
{
    /// <summary>
    /// 交易对符�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前最新资金费率（%�?
    /// </summary>
    /// <remarks>
    /// 正值表示多头支付空头（市场看多），负值相�?
    /// </remarks>
    public decimal CurrentRate { get; set; }

    /// <summary>
    /// 当前费率结算时间（Unix 时间戳）
    /// </summary>
    public long CurrentFundingTime { get; set; }

    /// <summary>
    /// 下次费率结算时间（Unix 时间戳）
    /// </summary>
    public long NextFundingTime { get; set; }

    /// <summary>
    /// 平均资金费率�?�?
    /// </summary>
    public decimal AverageRate { get; set; }

    /// <summary>
    /// 历史费率数据点（按时间倒序，最新的在前�?
    /// </summary>
    public List<FundingRatePoint> History { get; set; } = [];
}

/// <summary>
/// 单个资金费率数据�?
/// </summary>
public class FundingRatePoint
{
    /// <summary>
    /// 资金费率�?�?
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// 费率结算时间（Unix 时间戳）
    /// </summary>
    public long FundingTime { get; set; }
}
