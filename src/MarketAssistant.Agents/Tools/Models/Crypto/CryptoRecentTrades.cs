using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 最近成交数据
/// </summary>
[Description("加密货币最近实时成交记录及买卖力量统计")]
public class CryptoRecentTrades
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    [Description("代币符号/交易对")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 最近成交列表（按时间倒序，最新在前）
    /// </summary>
    [Description("最近成交记录明细列表")]
    public List<CryptoTrade> Trades { get; set; } = [];

    /// <summary>
    /// 买方主导成交量占比（%）
    /// </summary>
    [Description("买方主动买入占比（Taker买入，%）")]
    public decimal BuyerVolumePercent { get; set; }

    /// <summary>
    /// 卖方主导成交量占比（%）
    /// </summary>
    [Description("卖方主动卖出占比（Taker卖出，%）")]
    public decimal SellerVolumePercent { get; set; }

    /// <summary>
    /// 总成交量
    /// </summary>
    [Description("总成交量（代币数量）")]
    public decimal TotalVolume => Trades.Sum(t => t.Quantity);

    /// <summary>
    /// 总成交额
    /// </summary>
    [Description("总成交额（USDT价值）")]
    public decimal TotalQuoteVolume => Trades.Sum(t => t.QuoteQuantity);

    /// <summary>
    /// 平均成交价
    /// </summary>
    [Description("成交均价")]
    public decimal AveragePrice => TotalVolume > 0 ? TotalQuoteVolume / TotalVolume : 0;
}

/// <summary>
/// 单笔成交数据
/// </summary>
[Description("单笔成交明细")]
public class CryptoTrade
{
    /// <summary>
    /// 成交ID
    /// </summary>
    [Description("成交ID")]
    public long TradeId { get; set; }

    /// <summary>
    /// 成交价格
    /// </summary>
    [Description("成交价格")]
    public decimal Price { get; set; }

    /// <summary>
    /// 成交数量（基础货币）
    /// </summary>
    [Description("成交数量（代币）")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// 成交额（计价货币）
    /// </summary>
    [Description("成交额（计价USDT）")]
    public decimal QuoteQuantity { get; set; }

    /// <summary>
    /// 成交时间（Unix时间戳毫秒）
    /// </summary>
    [Description("成交时间戳（毫秒ms）")]
    public long Timestamp { get; set; }

    /// <summary>
    /// 是否为买方主动成交（Taker买入）
    /// </summary>
    [Description("是否为买方主动成交（true表示主动买入吃单）")]
    public bool IsBuyerMaker { get; set; }
}