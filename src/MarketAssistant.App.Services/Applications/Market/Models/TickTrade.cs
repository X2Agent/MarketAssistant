namespace MarketAssistant.Applications.Market.Models;

/// <summary>
/// 逐笔成交明细（市场无关）。
/// </summary>
/// <param name="Price">成交价。</param>
/// <param name="Quantity">成交量。</param>
/// <param name="Timestamp">成交时间。</param>
/// <param name="IsBuyerMaker">买方是否为挂单方（true 表示主动卖出，false 表示主动买入）。</param>
/// <param name="TradeId">数据源成交编号，缺失时为 0。</param>
public sealed record TickTrade(
    decimal Price,
    decimal Quantity,
    DateTime Timestamp,
    bool IsBuyerMaker,
    long TradeId = 0)
{
    /// <summary>成交额。</summary>
    public decimal Amount => Price * Quantity;

    /// <summary>主动方向：主动买入 / 主动卖出。</summary>
    public bool IsActiveBuy => !IsBuyerMaker;
}
