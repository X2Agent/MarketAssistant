namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 虚拟币市场情绪综合数�?
/// </summary>
public class CryptoSentiment
{
    /// <summary>
    /// 代币符号
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 恐慌贪婪指数�?-100�?=极度恐慌�?00=极度贪婪�?
    /// </summary>
    public int? FearGreedIndex { get; set; }

    /// <summary>
    /// 恐慌贪婪指数分类描述
    /// </summary>
    public string? FearGreedClassification { get; set; }

    /// <summary>
    /// 资金费率�?�?
    /// </summary>
    /// <remarks>
    /// 正值表示多头支付空头（市场看多），负值相�?
    /// </remarks>
    public decimal? FundingRate { get; set; }

    /// <summary>
    /// 多空持仓人数�?
    /// </summary>
    public decimal? LongShortRatio { get; set; }

    /// <summary>
    /// 大户多空持仓�?
    /// </summary>
    public decimal? TopTraderLongShortRatio { get; set; }

    /// <summary>
    /// 合约持仓量（USD�?
    /// </summary>
    public decimal? OpenInterest { get; set; }

    /// <summary>
    /// 24小时爆仓量（USD�?
    /// </summary>
    public decimal? Liquidation24h { get; set; }

    /// <summary>
    /// 多头爆仓量（USD�?
    /// </summary>
    public decimal? LongLiquidation { get; set; }

    /// <summary>
    /// 空头爆仓量（USD�?
    /// </summary>
    public decimal? ShortLiquidation { get; set; }

    /// <summary>
    /// 数据更新时间
    /// </summary>
    public string UpdatedAt { get; set; } = string.Empty;
}
