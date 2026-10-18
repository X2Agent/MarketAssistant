namespace MarketAssistant.Applications.Technical.Models;

/// <summary>
/// 技术指标快照（市场无关，由本地 K 线计算）。字段为 null 表示样本不足或该指标不可用。
/// </summary>
public sealed record TechnicalIndicatorSnapshot(
    DateTime Timestamp,
    IReadOnlyDictionary<int, decimal?> MovingAverages,
    decimal? MacdDiff,
    decimal? MacdDea,
    decimal? MacdBar,
    decimal? MacdFastEma,
    decimal? MacdSlowEma,
    decimal? BollUpper,
    decimal? BollMiddle,
    decimal? BollLower,
    decimal? KdjK,
    decimal? KdjD,
    decimal? KdjJ,
    decimal? Rsi = null)
{
    /// <summary>快照对应的 K 线日期（日线口径）。</summary>
    public string Date => Timestamp.ToString("yyyy-MM-dd");

    /// <summary>MA 周期集合，供界面按固定顺序渲染。</summary>
    public static readonly int[] MovingAveragePeriods = [3, 5, 10, 15, 20, 30, 60, 120, 200, 250];

    public decimal? GetMovingAverage(int period) =>
        MovingAverages.TryGetValue(period, out var value) ? value : null;

    public bool HasAnyValue =>
        MovingAverages.Values.Any(value => value.HasValue) ||
        MacdDiff.HasValue || BollMiddle.HasValue || KdjK.HasValue;
}
