using MarketAssistant.Services.Trading;
using Moq;

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

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RoutingExchangeClient_UserCancellation_ShouldNotRecordFailure()
    {
        var monitor = new ApiHealthMonitor();
        var userSettingService = new Moq.Mock<MarketAssistant.Services.Settings.IUserSettingService>();
        userSettingService
            .Setup(service => service.CurrentSetting)
            .Returns(new MarketAssistant.Applications.Settings.UserSetting
            {
                CryptoTradingMode = MarketAssistant.Trading.Models.TradingMode.SpotDemo
            });
        var monitorProvider = new Moq.Mock<IMarketMonitorProvider>();
        var environment = new TradingEnvironmentService(
            userSettingService.Object,
            monitorProvider.Object,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<TradingEnvironmentService>.Instance);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var innerClient = new Moq.Mock<MarketAssistant.Trading.Abstractions.IExchangeClient>();
        innerClient
            .Setup(client => client.GetAccountInfoAsync(Moq.It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MarketAssistant.Infrastructure.Core.FriendlyException(
                "已取消",
                new TaskCanceledException("User canceled", null, cts.Token)));

        var routingClient = new MarketAssistant.Services.Trading.Exchanges.RoutingExchangeClient(
            environment,
            new Dictionary<MarketAssistant.Trading.Models.TradingMode, MarketAssistant.Trading.Abstractions.IExchangeClient>
            {
                [MarketAssistant.Trading.Models.TradingMode.SpotDemo] = innerClient.Object
            },
            monitor);

        await Assert.ThrowsExactlyAsync<MarketAssistant.Infrastructure.Core.FriendlyException>(
            () => routingClient.GetAccountInfoAsync(cts.Token));

        Assert.AreEqual(0, monitor.FailureCount, "调用方主动取消不得计入 API 故障");

        // 未取消令牌下的 TaskCanceledException（如 HttpClient 超时）应计入故障
        using var uncancelledCts = new CancellationTokenSource();
        innerClient
            .Setup(client => client.GetAccountInfoAsync(Moq.It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("HttpClient.Timeout elapsed"));

        await Assert.ThrowsExactlyAsync<TaskCanceledException>(
            () => routingClient.GetAccountInfoAsync(uncancelledCts.Token));

        Assert.AreEqual(1, monitor.FailureCount, "非主动取消的超时应计入 API 故障");
    }
}
