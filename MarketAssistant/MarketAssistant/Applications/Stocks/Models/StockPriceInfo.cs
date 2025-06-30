namespace MarketAssistant.Applications.Stocks.Models;

public class StockPriceInfo
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
}
