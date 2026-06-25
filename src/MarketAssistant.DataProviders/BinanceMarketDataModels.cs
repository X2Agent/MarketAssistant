namespace MarketAssistant.Services.Data;

/// <summary>
/// 币安24小时价格变动统计数据模型（用于 BinanceMarketDataService）
/// 注意：这是专门为 /api/v3/ticker/24hr 接口定义的模型，字段为 decimal 类型
/// </summary>
public class Binance24hrTicker
{
    public string Symbol { get; set; } = string.Empty;
    public decimal OpenPrice { get; set; }
    public decimal HighPrice { get; set; }
    public decimal LowPrice { get; set; }
    public decimal LastPrice { get; set; }
    public decimal Volume { get; set; }
    public decimal QuoteVolume { get; set; }
    public long OpenTime { get; set; }
    public long CloseTime { get; set; }
    public long FirstId { get; set; }
    public long LastId { get; set; }
    public long Count { get; set; }

    // FULL类型才有的字段（MINI类型不包含）
    public decimal? PriceChange { get; set; }
    public decimal? PriceChangePercent { get; set; }
    public decimal? WeightedAvgPrice { get; set; }
    public decimal? PrevClosePrice { get; set; }
    public decimal? LastQty { get; set; }
    public decimal? BidPrice { get; set; }
    public decimal? BidQty { get; set; }
    public decimal? AskPrice { get; set; }
    public decimal? AskQty { get; set; }
}

/// <summary>
/// 币安交易所信息响应
/// </summary>
public class BinanceExchangeInfo
{
    public List<BinanceSymbolInfo>? Symbols { get; set; }
}

/// <summary>
/// 币安交易对信息
/// </summary>
public class BinanceSymbolInfo
{
    public string Symbol { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string BaseAsset { get; set; } = string.Empty;
    public string QuoteAsset { get; set; } = string.Empty;
}
