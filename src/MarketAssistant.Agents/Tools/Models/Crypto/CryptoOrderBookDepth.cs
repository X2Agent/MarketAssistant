using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 订单簿深度数据
/// </summary>
[Description("加密货币市场深度订单簿快照")]
public class CryptoOrderBookDepth
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 最新更新ID
    /// </summary>
    [Description("最新更新ID")]
    public long LastUpdateId { get; set; }

    /// <summary>
    /// 买盘深度（按价格降序，最高买价在前）
    /// </summary>
    [Description("买单列表（Bids，按价格降序）")]
    public List<OrderBookLevel> Bids { get; set; } = [];

    /// <summary>
    /// 卖盘深度（按价格升序，最低卖价在前）
    /// </summary>
    [Description("卖单列表（Asks，按价格升序）")]
    public List<OrderBookLevel> Asks { get; set; } = [];

    /// <summary>
    /// 最优买价（最高买入价）
    /// </summary>
    [Description("最优买入价（盘口买一）")]
    public decimal BestBidPrice => Bids.FirstOrDefault()?.Price ?? 0;

    /// <summary>
    /// 最优卖价（最低卖出价）
    /// </summary>
    [Description("最优卖出价（盘口卖一）")]
    public decimal BestAskPrice => Asks.FirstOrDefault()?.Price ?? 0;

    /// <summary>
    /// 买卖价差（spread）
    /// </summary>
    [Description("买卖价差（点差）")]
    public decimal Spread => BestAskPrice - BestBidPrice;

    /// <summary>
    /// 价差百分比
    /// </summary>
    [Description("价差百分比（%）")]
    public decimal SpreadPercent => BestBidPrice > 0 ? (Spread / BestBidPrice * 100) : 0;

    /// <summary>
    /// 买盘总量（前N档）
    /// </summary>
    [Description("买盘累计委托量")]
    public decimal TotalBidVolume => Bids.Sum(b => b.Quantity);

    /// <summary>
    /// 卖盘总量（前N档）
    /// </summary>
    [Description("卖盘累计委托量")]
    public decimal TotalAskVolume => Asks.Sum(a => a.Quantity);
}

/// <summary>
/// 订单簿价格档位
/// </summary>
[Description("盘口单档深度详情")]
public class OrderBookLevel
{
    /// <summary>
    /// 价格
    /// </summary>
    [Description("档位委托价格")]
    public decimal Price { get; set; }

    /// <summary>
    /// 数量
    /// </summary>
    [Description("档位委托数量")]
    public decimal Quantity { get; set; }

    /// <summary>
    /// 总价值（价格 * 数量）
    /// </summary>
    [Description("该档位累计委托价值")]
    public decimal Value => Price * Quantity;
}