namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// OHLCV K线数据（开盘、最高、最低、收盘、成交量�?
/// </summary>
public class CryptoOHLCV
{
    /// <summary>
    /// 交易对符�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 时间间隔（如 1m�?m�?h�?d�?
    /// </summary>
    public string Interval { get; set; } = string.Empty;

    /// <summary>
    /// K线数据点列表（按时间升序�?
    /// </summary>
    public List<OHLCVCandle> Candles { get; set; } = [];
}

/// <summary>
/// 单根K线数�?
/// </summary>
public class OHLCVCandle
{
    /// <summary>
    /// 开盘时间（Unix时间戳毫秒）
    /// </summary>
    public long OpenTime { get; set; }

    /// <summary>
    /// 收盘时间（Unix时间戳毫秒）
    /// </summary>
    public long CloseTime { get; set; }

    /// <summary>
    /// 开盘价
    /// </summary>
    public decimal Open { get; set; }

    /// <summary>
    /// 最高价
    /// </summary>
    public decimal High { get; set; }

    /// <summary>
    /// 最低价
    /// </summary>
    public decimal Low { get; set; }

    /// <summary>
    /// 收盘�?
    /// </summary>
    public decimal Close { get; set; }

    /// <summary>
    /// 成交量（基础货币�?
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// 成交额（计价货币�?
    /// </summary>
    public decimal QuoteVolume { get; set; }

    /// <summary>
    /// 成交笔数
    /// </summary>
    public int TradeCount { get; set; }
}
