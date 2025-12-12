namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 股票行情数据模型
/// </summary>
public class StockQuoteInfo
{
    /// <summary>
    /// 当前价格（元）
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 今日最高价（元）
    /// </summary>
    public decimal HighPrice { get; set; }

    /// <summary>
    /// 今日最低价（元）
    /// </summary>
    public decimal LowPrice { get; set; }

    /// <summary>
    /// 成交量（万手）
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// 成交额（亿）
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 涨跌价格（元）
    /// </summary>
    public decimal PriceChange { get; set; }

    /// <summary>
    /// 涨跌百分比（%）
    /// </summary>
    public decimal PercentageChange { get; set; }

    /// <summary>
    /// 换手率（%）
    /// </summary>
    public decimal TurnoverRate { get; set; }

    /// <summary>
    /// 3日涨跌百分比（%）
    /// </summary>
    public decimal PercentageChange3Day { get; set; }

    /// <summary>
    /// 5日涨跌百分比（%）
    /// </summary>
    public decimal PercentageChange5Day { get; set; }

    /// <summary>
    /// 总股本
    /// </summary>
    public decimal TotalShares { get; set; }

    /// <summary>
    /// 总市值（亿）
    /// </summary>
    public decimal MarketCapitalization { get; set; }

    /// <summary>
    /// 股票名称
    /// </summary>
    public string SecurityName { get; set; } = string.Empty;

    /// <summary>
    /// 股票代码
    /// </summary>
    public string SecurityCode { get; set; } = string.Empty;

    /// <summary>
    /// 交易状态
    /// </summary>
    public string TradeStatus { get; set; } = string.Empty;

    /// <summary>
    /// 证券类型
    /// </summary>
    public string SecurityType { get; set; } = string.Empty;

    /// <summary>
    /// 开盘价（元）
    /// </summary>
    public decimal OpenPrice { get; set; }

    /// <summary>
    /// 昨收价（元）
    /// </summary>
    public decimal PreviousClosePrice { get; set; }

    /// <summary>
    /// 涨停价（元）
    /// </summary>
    public decimal UpLimitPrice { get; set; }

    /// <summary>
    /// 跌停价（元）
    /// </summary>
    public decimal DownLimitPrice { get; set; }

    /// <summary>
    /// 振幅（%）
    /// </summary>
    public decimal Amplitude { get; set; }

    /// <summary>
    /// 市盈率
    /// </summary>
    public decimal PERatio { get; set; }

    /// <summary>
    /// TTM市盈率
    /// </summary>
    public decimal TTMPERatio { get; set; }

    /// <summary>
    /// 市净率
    /// </summary>
    public decimal PBRatio { get; set; }

    /// <summary>
    /// 流通市值（亿）
    /// </summary>
    public decimal CirculationMarketCap { get; set; }

    /// <summary>
    /// 流通股本
    /// </summary>
    public decimal NonRestrictedShares { get; set; }

    /// <summary>
    /// 每股净资产（元）
    /// </summary>
    public decimal NetAssetPerShare { get; set; }

    /// <summary>
    /// 均价（元）
    /// </summary>
    public decimal AveragePrice { get; set; }

    /// <summary>
    /// 量比
    /// </summary>
    public decimal VolumeRatio { get; set; }

    /// <summary>
    /// 委比（%）
    /// </summary>
    public decimal EntrustRatio { get; set; }
}
