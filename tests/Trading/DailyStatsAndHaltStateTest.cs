using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Services.Trading.Exchanges;
using MarketAssistant.Trading.Abstractions;
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

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RiskManager_CircuitBreakers_AndConfirmationThreshold_ShouldEnforceCorrectly()
    {
        var exchangeClient = new Mock<IExchangeClient>();
        exchangeClient.SetupGet(c => c.IsFutures).Returns(false);

        var portfolioService = new Mock<CryptoPortfolioService>(null!, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance);
        portfolioService
            .Setup(s => s.GetAccountBalanceSummaryAsync(It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            .ReturnsAsync(new AccountBalanceSummary
            {
                TotalValueUSDT = 1000m,
                Assets = [new AssetBalance { Asset = "USDT", Free = 1000m, Locked = 0m, ValueUSDT = 1000m }]
            });

        var dataService = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        dataService
            .Setup(s => s.LoadTradingHaltAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, string.Empty, (DateTime?)null));
        dataService
            .Setup(s => s.SaveTradingHaltAsync(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        dataService
            .Setup(s => s.SaveAccountSnapshotAsync(It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var haltState = new TradingHaltState(dataService.Object, NullLogger<TradingHaltState>.Instance);
        var apiHealth = new ApiHealthMonitor();
        var riskManager = new RiskManager(
            dataService.Object,
            portfolioService.Object,
            exchangeClient.Object,
            haltState,
            apiHealth,
            NullLogger<RiskManager>.Instance);

        // 1. 连续亏损熔断
        dataService
            .Setup(s => s.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskConfig { MaxConsecutiveLosses = 3, MaxSingleOrderPercent = 50m });
        dataService
            .Setup(s => s.GetTodayStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyStats { ConsecutiveLosses = 3, TotalPnl = -20m });
        var consecutiveResult = await riskManager.ValidateOrderAsync("BTCUSDT", OrderSide.Buy, 1m, 100m, OrderType.Limit);
        Assert.IsFalse(consecutiveResult.Passed);
        Assert.IsTrue((consecutiveResult.Reason ?? string.Empty).Contains("连续亏损", StringComparison.Ordinal));

        // 2. 最大回撤熔断（峰值 1250，当前 1000 → 回撤 20% >= 15%）
        dataService
            .Setup(s => s.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskConfig { MaxConsecutiveLosses = 5, MaxDrawdownPercent = 15m, MaxSingleOrderPercent = 50m });
        dataService
            .Setup(s => s.GetTodayStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyStats { ConsecutiveLosses = 0, TotalPnl = 0m });
        dataService
            .Setup(s => s.GetPeakAccountValueAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1250m);
        var drawdownResult = await riskManager.ValidateOrderAsync("BTCUSDT", OrderSide.Buy, 1m, 100m, OrderType.Limit);
        Assert.IsFalse(drawdownResult.Passed);
        Assert.IsTrue((drawdownResult.Reason ?? string.Empty).Contains("账户回撤", StringComparison.Ordinal));

        // 3. 日亏损百分比熔断（总值 1000，今日亏损 -60 → 6% >= 5%）
        dataService
            .Setup(s => s.GetPeakAccountValueAsync(It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1000m);
        dataService
            .Setup(s => s.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskConfig { MaxDrawdownPercent = 20m, MaxDailyLossPercent = 5m, MaxSingleOrderPercent = 50m });
        dataService
            .Setup(s => s.GetTodayStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyStats { TotalPnl = -60m });
        var dailyLossResult = await riskManager.ValidateOrderAsync("BTCUSDT", OrderSide.Buy, 1m, 100m, OrderType.Limit);
        Assert.IsFalse(dailyLossResult.Passed);
        Assert.IsTrue((dailyLossResult.Reason ?? string.Empty).Contains("今日亏损", StringComparison.Ordinal));

        // 4. 金额超过人工确认阈值
        dataService
            .Setup(s => s.GetTodayStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DailyStats { TotalPnl = 0m });
        dataService
            .Setup(s => s.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskConfig
            {
                MaxDrawdownPercent = 20m,
                MaxDailyLossPercent = 10m,
                MaxSingleOrderPercent = 50m,
                RequireConfirmation = true,
                ConfirmationThreshold = 80m
            });
        var confirmResult = await riskManager.ValidateOrderAsync("BTCUSDT", OrderSide.Buy, 1m, 100m, OrderType.Limit);
        Assert.IsFalse(confirmResult.Passed, "需人工确认的订单 Passed 应为 false 以阻断自动直发");
        Assert.IsTrue(confirmResult.NeedsConfirmation);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ReconcileTradeRecordAsync_WhenSellOrderFills_ShouldPersistRealizedPnlToDatabase()
    {
        var tempDbPath = Path.Combine(Path.GetTempPath(), $"market_reconcile_test_{Guid.NewGuid():N}.db");
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
            var strategies = new TradingStrategyRepository(schema, environment, NullLogger.Instance);
            var positions = new PositionRepository(schema, environment, NullLogger.Instance);
            var dailyStats = new DailyStatsRepository(schema, environment, NullLogger.Instance);
            var records = new TradeRecordRepository(schema, environment, NullLogger.Instance, positions, dailyStats);

            await strategies.SaveStrategyAsync(new TradingStrategy
            {
                Id = "strat-reconcile",
                Symbol = "BTCUSDT",
                Type = StrategyType.TakeProfit,
                Status = StrategyStatus.Active,
                Side = OrderSide.Sell,
                Quantity = 1m
            });

            // 1. 先开仓 1 BTC @ 100 USDT
            await positions.OpenPositionAsync(new Position
            {
                Symbol = "BTCUSDT",
                Side = PositionSide.Long,
                Quantity = 1m,
                EntryPrice = 100m
            });

            // 2. 保存一笔未成交的限价卖出单
            var pendingSell = new TradeRecord
            {
                StrategyId = "strat-reconcile",
                Symbol = "BTCUSDT",
                Side = OrderSide.Sell,
                OrderType = OrderType.Limit,
                RequestedQty = 1m,
                RequestedPrice = 120m,
                ExecutedQty = 0m,
                ExecutedPrice = 0m,
                Status = TradeRecordStatus.Pending,
                ExchangeOrderId = 88801
            };
            await records.SaveTradeRecordAsync(pendingSell);

            // 3. 异步对账成交：卖出 1 BTC @ 120 USDT → FIFO 已实现盈亏应为 (120 - 100) * 1 = 20
            var reconciled = await records.ReconcileTradeRecordAsync(pendingSell, new ExchangeOrderResult
            {
                OrderId = "88801",
                Status = "FILLED",
                RequestedQty = 1m,
                ExecutedQty = 1m,
                Price = 120m,
                CumulativeQuoteQty = 120m,
                FillCommission = 0.5m,
                CommissionAsset = "USDT"
            });

            Assert.AreEqual(20m, reconciled.RealizedPnl);

            // 4. 从数据库重新读取，验证 realized_pnl 列已持久化
            var storedList = await records.GetRecordsByStrategyAsync("strat-reconcile");
            Assert.AreEqual(1, storedList.Count);
            Assert.AreEqual(20m, storedList[0].RealizedPnl, "异步对账平仓单的已实现盈亏应写回数据库 trade_records.realized_pnl 列");
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
    public async Task EmergencyStopService_CloseAllPositionsAsync_ShouldPauseStrategies_HaltAndAggregatePartialFailures()
    {
        var settingService = new Mock<IUserSettingService>();
        settingService
            .SetupGet(s => s.CurrentSetting)
            .Returns(new UserSetting { CryptoTradingMode = TradingMode.FuturesTestnet });

        var monitor = new Mock<MarketMonitor>();
        monitor.SetupGet(m => m.IsRunning).Returns(true);
        monitor.Setup(m => m.TryStopAsync()).ReturnsAsync(true);

        var monitorProvider = new Mock<IMarketMonitorProvider>();
        monitorProvider.Setup(p => p.GetMonitor()).Returns(monitor.Object);

        var environment = new TradingEnvironmentService(
            settingService.Object,
            monitorProvider.Object,
            NullLogger<TradingEnvironmentService>.Instance);

        var dataService = new Mock<TradingDataService>(environment, NullLogger<TradingDataService>.Instance);
        dataService
            .Setup(s => s.LoadTradingHaltAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, string.Empty, (DateTime?)null));
        dataService
            .Setup(s => s.SaveTradingHaltAsync(It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var strategyService = new Mock<TradingStrategyService>(dataService.Object);
        strategyService
            .Setup(s => s.GetStrategiesByStatusAsync(StrategyStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new TradingStrategy { Id = "s1", Symbol = "BTCUSDT", Status = StrategyStatus.Active },
                new TradingStrategy { Id = "s2", Symbol = "ETHUSDT", Status = StrategyStatus.Active }
            ]);
        strategyService
            .Setup(s => s.UpdateStrategyStatusAsync(It.IsAny<string>(), StrategyStatus.Paused, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var client = new Mock<IExchangeClient>();
        client.SetupGet(c => c.IsFutures).Returns(true);
        client.Setup(c => c.GetOpenOrdersAsync(null, It.IsAny<CancellationToken>())).ReturnsAsync([]);

        var router = new RoutingExchangeClient(
            environment,
            new Dictionary<TradingMode, IExchangeClient> { [TradingMode.FuturesTestnet] = client.Object },
            new ApiHealthMonitor());

        var executor = new Mock<TradeExecutor>(
            client.Object, null!, dataService.Object, NullLogger<TradeExecutor>.Instance, environment, null!, null!);
        executor
            .Setup(e => e.ExecuteEmergencyCloseAsync(
                "BTCUSDT", OrderSide.Sell, 1.5m, 60000m, It.IsAny<CancellationToken>(), TradingMode.FuturesTestnet))
            .ReturnsAsync(new TradeResult { Success = true });
        executor
            .Setup(e => e.ExecuteEmergencyCloseAsync(
                "ETHUSDT", OrderSide.Buy, 2.0m, 3000m, It.IsAny<CancellationToken>(), TradingMode.FuturesTestnet))
            .ReturnsAsync(new TradeResult { Success = false, ErrorMessage = "模拟交易所拒绝" });

        var conditionOrderService = new ExchangeConditionOrderService(
            strategyService.Object, executor.Object, dataService.Object, router, environment,
            NullLogger<ExchangeConditionOrderService>.Instance);

        var portfolioService = new Mock<CryptoPortfolioService>(
            null!, null!, null!, null!, null!, NullLogger<CryptoPortfolioService>.Instance);
        portfolioService
            .Setup(p => p.GetCurrentPositionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                new PositionInfo { Symbol = "BTCUSDT", Quantity = 1.5m, CurrentPrice = 60000m, IsShort = false },
                new PositionInfo { Symbol = "ETHUSDT", Quantity = 2.0m, CurrentPrice = 3000m, IsShort = true }
            ]);

        var haltState = new TradingHaltState(dataService.Object, NullLogger<TradingHaltState>.Instance);
        var alertCenter = new Mock<IAlertCenterService>();
        var liquidationEvaluator = new LiquidationRiskEvaluator(
            client.Object, dataService.Object, alertCenter.Object, NullLogger<LiquidationRiskEvaluator>.Instance);
        var riskAlertEvaluator = new RiskAlertEvaluator(
            alertCenter.Object, dataService.Object, portfolioService.Object, liquidationEvaluator,
            NullLogger<RiskAlertEvaluator>.Instance);

        var emergencyStop = new EmergencyStopService(
            monitorProvider.Object,
            strategyService.Object,
            conditionOrderService,
            portfolioService.Object,
            executor.Object,
            environment,
            haltState,
            riskAlertEvaluator,
            NullLogger<EmergencyStopService>.Instance);

        var closeResult = await emergencyStop.CloseAllPositionsAsync("极端行情紧急平仓");

        Assert.IsTrue(haltState.IsHalted, "紧急平仓后必须处于全局停机状态");
        Assert.AreEqual(1, closeResult.ClosedSymbolCount, "成功的标的应计数为 1");
        Assert.AreEqual(1, closeResult.Failures.Count, "失败的标的应被收集到 Failures");
        Assert.IsFalse(closeResult.Success, "存在平仓失败标的时整体 Success 应为 false 以便 UI 醒目提示");
        strategyService.Verify(
            s => s.UpdateStrategyStatusAsync(It.IsAny<string>(), StrategyStatus.Paused, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
