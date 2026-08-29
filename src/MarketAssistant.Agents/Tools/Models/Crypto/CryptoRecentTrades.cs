using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币最近实时成交记录及买卖力量统计")]
public class CryptoRecentTrades
{
    [Description("代币符号/交易对")]
    public string Symbol { get; set; } = string.Empty;

    [Description("最近成交记录明细列表")]
    public List<CryptoTrade> Trades { get; set; } = [];

    [Description("买方主动买入占比（Taker买入，%）")]
    public decimal BuyerVolumePercent { get; set; }

    [Description("卖方主动卖出占比（Taker卖出，%）")]
    public decimal SellerVolumePercent { get; set; }

    [Description("总成交量（代币数量）")]
    public decimal TotalVolume => Trades.Sum(t => t.Quantity);

    [Description("总成交额（USDT价值）")]
    public decimal TotalQuoteVolume => Trades.Sum(t => t.QuoteQuantity);

    [Description("成交均价")]
    public decimal AveragePrice => TotalVolume > 0 ? TotalQuoteVolume / TotalVolume : 0;
}

[Description("单笔成交明细")]
public class CryptoTrade
{
    [Description("成交ID")]
    public long TradeId { get; set; }

    [Description("成交价格")]
    public decimal Price { get; set; }

    [Description("成交数量（代币）")]
    public decimal Quantity { get; set; }

    [Description("成交额（计价USDT）")]
    public decimal QuoteQuantity { get; set; }

    [Description("成交时间戳（毫秒ms）")]
    public long Timestamp { get; set; }

    [Description("是否为买方主动成交（true表示主动买入吃单）")]
    public bool IsBuyerMaker { get; set; }
}
