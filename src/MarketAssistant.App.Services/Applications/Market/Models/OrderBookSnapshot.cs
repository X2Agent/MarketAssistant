namespace MarketAssistant.Applications.Market.Models;

/// <summary>
/// 盘口单档挂单。
/// </summary>
public sealed record OrderBookLevel(decimal Price, decimal Quantity)
{
    /// <summary>挂单金额（价格 × 数量）。</summary>
    public decimal Amount => Price * Quantity;
}

/// <summary>
/// 盘口深度快照（市场无关）。买盘按价格降序、卖盘按价格升序。
/// </summary>
/// <param name="LastUpdateId">数据源快照序号，缺失时为 0。</param>
public sealed record OrderBookSnapshot(
    IReadOnlyList<OrderBookLevel> Bids,
    IReadOnlyList<OrderBookLevel> Asks,
    long LastUpdateId = 0)
{
    public decimal? BestBid => Bids.Count > 0 ? Bids[0].Price : null;

    public decimal? BestAsk => Asks.Count > 0 ? Asks[0].Price : null;

    /// <summary>买卖价差。</summary>
    public decimal? Spread => BestBid.HasValue && BestAsk.HasValue ? BestAsk.Value - BestBid.Value : null;

    /// <summary>价差占买一价百分比。</summary>
    public decimal? SpreadPercent =>
        Spread.HasValue && BestBid is > 0 ? Spread.Value / BestBid.Value * 100 : null;

    public decimal TotalBidQuantity => Bids.Sum(level => level.Quantity);

    public decimal TotalAskQuantity => Asks.Sum(level => level.Quantity);

    /// <summary>买盘占比（%），用于深度概览条。无挂单时为 null。</summary>
    public decimal? BidQuantityPercent
    {
        get
        {
            var total = TotalBidQuantity + TotalAskQuantity;
            return total > 0 ? TotalBidQuantity / total * 100 : null;
        }
    }
}
