using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 连续亏损状态机与紧急停机状态并发竞态回归测试。
/// </summary>
[TestClass]
public sealed class DailyStatsAndHaltStateTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task UpdateDailyStatsAsync_ConsecutiveLosses_ShouldIncrementOnLoss_KeepOnZero_ResetOnProfit()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"market_test_{Guid.NewGuid():N}.db");
        try
        {
            var userSettingService = new Mock<IUserSettingService>();
            userSettingService
                .Setup(service => service.CurrentSetting)
                .Returns(new UserSetting { CryptoTradingMode = TradingMode.SpotDemo });
            var monitorProvider = new Mock<IMarketMonitorProvider>();
            var environment = new TradingEnvironmentService(
                userSettingService.Object,
                monitorProvider.Object,
                NullLogger<TradingEnvironmentService>.Instance);

            var schema = new TradingSchemaInitializer(NullLogger.Instance, tempDbPath);
            var repository = new DailyStatsRepository(schema, environment, NullLogger.Instance);

            // 第 1 笔亏损：连亏 = 1
            await repository.UpdateDailyStatsAsync(pnl: -10m, commission: 0.1m, countTrade: true);
            var stats = await repository.GetTodayStatsAsync();
            Assert.AreEqual(1, stats.ConsecutiveLosses, "首笔亏损后连亏计数应为 1");
            Assert.AreEqual(1, stats.TradeCount);
            Assert.AreEqual(-10m, stats.TotalPnl);

            // 第 2 笔亏损：连亏 = 2
            await repository.UpdateDailyStatsAsync(pnl: -5m, commission: 0.1m, countTrade: true);
            stats = await repository.GetTodayStatsAsync();
            Assert.AreEqual(2, stats.ConsecutiveLosses, "连续两笔亏损后连亏计数应为 2");

            // 开仓成交（无已实现盈亏 pnl == 0）：连亏保持 2
            await repository.UpdateDailyStatsAsync(pnl: 0m, commission: 0.1m, countTrade: true);
            stats = await repository.GetTodayStatsAsync();
            Assert.AreEqual(2, stats.ConsecutiveLosses, "开仓成交（pnl=0）不应改变连亏计数");

            // 盈利成交（pnl > 0）：连亏必须归零
            await repository.UpdateDailyStatsAsync(pnl: 20m, commission: 0.1m, countTrade: true);
            stats = await repository.GetTodayStatsAsync();
            Assert.AreEqual(0, stats.ConsecutiveLosses, "实现盈利后连亏计数必须归零");
            Assert.AreEqual(5m, stats.TotalPnl);

            // 归零后再次亏损：从 1 重新计数
            await repository.UpdateDailyStatsAsync(pnl: -3m, commission: 0.1m, countTrade: true);
            stats = await repository.GetTodayStatsAsync();
            Assert.AreEqual(1, stats.ConsecutiveLosses, "盈利归零后再次亏损应从 1 开始计数");
        }
        finally
        {
            using var conn = new SqliteConnection($"Data Source={tempDbPath}");
            SqliteConnection.ClearPool(conn);
            if (File.Exists(tempDbPath))
                File.Delete(tempDbPath);
        }
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task TradingHaltState_WhenHaltCalledDuringSlowLoad_ShouldNotBeOverwrittenByStaleLoad()
    {
        var loadTcs = new TaskCompletionSource<(bool Halted, string Reason, DateTime? HaltedAtUtc)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var dataService = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        dataService
            .Setup(service => service.LoadTradingHaltAsync(It.IsAny<CancellationToken>()))
            .Returns(loadTcs.Task);
        dataService
            .Setup(service => service.SaveTradingHaltAsync(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var haltState = new TradingHaltState(dataService.Object, NullLogger<TradingHaltState>.Instance);

        // 1. 启动慢速加载（模拟启动初期读取数据库尚未返回）
        var loadTask = haltState.EnsureLoadedAsync();

        // 2. 加载完成前触发紧急停机
        await haltState.HaltAsync("用户紧急停止");
        Assert.IsTrue(haltState.IsHalted);
        Assert.AreEqual("用户紧急停止", haltState.HaltReason);

        // 3. 慢速加载随后返回旧的“未停机”结果
        loadTcs.SetResult((false, string.Empty, null));
        await loadTask;

        // 4. 内存态绝不能被过期的数据库读结果覆盖回未停机
        Assert.IsTrue(haltState.IsHalted, "慢速加载返回的旧未停机状态不得覆盖已登记的紧急停机");
        Assert.AreEqual("用户紧急停止", haltState.HaltReason);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task TradingHaltState_WhenResumeCalledDuringFailingLoad_ShouldRemainResumed()
    {
        var loadTcs = new TaskCompletionSource<(bool Halted, string Reason, DateTime? HaltedAtUtc)>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var dataService = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        dataService
            .Setup(service => service.LoadTradingHaltAsync(It.IsAny<CancellationToken>()))
            .Returns(loadTcs.Task);
        dataService
            .Setup(service => service.SaveTradingHaltAsync(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var haltState = new TradingHaltState(dataService.Object, NullLogger<TradingHaltState>.Instance);

        var loadTask = haltState.EnsureLoadedAsync();
        await haltState.ResumeAsync();

        loadTcs.SetException(new InvalidOperationException("模拟旧加载异常"));
        await loadTask;

        Assert.IsFalse(haltState.IsHalted, "显式 Resume 后，在途旧加载异常不得把状态覆盖为 fail-closed 停机");
        Assert.IsFalse(haltState.HasLoadError);
    }
}
