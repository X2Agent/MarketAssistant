namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 虚拟币行情数据模型
/// </summary>
public class CryptoQuoteInfo
{
    /// <summary>
    /// 币种代码（如 BTC、ETH）
    /// </summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>
    /// 币种名称（如 Bitcoin、Ethereum）
    /// </summary>
    public string SecurityName { get; set; } = string.Empty;

    /// <summary>
    /// 交易状态
    /// </summary>
    public string TradeStatus { get; set; } = string.Empty;

    /// <summary>
    /// 资产类型（虚拟币）
    /// </summary>
    public string SecurityType { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格（USDT）
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 开盘价（USDT）
    /// </summary>
    public decimal OpenPrice { get; set; }

    /// <summary>
    /// 昨收价（USDT）
    /// </summary>
    public decimal PreviousClosePrice { get; set; }

    /// <summary>
    /// 今日最高价（USDT）
    /// </summary>
    public decimal HighPrice { get; set; }

    /// <summary>
    /// 今日最低价（USDT）
    /// </summary>
    public decimal LowPrice { get; set; }

    /// <summary>
    /// 加权平均价（USDT）
    /// </summary>
    public decimal AveragePrice { get; set; }

    /// <summary>
    /// 涨跌价格（USDT）
    /// </summary>
    public decimal PriceChange { get; set; }

    /// <summary>
    /// 涨跌百分比（%）
    /// </summary>
    public decimal PercentageChange { get; set; }

    /// <summary>
    /// 振幅（%）
    /// </summary>
    public decimal Amplitude { get; set; }

    /// <summary>
    /// 24h成交量（币数量）
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// 24h成交额（USDT）
    /// </summary>
    public decimal Amount { get; set; }
}
