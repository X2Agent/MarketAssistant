namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 加密货币波动性指�?
/// </summary>
public class CryptoVolatilityMetrics
{
    /// <summary>
    /// 交易对符�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 历史波动率（年化�?�?
    /// </summary>
    public decimal AnnualizedVolatility { get; set; }

    /// <summary>
    /// 日波动率�?�?
    /// </summary>
    public decimal DailyVolatility { get; set; }

    /// <summary>
    /// 平均真实波幅（ATR�?
    /// </summary>
    public decimal AverageTrueRange { get; set; }

    /// <summary>
    /// 最大回撤（%�?
    /// </summary>
    public decimal MaxDrawdown { get; set; }

    /// <summary>
    /// 最大回撤起始时�?
    /// </summary>
    public long MaxDrawdownStartTime { get; set; }

    /// <summary>
    /// 最大回撤结束时�?
    /// </summary>
    public long MaxDrawdownEndTime { get; set; }

    /// <summary>
    /// 夏普比率（Sharpe Ratio�?
    /// </summary>
    /// <remarks>
    /// 衡量风险调整后收益，数值越高越�?
    /// </remarks>
    public decimal? SharpeRatio { get; set; }

    /// <summary>
    /// 统计周期（天数）
    /// </summary>
    public int PeriodDays { get; set; }

    /// <summary>
    /// 价格标准�?
    /// </summary>
    public decimal StandardDeviation { get; set; }

    /// <summary>
    /// 平均收益率（%�?
    /// </summary>
    public decimal AverageReturn { get; set; }
}
