using System.Net.Http;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// TradeExecutor 下单韧性与确认流测试：网络重试耗尽后的幂等回查收敛、
/// 人工确认事件、告警联动门、交易模式切换复检与紧急平仓真实路径。
/// </summary>
[TestClass]
public sealed class TradeExecutorResilienceTest
{
    private const string Symbol = "BTCUSDT";
    private OrderLedgerFixture? _ledger;
    [TestCleanup] public void Cleanup() => _ledger?.Dispose();

    private static Mock<IExchangeClient> CreateSpotClient()
    {
        var client = new Mock<IExchangeClient>();
        client.SetupGet(c => c.IsFutures).Returns(false);
        client.Setup(c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeOrderResult
            {
                OrderId = "1001",
                Status = "FILLED",
                RequestedQty = 0.5m,
                ExecutedQty = 0.5m,
                Price = 100m
            });
        return client;
    }

    private static Mock<TradingDataService> CreateData()
    {
        var data = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        data.Setup(d => d.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskConfig { MaxSlippagePercent = 0 });
        data.Setup(d => d.GetOpenPositionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreatePosition(1m)]);
        data.Setup(d => d.SaveTradeRecordAsync(It.IsAny<TradeRecord>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        data.Setup(d => d.OpenPositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        data.Setup(d => d.ClosePositionFifoAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>(), It.IsAny<PositionSide>()))
            .ReturnsAsync(0m);
        data.Setup(d => d.UpdateDailyStatsAsync(
                It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        data.Setup(d => d.UpdateTradeRecordRealizedPnlAsync(
                It.IsAny<string>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return data;
    }

    private static Position CreatePosition(decimal remainingQuantity) => new()
    {
        Symbol = Symbol,
        Side = PositionSide.Long,
        Quantity = remainingQuantity,
        ClosedQuantity = 0m
    };

    private static Mock<RiskManager> CreateRisk(Func<RiskCheckResult> result)
    {
        var risk = new Mock<RiskManager>(null!, null!, null!, null!, null!, NullLogger<RiskManager>.Instance, null!, null!);
        risk.Setup(r => r.ValidateOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<OrderType>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result());
        return risk;
    }

    private TradeExecutor CreateExecutor(
        Mock<IExchangeClient> client, Mock<RiskManager> risk, Mock<TradingDataService> data,
        TradingEnvironmentService? environment = null, IAlertGate? alertGate = null)
    {
        _ledger = new OrderLedgerFixture(data);
        var executor = new TradeExecutor(
            client.Object, risk.Object, data.Object,
            NullLogger<TradeExecutor>.Instance, environment, alertGate);
        return executor;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LostResponse_RecoveryByClientOrderId_ShouldSettleWithoutDuplicateOrder()
    {
        // 下单响应丢失但订单已在交易所落地：按同一幂等 ID 回查命中后必须照常落库，不得重新下单
        var client = CreateSpotClient();
        client.Setup(c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟响应丢失"));

        var recovered = new ExchangeOrderResult
        {
            OrderId = "900",
            Status = "FILLED",
            RequestedQty = 0.5m,
            ExecutedQty = 0.5m,
            Price = 101m
        };
        client.Setup(c => c.GetOrderByClientOrderIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(recovered);

        var data = CreateData();

        using var executor = CreateExecutor(client, CreateRisk(RiskCheckResult.Pass), data);
        await _ledger!.Positions.OpenPositionAsync(new Position { Side = PositionSide.Long, Symbol = Symbol, Quantity = 0.5m, EntryPrice = 81m });

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Limit, 0.5m, 100m);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        var persisted = (await _ledger.Records.GetTradeRecordsAsync()).Single();
        Assert.IsNotNull(persisted, "回查命中的订单必须照常落库");
        Assert.AreEqual(900L, persisted!.ExchangeOrderId);
        Assert.AreEqual(0.5m, persisted.ExecutedQty);
        Assert.AreEqual(TradeRecordStatus.Filled, persisted.Status);
        Assert.AreEqual(10m, persisted.RealizedPnl);
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once, "结果不确定时只回查，不再次提交");
        client.Verify(c => c.GetOrderByClientOrderIdAsync(
            Symbol, It.Is<string>(id => id.StartsWith("MA", StringComparison.Ordinal)),
            It.IsAny<CancellationToken>()),
            Times.Once, "收敛查询必须按同一次调用生成的幂等 ID 回查");
        Assert.AreEqual(0, (await _ledger.Records.GetUnsettledTradeRecordsAsync()).Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RecoveryQueryFails_ShouldReturnOrderStateUnknown_WithoutReordering()
    {
        var client = CreateSpotClient();
        client.Setup(c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟网络中断"));
        client.Setup(c => c.GetOrderByClientOrderIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟回查失败"));

        using var executor = CreateExecutor(client, CreateRisk(RiskCheckResult.Pass), CreateData());

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Limit, 0.5m, 100m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.OrderStateUnknown, result.FailureCategory);
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "停止自动重试");
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "幂等ID=MA");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RecoveryQueryOrderNotFound_ShouldFreezeWithoutResubmission()
    {
        var client = CreateSpotClient();
        client.Setup(c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟网络中断"));
        client.Setup(c => c.GetOrderByClientOrderIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OrderNotFoundException("订单不存在"));

        using var executor = CreateExecutor(client, CreateRisk(RiskCheckResult.Pass), CreateData());

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Limit, 0.5m, 100m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.OrderStateUnknown, result.FailureCategory);
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task BusinessError_ShouldNotRetry()
    {
        var client = CreateSpotClient();
        client.Setup(c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FriendlyException("账户余额不足"));

        using var executor = CreateExecutor(client, CreateRisk(RiskCheckResult.Pass), CreateData());

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Limit, 0.5m, 100m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Other, result.FailureCategory,
            "业务错误重试无意义，应归为 Other 由调用方按冷却策略处理");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Exactly(1), "非瞬时业务错误不得进入网络重试");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ConfirmationRequested_WhenSubscriberApproves_ShouldExecute()
    {
        var data = CreateData();
        using var executor = CreateExecutor(
            CreateSpotClient(),
            CreateRisk(() => RiskCheckResult.RequireConfirmation("订单金额超过确认阈值")),
            data);

        executor.ConfirmationRequested += (_, _, _, _, _) => Task.FromResult(true);

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.5m, 100m);

        Assert.IsTrue(result.Success, result.ErrorMessage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ConfirmationRequested_WhenSubscriberRejects_ShouldNotExecute()
    {
        var client = CreateSpotClient();
        using var executor = CreateExecutor(
            client,
            CreateRisk(() => RiskCheckResult.RequireConfirmation("订单金额超过确认阈值")),
            CreateData());

        executor.ConfirmationRequested += (_, _, _, _, _) => Task.FromResult(false);

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.5m, 100m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Rejected, result.FailureCategory);
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "用户拒绝交易");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ConfirmationRequested_WithoutSubscriber_ShouldReject()
    {
        // 未订阅确认事件时保持"直接拒绝"语义，不得默认放行需要确认的订单
        var client = CreateSpotClient();
        using var executor = CreateExecutor(
            client,
            CreateRisk(() => RiskCheckResult.RequireConfirmation("订单金额超过确认阈值")),
            CreateData());

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.5m, 100m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Rejected, result.FailureCategory);
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "需人工确认");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task AlertGate_WhenGated_ShouldForceConfirmation_AndExemptExitOrders()
    {
        var gate = new Mock<IAlertGate>();
        gate.Setup(g => g.IsGated(MarketType.Crypto, It.IsAny<string>())).Returns(true);

        var client = CreateSpotClient();
        var data = CreateData();
        using var executor = CreateExecutor(client, CreateRisk(RiskCheckResult.Pass), data, alertGate: gate.Object);

        var confirmationCount = 0;
        string? capturedReason = null;
        executor.ConfirmationRequested += (_, _, _, _, reason) =>
        {
            confirmationCount++;
            capturedReason = reason;
            return Task.FromResult(true);
        };

        // 1. 开仓信号：确认级告警触发期间强制人工确认
        var openResult = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.5m, 100m);
        Assert.IsTrue(openResult.Success, openResult.ErrorMessage);
        Assert.AreEqual(1, confirmationCount, "告警联动门必须把开仓信号升级为人工确认");
        StringAssert.Contains(capturedReason ?? string.Empty, "紧急告警");

        // 2. 退出型触发豁免：告警期间保护性平仓不能被确认流程挡住
        var exitResult = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Market, 0.5m, 100m, requireClose: true);
        Assert.IsTrue(exitResult.Success, exitResult.ErrorMessage);
        Assert.AreEqual(1, confirmationCount, "requireClose 的保护性平仓必须豁免告警联动门");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ModeSwitchDuringConfirmation_ShouldRejectOrder_NeverRouteToNewEnvironment()
    {
        var userSettingService = new Mock<IUserSettingService>();
        userSettingService.SetupGet(s => s.CurrentSetting)
            .Returns(new UserSetting { CryptoTradingMode = TradingMode.SpotDemo });
        userSettingService.Setup(s => s.UpdateSetting(It.IsAny<Action<UserSetting>>()));
        var monitor = new Mock<MarketMonitor>();
        monitor.SetupGet(m => m.IsRunning).Returns(false);
        var monitorProvider = new Mock<IMarketMonitorProvider>();
        monitorProvider.Setup(p => p.GetMonitor()).Returns(monitor.Object);
        var environment = new TradingEnvironmentService(
            userSettingService.Object, monitorProvider.Object,
            NullLogger<TradingEnvironmentService>.Instance);

        var client = CreateSpotClient();
        var data = CreateData();
        using var executor = CreateExecutor(
            client,
            CreateRisk(() => RiskCheckResult.RequireConfirmation("订单金额超过确认阈值")),
            data, environment);

        // 确认等待期间用户切换到实盘：本笔订单必须整体取消，绝不能路由到新环境
        executor.ConfirmationRequested += async (_, _, _, _, _) =>
        {
            await environment.ApplyModeAsync(TradingMode.LiveSpot);
            return true;
        };

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.5m, 100m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Rejected, result.FailureCategory);
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "交易模式在确认期间被切换");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never, "模式已切换的订单绝不能落到新环境下单");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task EmergencyClose_BypassesRiskButRecords_WithDedicatedStrategyId()
    {
        var client = CreateSpotClient();
        var risk = CreateRisk(RiskCheckResult.Pass);
        TradeRecord? persisted = null;
        var data = CreateData();
        data.Setup(d => d.SaveTradeRecordAsync(It.IsAny<TradeRecord>(), It.IsAny<CancellationToken>()))
            .Callback<TradeRecord, CancellationToken>((record, _) => persisted = record)
            .Returns(Task.CompletedTask);

        using var executor = CreateExecutor(client, risk, data);

        var result = await executor.ExecuteEmergencyCloseAsync(
            Symbol, OrderSide.Sell, 0.5m, 100m);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        persisted = (await _ledger!.Records.GetTradeRecordsAsync()).Single();
        Assert.IsNotNull(persisted, "紧急平仓必须照常落库（成交记录与持仓回写不走捷径）");
        Assert.AreEqual(EmergencyStopService.EmergencyCloseStrategyId, persisted!.StrategyId);
        StringAssert.Contains(persisted.AIReasoning ?? string.Empty, "紧急停止");
        risk.Verify(r => r.ValidateOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
            It.IsAny<OrderType>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "紧急平仓是 kill switch 专用通道，绕过常规风控（避免等确认把用户暴露在风险中）");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task EmergencyClose_WhenModeMismatch_ShouldAbort()
    {
        var userSettingService = new Mock<IUserSettingService>();
        userSettingService.SetupGet(s => s.CurrentSetting)
            .Returns(new UserSetting { CryptoTradingMode = TradingMode.SpotDemo });
        var monitor = new Mock<MarketMonitor>();
        monitor.SetupGet(m => m.IsRunning).Returns(false);
        var monitorProvider = new Mock<IMarketMonitorProvider>();
        monitorProvider.Setup(p => p.GetMonitor()).Returns(monitor.Object);
        var environment = new TradingEnvironmentService(
            userSettingService.Object, monitorProvider.Object,
            NullLogger<TradingEnvironmentService>.Instance);

        var client = CreateSpotClient();
        var data = CreateData();
        using var executor = CreateExecutor(client, CreateRisk(RiskCheckResult.Pass), data, environment);

        // 调用方按 LiveSpot 发起，但当前环境已切回 SpotDemo：本笔平仓必须中止
        var result = await executor.ExecuteEmergencyCloseAsync(
            Symbol, OrderSide.Sell, 0.5m, 100m, expectedMode: TradingMode.LiveSpot);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Rejected, result.FailureCategory);
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "紧急平仓期间交易模式被切换");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
