using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// OHLCV K线数据（开盘、最高、最低、收盘、成交量）
/// </summary>
[Description("加密货币OHLCV历史蜡烛图K线数据")]
public class CryptoOHLCV
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 时间间隔（如 1m, 5m, 1h, 1d）
    /// </summary>
    [Description("K线周期时间间隔（如1m, 1h, 1d等）")]
    public string Interval { get; set; } = string.Empty;

    /// <summary>
    /// K线数据点列表（按时间升序）
    /// </summary>
    [Description("K线柱列表（按时间升序）")]
    public List<OHLCVCandle> Candles { get; set; } = [];
}

/// <summary>
/// 单根K线数据
/// </summary>
[Description("单根K线柱柱体明细")]
public class OHLCVCandle
{
    /// <summary>
    /// 开盘时间（Unix时间戳毫秒）
    /// </summary>
    [Description("开盘时间戳（ms）")]
    public long OpenTime { get; set; }

    /// <summary>
    /// 收盘时间（Unix时间戳毫秒）
    /// </summary>
    [Description("收盘时间戳（ms）")]
    public long CloseTime { get; set; }

    /// <summary>
    /// 开盘价
    /// </summary>
    [Description("开盘价")]
    public decimal Open { get; set; }

    /// <summary>
    /// 最高价
    /// </summary>
    [Description("最高价")]
    public decimal High { get; set; }

    /// <summary>
    /// 最低价
    /// </summary>
    [Description("最低价")]
    public decimal Low { get; set; }

    /// <summary>
    /// 收盘价
    /// </summary>
    [Description("收盘价")]
    public decimal Close { get; set; }

    /// <summary>
    /// 成交量（基础货币）
    /// </summary>
    [Description("基础代币成交量")]
    public decimal Volume { get; set; }

    /// <summary>
    /// 成交额（计价货币）
    /// </summary>
    [Description("计价成交总额（通常为USDT）")]
    public decimal QuoteVolume { get; set; }

    /// <summary>
    /// 成交笔数
    /// </summary>
    [Description("成交笔数（单数）")]
    public int TradeCount { get; set; }
}