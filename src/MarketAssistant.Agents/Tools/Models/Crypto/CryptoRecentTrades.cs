namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 最近成交数�?
/// </summary>
public class CryptoRecentTrades
{
    /// <summary>
    /// 交易对符�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 最近成交列表（按时间倒序，最新在前）
    /// </summary>
    public List<CryptoTrade> Trades { get; set; } = [];

    /// <summary>
    /// 买方主导成交量占比（%�?
    /// </summary>
    public decimal BuyerVolumePercent { get; set; }

    /// <summary>
    /// 卖方主导成交量占比（%�?
    /// </summary>
    public decimal SellerVolumePercent { get; set; }

    /// <summary>
    /// 总成交量
    /// </summary>
    public decimal TotalVolume => Trades.Sum(t => t.Quantity);

    /// <summary>
    /// 总成交额
    /// </summary>
    public decimal TotalQuoteVolume => Trades.Sum(t => t.QuoteQuantity);

    /// <summary>
    /// 平均成交�?
    /// </summary>
    public decimal AveragePrice => TotalVolume > 0 ? TotalQuoteVolume / TotalVolume : 0;
}

/// <summary>
/// 单笔成交数据
/// </summary>
public class CryptoTrade
{
    /// <summary>
    /// 成交ID
    /// </summary>
    public long TradeId { get; set; }

    /// <summary>
    /// 成交价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 成交数量（基础货币�?
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// 成交额（计价货币�?
    /// </summary>
    public decimal QuoteQuantity { get; set; }

    /// <summary>
    /// 成交时间（Unix时间戳毫秒）
    /// </summary>
    public long Timestamp { get; set; }

    /// <summary>
    /// 是否为买方主动成交（Taker买入�?
    /// </summary>
    public bool IsBuyerMaker { get; set; }
}
