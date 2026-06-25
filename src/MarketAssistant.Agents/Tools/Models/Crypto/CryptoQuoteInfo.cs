using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 虚拟币行情数据模型
/// </summary>
[Description("加密货币实时行情数据")]
public class CryptoQuoteInfo
{
    /// <summary>
    /// 币种代码（如 BTC、ETH）
    /// </summary>
    [Description("证券代码/代币符号（如BTC, ETH）")]
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>
    /// 币种名称（如 Bitcoin、Ethereum）
    /// </summary>
    [Description("代币名称")]
    public string SecurityName { get; set; } = string.Empty;

    /// <summary>
    /// 交易状态
    /// </summary>
    [Description("交易状态")]
    public string TradeStatus { get; set; } = string.Empty;

    /// <summary>
    /// 资产类型（虚拟币）
    /// </summary>
    [Description("资产类型（Crypto）")]
    public string SecurityType { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格（USDT）
    /// </summary>
    [Description("当前最新价格（USD/USDT）")]
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 开盘价（USDT）
    /// </summary>
    [Description("24小时开盘价（USD/USDT）")]
    public decimal OpenPrice { get; set; }

    /// <summary>
    /// 昨收价（USDT）
    /// </summary>
    [Description("昨日收盘价（USD/USDT）")]
    public decimal PreviousClosePrice { get; set; }

    /// <summary>
    /// 今日最高价（USDT）
    /// </summary>
    [Description("24小时最高价（USD/USDT）")]
    public decimal HighPrice { get; set; }

    /// <summary>
    /// 今日最低价（USDT）
    /// </summary>
    [Description("24小时最低价（USD/USDT）")]
    public decimal LowPrice { get; set; }

    /// <summary>
    /// 加权平均价（USDT）
    /// </summary>
    [Description("24小时平均均价")]
    public decimal AveragePrice { get; set; }

    /// <summary>
    /// 涨跌价格（USDT）
    /// </summary>
    [Description("价格涨跌额")]
    public decimal PriceChange { get; set; }

    /// <summary>
    /// 涨跌百分比（%）
    /// </summary>
    [Description("价格涨跌幅（%）")]
    public decimal PercentageChange { get; set; }

    /// <summary>
    /// 振幅（%）
    /// </summary>
    [Description("价格振幅（%）")]
    public decimal Amplitude { get; set; }

    /// <summary>
    /// 24h成交量（币数量）
    /// </summary>
    [Description("24小时成交量（代币数量）")]
    public decimal Volume { get; set; }

    /// <summary>
    /// 24h成交额（USDT）
    /// </summary>
    [Description("24小时成交额（USD/USDT）")]
    public decimal Amount { get; set; }
}