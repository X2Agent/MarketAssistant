using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币市场深度订单簿快照")]
public class CryptoOrderBookDepth
{
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("最新更新ID")]
    public long LastUpdateId { get; set; }

    [Description("买单列表（Bids，按价格降序）")]
    public List<OrderBookLevel> Bids { get; set; } = [];

    [Description("卖单列表（Asks，按价格升序）")]
    public List<OrderBookLevel> Asks { get; set; } = [];

    [Description("最优买入价（盘口买一）")]
    public decimal BestBidPrice => Bids.FirstOrDefault()?.Price ?? 0;

    [Description("最优卖出价（盘口卖一）")]
    public decimal BestAskPrice => Asks.FirstOrDefault()?.Price ?? 0;

    [Description("买卖价差（点差）")]
    public decimal Spread => BestAskPrice - BestBidPrice;

    [Description("价差百分比（%）")]
    public decimal SpreadPercent => BestBidPrice > 0 ? (Spread / BestBidPrice * 100) : 0;

    [Description("买盘累计委托量")]
    public decimal TotalBidVolume => Bids.Sum(b => b.Quantity);

    [Description("卖盘累计委托量")]
    public decimal TotalAskVolume => Asks.Sum(a => a.Quantity);
}

[Description("盘口单档深度详情")]
public class OrderBookLevel
{
    [Description("档位委托价格")]
    public decimal Price { get; set; }

    [Description("档位委托数量")]
    public decimal Quantity { get; set; }

    [Description("该档位累计委托价值")]
    public decimal Value => Price * Quantity;
}
