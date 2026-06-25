using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 虚拟币市场情绪综合数据
/// </summary>
[Description("加密货币市场情绪及持仓综合数据")]
public class CryptoSentiment
{
    /// <summary>
    /// 代币符号
    /// </summary>
    [Description("代币符号（如 BTC）")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 恐惧贪婪指数（0-100，0=极度恐惧，100=极度贪婪）
    /// </summary>
    /// <remarks>
    /// 当前无数据来源：需要集成 alternative.me Fear &amp; Greed Index API
    /// </remarks>
    [Description("恐惧贪婪指数（0-100，0=极度恐惧，100=极度贪婪）")]
    public int? FearGreedIndex { get; set; }

    /// <summary>
    /// 恐惧贪婪指数分类描述
    /// </summary>
    /// <remarks>
    /// 当前无数据来源：需要集成 alternative.me Fear &amp; Greed Index API
    /// </remarks>
    [Description("恐惧贪婪指数分类描述")]
    public string? FearGreedClassification { get; set; }

    /// <summary>
    /// 资金费率（%）
    /// </summary>
    /// <remarks>
    /// 正值表示多头支付空头（市场看多），负值相反。
    /// 数据来源：币安 premiumIndex API
    /// </remarks>
    [Description("永续合约资金费率（%），正值代表看多，负值看空")]
    public decimal? FundingRate { get; set; }

    /// <summary>
    /// 多空持仓人数比
    /// </summary>
    [Description("多空持仓人数比率")]
    public decimal? LongShortRatio { get; set; }

    /// <summary>
    /// 大户多空持仓比
    /// </summary>
    [Description("大户/精英多空持仓比率")]
    public decimal? TopTraderLongShortRatio { get; set; }

    /// <summary>
    /// 合约持仓量（USD）
    /// </summary>
    [Description("合约持仓量（USD价值）")]
    public decimal? OpenInterest { get; set; }

    /// <summary>
    /// 24小时爆仓金额（USD）
    /// </summary>
    /// <remarks>
    /// 当前无数据来源：需要集成 CoinGlass 或类似爆仓数据 API
    /// </remarks>
    [Description("24小时爆仓总额（USD）")]
    public decimal? Liquidation24h { get; set; }

    /// <summary>
    /// 多头爆仓金额（USD）
    /// </summary>
    /// <remarks>
    /// 当前无数据来源：需要集成 CoinGlass 或类似爆仓数据 API
    /// </remarks>
    [Description("多头爆仓总额（USD）")]
    public decimal? LongLiquidation { get; set; }

    /// <summary>
    /// 空头爆仓金额（USD）
    /// </summary>
    /// <remarks>
    /// 当前无数据来源：需要集成 CoinGlass 或类似爆仓数据 API
    /// </remarks>
    [Description("空头爆仓总额（USD）")]
    public decimal? ShortLiquidation { get; set; }

    /// <summary>
    /// 数据更新时间
    /// </summary>
    [Description("数据更新时间")]
    public string UpdatedAt { get; set; } = string.Empty;
}