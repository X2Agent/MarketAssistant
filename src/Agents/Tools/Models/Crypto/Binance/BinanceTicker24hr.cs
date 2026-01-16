namespace MarketAssistant.Agents.Tools.Models.Crypto.Binance;

/// <summary>
/// 币安 24 小时行情数据模型
/// </summary>
public class BinanceTicker24hr
{
    public string Symbol { get; set; } = string.Empty;
    public decimal PriceChange { get; set; }
    public decimal PriceChangePercent { get; set; }
    public decimal WeightedAvgPrice { get; set; }
    public decimal PrevClosePrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal BidPrice { get; set; }
    public decimal AskPrice { get; set; }
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal Volume { get; set; }
    public decimal QuoteVolume { get; set; }
    public long OpenTime { get; set; }
    public long CloseTime { get; set; }
    public int Count { get; set; }
}
