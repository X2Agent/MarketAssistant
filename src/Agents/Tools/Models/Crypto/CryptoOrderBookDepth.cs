namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 订单簿深度数�?
/// </summary>
public class CryptoOrderBookDepth
{
    /// <summary>
    /// 交易对符�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 最新更新ID
    /// </summary>
    public long LastUpdateId { get; set; }

    /// <summary>
    /// 买盘深度（按价格降序，最高买价在前）
    /// </summary>
    public List<OrderBookLevel> Bids { get; set; } = [];

    /// <summary>
    /// 卖盘深度（按价格升序，最低卖价在前）
    /// </summary>
    public List<OrderBookLevel> Asks { get; set; } = [];

    /// <summary>
    /// 最优买价（最高买入价�?
    /// </summary>
    public decimal BestBidPrice => Bids.FirstOrDefault()?.Price ?? 0;

    /// <summary>
    /// 最优卖价（最低卖出价�?
    /// </summary>
    public decimal BestAskPrice => Asks.FirstOrDefault()?.Price ?? 0;

    /// <summary>
    /// 买卖价差（spread�?
    /// </summary>
    public decimal Spread => BestAskPrice - BestBidPrice;

    /// <summary>
    /// 价差百分�?
    /// </summary>
    public decimal SpreadPercent => BestBidPrice > 0 ? (Spread / BestBidPrice * 100) : 0;

    /// <summary>
    /// 买盘总量（前N档）
    /// </summary>
    public decimal TotalBidVolume => Bids.Sum(b => b.Quantity);

    /// <summary>
    /// 卖盘总量（前N档）
    /// </summary>
    public decimal TotalAskVolume => Asks.Sum(a => a.Quantity);
}

/// <summary>
/// 订单簿价格档�?
/// </summary>
public class OrderBookLevel
{
    /// <summary>
    /// 价格
    /// </summary>
    public decimal Price { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// 总价值（价格 × 数量�?
    /// </summary>
    public decimal Value => Price * Quantity;
}
