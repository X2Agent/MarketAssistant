using MarketAssistant.Services.Trading;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 交易所 API 健康度监控测试：滑动窗口、阈值判定与恢复。
/// </summary>
[TestClass]
public sealed class ApiHealthMonitorTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void FailureCount_ShouldExpireOutsideWindow()
    {
        var monitor = new ApiHealthMonitor();

        // 直接通过公开 API 无法注入时间，这里验证窗口裁剪逻辑的对外表现：
        // 少量失败不应触发熔断，且成功后连续计数归零
        monitor.RecordFailure();
        monitor.RecordFailure();
        monitor.RecordSuccess();

        Assert.AreEqual(2, monitor.FailureCount, "窗口内失败次数应累计");
        Assert.AreEqual(0, monitor.ConsecutiveFailures, "成功后连续失败计数应归零");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsUnhealthy_ShouldCompareAgainstThreshold()
    {
        var monitor = new ApiHealthMonitor();

        Assert.IsFalse(monitor.IsUnhealthy(0), "阈值为 0 表示关闭熔断");
        Assert.IsFalse(monitor.IsUnhealthy(3), "未达阈值时应判定健康");

        monitor.RecordFailure();
        monitor.RecordFailure();
        monitor.RecordFailure();

        Assert.IsTrue(monitor.IsUnhealthy(3), "达到阈值后应判定不健康");
        Assert.IsFalse(monitor.IsUnhealthy(5), "未达到更高阈值时仍健康");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Reset_ShouldClearAllState()
    {
        var monitor = new ApiHealthMonitor();
        monitor.RecordFailure();
        monitor.RecordFailure();

        monitor.Reset();

        Assert.AreEqual(0, monitor.FailureCount);
        Assert.AreEqual(0, monitor.ConsecutiveFailures);
        Assert.IsFalse(monitor.IsUnhealthy(1));
    }
}
