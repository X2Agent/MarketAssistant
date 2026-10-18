using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.PriceAlert;

namespace TestMarketAssistant.Application;

/// <summary>
/// 指标度量计算测试：量比口径与样本不足的处理。
/// </summary>
[TestClass]
public sealed class IndicatorMetricCalculatorTest
{
    private static KLineData CreateBar(DateTime timestamp, decimal volume, decimal close = 100m) => new()
    {
        Timestamp = timestamp,
        Open = close,
        High = close,
        Low = close,
        Close = close,
        Volume = volume
    };

    [TestMethod]
    [TestCategory("Unit")]
    public void VolumeRatio_ShouldDivideLatestVolumeByPreviousAverage()
    {
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<KLineData>();
        for (var i = 0; i < 20; i++)
            bars.Add(CreateBar(baseTime.AddHours(i), volume: 10m));
        bars.Add(CreateBar(baseTime.AddHours(20), volume: 35m));

        var ratio = IndicatorMetricCalculator.CalculateVolumeRatio(bars);

        // 前 20 根均量 10，最新 35 → 3.5 倍
        Assert.IsNotNull(ratio);
        Assert.AreEqual(3.5m, ratio!.Value, 0.0001m);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void VolumeRatio_WithZeroAverage_ShouldReturnNull()
    {
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<KLineData>
        {
            CreateBar(baseTime, volume: 0m),
            CreateBar(baseTime.AddHours(1), volume: 0m)
        };

        // 均量为 0（停牌/无成交）不得伪装成"量比为 0"
        Assert.IsNull(IndicatorMetricCalculator.CalculateVolumeRatio(bars));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void VolumeRatio_WithInsufficientSamples_ShouldReturnNull()
    {
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.IsNull(IndicatorMetricCalculator.CalculateVolumeRatio([CreateBar(baseTime, volume: 10m)]));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void VolumeRatio_ShouldIgnoreBarsBeyondAverageWindow()
    {
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<KLineData>();
        // 30 根 10 量 + 最新 60 量：均量窗口只取最后 20 根（都是 10）→ 6 倍
        for (var i = 0; i < 30; i++)
            bars.Add(CreateBar(baseTime.AddHours(i), volume: 10m));
        bars.Add(CreateBar(baseTime.AddHours(30), volume: 60m));

        Assert.AreEqual(6m, IndicatorMetricCalculator.CalculateVolumeRatio(bars)!.Value, 0.0001m);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Calculate_ShouldMapIndicatorSnapshotIntoMetric()
    {
        var baseTime = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var bars = new List<KLineData>
        {
            CreateBar(baseTime, volume: 10m, close: 100m),
            CreateBar(baseTime.AddHours(1), volume: 20m, close: 110m)
        };

        var snapshot = new MarketAssistant.Applications.Technical.Models.TechnicalIndicatorSnapshot(
            Timestamp: baseTime.AddHours(1),
            MovingAverages: new Dictionary<int, decimal?> { [5] = 105m, [10] = 100m },
            MacdDiff: 1.5m,
            MacdDea: 1.0m,
            MacdBar: 1.0m,
            MacdFastEma: null,
            MacdSlowEma: null,
            BollUpper: 120m,
            BollMiddle: 105m,
            BollLower: 90m,
            KdjK: 80m,
            KdjD: 70m,
            KdjJ: 100m,
            Rsi: 65m);

        var metric = IndicatorMetricCalculator.Calculate(bars, snapshot);

        Assert.AreEqual(2m, metric.VolumeRatio!.Value, 0.0001m);
        Assert.AreEqual(110m, metric.ClosePrice);
        Assert.AreEqual(1.5m, metric.MacdDiff);
        Assert.AreEqual(1.0m, metric.MacdDea);
        Assert.AreEqual(65m, metric.Rsi);
        Assert.AreEqual(80m, metric.KdjK);
        Assert.AreEqual(70m, metric.KdjD);
        Assert.AreEqual(120m, metric.BollUpper);
        Assert.AreEqual(90m, metric.BollLower);
        Assert.AreEqual(105m, metric.MaShort);
        Assert.AreEqual(100m, metric.MaLong);
    }
}
