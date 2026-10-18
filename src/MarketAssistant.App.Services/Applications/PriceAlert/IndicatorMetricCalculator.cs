using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Technical.Models;

namespace MarketAssistant.Applications.PriceAlert;

/// <summary>
/// 指标告警度量计算（纯函数，无 IO）：把 K 线与指标快照折算为规则可比较的度量值。
/// 指标公式不在本类重复实现——全部取自 <see cref="TechnicalIndicatorSnapshot"/>。
/// </summary>
public static class IndicatorMetricCalculator
{
    /// <summary>量比均量样本根数（不含最新一根）。</summary>
    public const int VolumeAverageBars = 20;

    /// <summary>由 K 线数据与指标快照构建度量快照；样本不足的字段为 null，对应条件本次不评估。</summary>
    public static AlertRuleMetric Calculate(
        IReadOnlyList<KLineData> kLineData, TechnicalIndicatorSnapshot indicator)
    {
        ArgumentNullException.ThrowIfNull(kLineData);
        ArgumentNullException.ThrowIfNull(indicator);

        return new AlertRuleMetric(
            VolumeRatio: CalculateVolumeRatio(kLineData),
            MacdDiff: indicator.MacdDiff,
            MacdDea: indicator.MacdDea,
            Rsi: indicator.Rsi,
            KdjK: indicator.KdjK,
            KdjD: indicator.KdjD,
            BollUpper: indicator.BollUpper,
            BollLower: indicator.BollLower,
            ClosePrice: GetLatestClose(kLineData),
            MaShort: indicator.GetMovingAverage(5),
            MaLong: indicator.GetMovingAverage(10));
    }

    /// <summary>
    /// 量比 = 最新 K 线成交量 / 前 N 根平均成交量。
    /// 样本不足或均量为 0（无成交/停牌）时返回 null，不得用 0 伪装成"量比为 0"。
    /// </summary>
    public static decimal? CalculateVolumeRatio(IReadOnlyList<KLineData> kLineData)
    {
        ArgumentNullException.ThrowIfNull(kLineData);
        if (kLineData.Count < 2)
            return null;

        var ordered = kLineData.OrderBy(item => item.Timestamp).ToList();
        var latestVolume = ordered[^1].Volume;
        var previous = ordered.Take(ordered.Count - 1).TakeLast(VolumeAverageBars).ToList();
        if (previous.Count == 0)
            return null;

        var averageVolume = previous.Average(item => item.Volume);
        return averageVolume <= 0 ? null : latestVolume / averageVolume;
    }

    private static decimal? GetLatestClose(IReadOnlyList<KLineData> kLineData)
    {
        var latest = kLineData.OrderBy(item => item.Timestamp).LastOrDefault();
        return latest is null || latest.Close <= 0 ? null : latest.Close;
    }
}
