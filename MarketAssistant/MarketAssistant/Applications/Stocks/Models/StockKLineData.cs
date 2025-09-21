using System.Text.Json.Serialization;

namespace MarketAssistant.Applications.Stocks.Models;

/// <summary>
/// K线图数据模型
/// </summary>
public class StockKLineData
{
    /// <summary>
    /// 交易日期/时间
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// 开盘价
    /// </summary>
    [JsonPropertyName("open")]
    public decimal Open { get; set; }

    /// <summary>
    /// 最高价
    /// </summary>
    [JsonPropertyName("high")]
    public decimal High { get; set; }

    /// <summary>
    /// 最低价
    /// </summary>
    [JsonPropertyName("low")]
    public decimal Low { get; set; }

    /// <summary>
    /// 收盘价
    /// </summary>
    [JsonPropertyName("close")]
    public decimal Close { get; set; }

    /// <summary>
    /// 成交量
    /// </summary>
    [JsonPropertyName("volume")]
    public decimal Volume { get; set; }

    /// <summary>
    /// 昨收价
    /// </summary>
    [JsonPropertyName("pre_close")]
    public decimal PreClose { get; set; }

    /// <summary>
    /// 涨跌额
    /// </summary>
    [JsonPropertyName("change")]
    public decimal Change { get; set; }

    /// <summary>
    /// 涨跌幅百分比
    /// </summary>
    [JsonPropertyName("pct_chg")]
    public decimal PctChg { get; set; }

    /// <summary>
    /// 成交额
    /// </summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }
}

/// <summary>
/// K线图数据集合
/// </summary>
public class StockKLineDataSet
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 时间周期（日K、周K、月K等）
    /// </summary>
    public string Interval { get; set; } = string.Empty;

    /// <summary>
    /// K线数据列表
    /// </summary>
    public List<StockKLineData> Data { get; set; } = new List<StockKLineData>();
}