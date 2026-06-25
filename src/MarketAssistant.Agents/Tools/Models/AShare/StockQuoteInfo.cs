using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.AShare;

[Description("A股个股实时行情数据")]
public class StockQuoteInfo
{
    [Description("证券名称")]
    public string SecurityName { get; set; } = string.Empty;

    [Description("证券代码")]
    public string SecurityCode { get; set; } = string.Empty;

    [Description("交易状态")]
    public string TradeStatus { get; set; } = string.Empty;

    [Description("证券类型")]
    public string SecurityType { get; set; } = string.Empty;

    [Description("当前价格（元）")]
    public decimal CurrentPrice { get; set; }

    [Description("今日开盘价")]
    public decimal OpenPrice { get; set; }

    [Description("昨日收盘价")]
    public decimal PreviousClosePrice { get; set; }

    [Description("今日最高价（元）")]
    public decimal HighPrice { get; set; }

    [Description("今日最低价（元）")]
    public decimal LowPrice { get; set; }

    [Description("涨停价格")]
    public decimal UpLimitPrice { get; set; }

    [Description("跌停价格")]
    public decimal DownLimitPrice { get; set; }

    [Description("涨跌金额")]
    public decimal PriceChange { get; set; }

    [Description("涨跌幅（%）")]
    public decimal PercentageChange { get; set; }

    [Description("3日涨跌幅（%）")]
    public decimal PercentageChange3Day { get; set; }

    [Description("5日涨跌幅（%）")]
    public decimal PercentageChange5Day { get; set; }

    [Description("振幅（%）")]
    public decimal Amplitude { get; set; }

    [Description("成交量（手）")]
    public decimal Volume { get; set; }

    [Description("成交额（元）")]
    public decimal Amount { get; set; }

    [Description("换手率（%）")]
    public decimal TurnoverRate { get; set; }

    [Description("量比（大于1表示放量）")]
    public decimal VolumeRatio { get; set; }

    [Description("委比（%），正值表示买盘强")]
    public decimal EntrustRatio { get; set; }

    [Description("均价（元）")]
    public decimal AveragePrice { get; set; }

    [Description("总股本")]
    public decimal TotalShares { get; set; }

    [Description("流通股本")]
    public decimal NonRestrictedShares { get; set; }

    [Description("总市值（元）")]
    public decimal MarketCapitalization { get; set; }

    [Description("流通市值（元）")]
    public decimal CirculationMarketCap { get; set; }

    [Description("市盈率（静）")]
    public decimal PERatio { get; set; }

    [Description("市盈率（TTM）")]
    public decimal TTMPERatio { get; set; }

    [Description("市净率")]
    public decimal PBRatio { get; set; }

    [Description("每股净资产（元）")]
    public decimal NetAssetPerShare { get; set; }
}