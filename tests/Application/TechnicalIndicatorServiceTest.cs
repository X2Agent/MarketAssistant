using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Technical;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Application;

/// <summary>
/// TechnicalIndicatorService 单元测试：已知 K 线序列的 MA/MACD/BOLL/KDJ 数值断言，
/// 以及样本不足与空数据的行为（不依赖真实网络）。
/// </summary>
[TestClass]
public class TechnicalIndicatorServiceTest
{
    private const int FullBarCount = 250;

    /// <summary>构造收盘价严格递增的 K 线，便于手算 SMA。</summary>
    private static List<KLineData> CreateSeries(int count)
    {
        var start = new DateTime(2024, 1, 1);
        return [.. Enumerable.Range(0, count)
            .Select(index => new KLineData
            {
                Timestamp = start.AddDays(index),
                Open = 100m + index - 0.5m,
                High = 100m + index + 1m,
                Low = 100m + index - 1m,
                Close = 100m + index,
                Volume = 1_000 + index
            })];
    }

    /// <summary>递增序列最后一个周期为 p 的 SMA：收盘价为 100+i，故均值 = (699 - p) / 2。</summary>
    private static decimal ExpectedSma(int period) => (699m - period) / 2m;

    private static TechnicalIndicatorService CreateService(List<KLineData>? kLineData)
    {
        var kLineService = new Mock<IKLineService>();
        kLineService
            .Setup(service => service.GetKLineDataAsync(It.IsAny<string>(), It.IsAny<KLineType>(), It.IsAny<int>()))
            .ReturnsAsync(kLineData ?? []);
        return new TechnicalIndicatorService(kLineService.Object, NullLogger<TechnicalIndicatorService>.Instance);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_WithSufficientBars_ShouldComputeEveryMovingAverage()
    {
        var service = CreateService(CreateSeries(FullBarCount));

        var snapshot = await service.GetSnapshotAsync("600519", KLineType.Daily, FullBarCount);

        foreach (var period in new[] { 3, 5, 10, 15, 20, 30, 60, 120, 200, 250 })
        {
            Assert.AreEqual(
                ExpectedSma(period),
                snapshot.GetMovingAverage(period),
                $"MA{period} 与手算值不一致");
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_ShouldUseLatestBarTimestamp()
    {
        var series = CreateSeries(FullBarCount);
        var service = CreateService(series);

        var snapshot = await service.GetSnapshotAsync("600519");

        Assert.AreEqual(series[^1].Timestamp, snapshot.Timestamp);
        Assert.AreEqual(series[^1].Timestamp.ToString("yyyy-MM-dd"), snapshot.Date);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_ShouldProduceMacdBarAsTwiceDiffMinusDea()
    {
        var service = CreateService(CreateSeries(FullBarCount));

        var snapshot = await service.GetSnapshotAsync("600519");

        Assert.IsNotNull(snapshot.MacdDiff, "250 根 K 线应足以算出 DIFF");
        Assert.IsNotNull(snapshot.MacdDea, "250 根 K 线应足以算出 DEA");
        Assert.IsNotNull(snapshot.MacdFastEma);
        Assert.IsNotNull(snapshot.MacdSlowEma);
        Assert.AreEqual(
            2 * (snapshot.MacdDiff!.Value - snapshot.MacdDea!.Value),
            snapshot.MacdBar,
            "国内口径 MACD 柱线 = 2 × (DIFF - DEA)");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_ShouldComputeBollingerBandsAroundSma20()
    {
        var service = CreateService(CreateSeries(FullBarCount));

        var snapshot = await service.GetSnapshotAsync("600519");

        Assert.IsNotNull(snapshot.BollUpper);
        Assert.IsNotNull(snapshot.BollMiddle);
        Assert.IsNotNull(snapshot.BollLower);
        Assert.AreEqual(ExpectedSma(20), snapshot.BollMiddle, "布林中轨应与 MA20 一致");
        Assert.IsTrue(snapshot.BollUpper > snapshot.BollMiddle, "布林上轨应高于中轨");
        Assert.IsTrue(snapshot.BollLower < snapshot.BollMiddle, "布林下轨应低于中轨");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_ShouldComputeKdjWithJEqualsThreeKMinusTwoD()
    {
        var service = CreateService(CreateSeries(FullBarCount));

        var snapshot = await service.GetSnapshotAsync("600519");

        Assert.IsNotNull(snapshot.KdjK);
        Assert.IsNotNull(snapshot.KdjD);
        Assert.IsNotNull(snapshot.KdjJ);
        Assert.AreEqual(
            (double)(3 * snapshot.KdjK!.Value - 2 * snapshot.KdjD!.Value),
            (double)snapshot.KdjJ!.Value,
            delta: 0.1,
            "KDJ 的 J = 3K - 2D");
        Assert.IsTrue(snapshot.HasAnyValue);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_WithInsufficientBars_ShouldLeaveLongWindowIndicatorsNull()
    {
        // 10 根只够 MA3/MA5/MA10；MA15 及以上、MACD/BOLL/KDJ 均样本不足
        var service = CreateService(CreateSeries(10));

        var snapshot = await service.GetSnapshotAsync("600519");

        Assert.AreEqual(ExpectedSmaForShortSeries(10, 3), snapshot.GetMovingAverage(3));
        Assert.AreEqual(ExpectedSmaForShortSeries(10, 5), snapshot.GetMovingAverage(5));
        Assert.AreEqual(ExpectedSmaForShortSeries(10, 10), snapshot.GetMovingAverage(10));
        Assert.IsNull(snapshot.GetMovingAverage(15), "样本不足时 MA15 应为 null");
        Assert.IsNull(snapshot.GetMovingAverage(250), "样本不足时 MA250 应为 null");
        Assert.IsNull(snapshot.MacdDiff, "样本不足时 DIFF 应为 null");
        Assert.IsNull(snapshot.MacdBar, "样本不足时 MACD 柱线应为 null");
        Assert.IsNull(snapshot.BollMiddle, "样本不足时布林中轨应为 null");
        Assert.IsNull(snapshot.KdjK, "样本不足时 K 应为 null");
        Assert.IsTrue(snapshot.HasAnyValue, "仍应有可用的 MA 值");
    }

    /// <summary>10 根递增序列（收盘价 100..109）最后 p 根的均值。</summary>
    private static decimal ExpectedSmaForShortSeries(int totalCount, int period)
    {
        var last = 100m + totalCount - 1;
        var first = last - period + 1;
        return (first + last) / 2m;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_WithoutAnyBar_ShouldThrowFriendlyException()
    {
        var service = CreateService(null);

        var exception = await Assert.ThrowsExactlyAsync<FriendlyException>(
            () => service.GetSnapshotAsync("600519"));

        Assert.Contains("600519", exception.Message);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_WithBlankCode_ShouldThrowBeforeRequestingKLine()
    {
        var kLineService = new Mock<IKLineService>();
        var service = new TechnicalIndicatorService(kLineService.Object, NullLogger<TechnicalIndicatorService>.Instance);

        await Assert.ThrowsExactlyAsync<FriendlyException>(() => service.GetSnapshotAsync("  "));

        kLineService.Verify(
            client => client.GetKLineDataAsync(It.IsAny<string>(), It.IsAny<KLineType>(), It.IsAny<int>()),
            Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetSnapshotAsync_WithSmallBarCount_ShouldStillRequestTheMacdWarmUpWindow()
    {
        var kLineService = new Mock<IKLineService>();
        kLineService
            .Setup(service => service.GetKLineDataAsync(It.IsAny<string>(), It.IsAny<KLineType>(), It.IsAny<int>()))
            .ReturnsAsync(CreateSeries(30));
        var service = new TechnicalIndicatorService(kLineService.Object, NullLogger<TechnicalIndicatorService>.Instance);

        await service.GetSnapshotAsync("600519", KLineType.Daily, barCount: 5);

        kLineService.Verify(
            client => client.GetKLineDataAsync("600519", KLineType.Daily, 26),
            Times.Once);
    }
}
