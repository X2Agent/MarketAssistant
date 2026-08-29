using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币实时行情数据")]
public class CryptoQuoteInfo
{
    [Description("证券代码/代币符号（如BTC, ETH）")]
    public string SecurityCode { get; set; } = string.Empty;

    [Description("代币名称")]
    public string SecurityName { get; set; } = string.Empty;

    [Description("交易状态")]
    public string TradeStatus { get; set; } = string.Empty;

    [Description("资产类型（Crypto）")]
    public string SecurityType { get; set; } = string.Empty;

    [Description("当前最新价格（USD/USDT）")]
    public decimal CurrentPrice { get; set; }

    [Description("24小时开盘价（USD/USDT）")]
    public decimal OpenPrice { get; set; }

    [Description("昨日收盘价（USD/USDT）")]
    public decimal PreviousClosePrice { get; set; }

    [Description("24小时最高价（USD/USDT）")]
    public decimal HighPrice { get; set; }

    [Description("24小时最低价（USD/USDT）")]
    public decimal LowPrice { get; set; }

    [Description("24小时平均均价")]
    public decimal AveragePrice { get; set; }

    [Description("价格涨跌额")]
    public decimal PriceChange { get; set; }

    [Description("价格涨跌幅（%）")]
    public decimal PercentageChange { get; set; }

    [Description("价格振幅（%）")]
    public decimal Amplitude { get; set; }

    [Description("24小时成交量（代币数量）")]
    public decimal Volume { get; set; }

    [Description("24小时成交额（USD/USDT）")]
    public decimal Amount { get; set; }
}
