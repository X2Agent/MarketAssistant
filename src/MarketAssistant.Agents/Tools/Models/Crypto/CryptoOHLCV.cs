using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币OHLCV历史蜡烛图K线数据")]
public class CryptoOHLCV
{
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("K线周期时间间隔（如1m, 1h, 1d等）")]
    public string Interval { get; set; } = string.Empty;

    [Description("K线柱列表（按时间升序）")]
    public List<OHLCVCandle> Candles { get; set; } = [];
}

[Description("单根K线柱柱体明细")]
public class OHLCVCandle
{
    [Description("开盘时间戳（ms）")]
    public long OpenTime { get; set; }

    [Description("收盘时间戳（ms）")]
    public long CloseTime { get; set; }

    [Description("开盘价")]
    public decimal Open { get; set; }

    [Description("最高价")]
    public decimal High { get; set; }

    [Description("最低价")]
    public decimal Low { get; set; }

    [Description("收盘价")]
    public decimal Close { get; set; }

    [Description("基础代币成交量")]
    public decimal Volume { get; set; }

    [Description("计价成交总额（通常为USDT）")]
    public decimal QuoteVolume { get; set; }

    [Description("成交笔数（单数）")]
    public int TradeCount { get; set; }
}
