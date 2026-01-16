namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 多空比历史数�?
/// </summary>
public class LongShortRatioHistory
{
    /// <summary>
    /// 交易对符�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前最新多头占�?
    /// </summary>
    public decimal CurrentLongRatio { get; set; }

    /// <summary>
    /// 当前最新空头占�?
    /// </summary>
    public decimal CurrentShortRatio { get; set; }

    /// <summary>
    /// 当前多空比率（Long/Short�?
    /// </summary>
    public decimal CurrentRatio { get; set; }

    /// <summary>
    /// 平均多空比率
    /// </summary>
    public decimal AverageRatio { get; set; }

    /// <summary>
    /// 历史多空比数据点（按时间倒序，最新的在前�?
    /// </summary>
    public List<LongShortRatioPoint> History { get; set; } = [];
}

/// <summary>
/// 单个多空比数据点
/// </summary>
public class LongShortRatioPoint
{
    /// <summary>
    /// 多头占比
    /// </summary>
    public decimal LongRatio { get; set; }

    /// <summary>
    /// 空头占比
    /// </summary>
    public decimal ShortRatio { get; set; }

    /// <summary>
    /// 多空比率（Long/Short�?
    /// </summary>
    public decimal Ratio { get; set; }

    /// <summary>
    /// 数据时间戳（Unix 毫秒�?
    /// </summary>
    public long Timestamp { get; set; }
}
