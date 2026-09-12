using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Services.Trading.Exchanges;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 交易所侧保护性条件单测试：
/// 覆盖缺失补挂、孤儿单撤销、参数漂移重建、追踪未激活不挂单、
/// 现货短路，以及 TradeExecutor 条件单规格映射与 Pending 记录落库。
/// </summary>
[TestClass]
public sealed class ExchangeConditionOrderServiceTest
{
    private const string Symbol = "BTCUSDT";

    private sealed class ThrowingMarketMonitorProvider : IMarketMonitorProvider
    {
        public MarketMonitor GetMonitor()
            => throw new InvalidOperationException("条件单测试中不应创建 MarketMonitor");
    }

    private static (
        ExchangeConditionOrderService Service,
        Mock<IExchangeClient> Client,
        Mock<TradeExecutor> Executor,
        Mock<TradingStrategyService> Strategies,
        Mock<TradingDataService> Data) CreateContext(bool futures)
    {
        var mode = futures ? CryptoTradingMode.BinanceFuturesTestnet : CryptoTradingMode.BinanceSpotDemo;
        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(s => s.CurrentSetting).Returns(new UserSetting { CryptoTradingMode = mode });

        var environment = new TradingEnvironmentService(
            settingService.Object,
            new ThrowingMarketMonitorProvider(),
            NullLogger<TradingEnvironmentService>.Instance);

        var client = new Mock<IExchangeClient>();
        client.SetupGet(c => c.IsFutures).Returns(futures);

        var router = new RoutingExchangeClient(
            environment,
            new Dictionary<CryptoTradingMode, IExchangeClient> { [mode] = client.Object });

        var data = new Mock<TradingDataService>(environment, NullLogger<TradingDataService>.Instance);
        var strategies = new Mock<TradingStrategyService>(data.Object);
        var executor = new Mock<TradeExecutor>(
            client.Object, null!, data.Object, NullLogger<TradeExecutor>.Instance, environment, null!)
        { CallBase = true };

        var service = new ExchangeConditionOrderService(
            strategies.Object, executor.Object, data.Object, router, environment,
            NullLogger<ExchangeConditionOrderService>.Instance);

        return (service, client, executor, strategies, data);
    }

    private static TradingStrategy CreateStopLossStrategy(string id = "stoploss-1") => new()
    {
        Id = id,
        Symbol = Symbol,
        Type = StrategyType.StopLoss,
        Status = StrategyStatus.Active,
        Side = OrderSide.Sell,
        TriggerPrice = 90m,
        Quantity = 1m
    };

    private static ExchangeOrderResult CreateManagedOrder(string strategyId, decimal stopPrice = 90m) => new()
    {
        Symbol = Symbol,
        OrderId = "999",
        ClientOrderId = TradeExecutor.BuildConditionOrderClientOrderId(strategyId),
        Status = "NEW",
        Type = "STOP_MARKET",
        StopPrice = stopPrice,
        RequestedQty = 1m
    };

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Futures_MissingProtectiveOrder_ShouldPlace_AndStoreFingerprint()
    {
        var ctx = CreateContext(futures: true);
        var strategy = CreateStopLossStrategy();
        ctx.Strategies
            .Setup(s => s.GetStrategiesByStatusAsync(StrategyStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([strategy]);
        ctx.Client
            .Setup(c => c.GetOpenOrdersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var placedRecord = new TradeRecord { ExchangeOrderId = 777, Status = TradeRecordStatus.Pending };
        ctx.Executor
            .Setup(e => e.PlaceConditionOrderAsync(It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeResult { Success = true, Record = placedRecord });

        await ctx.Service.ReconcileAsync(CancellationToken.None);

        ctx.Executor.Verify(
            e => e.PlaceConditionOrderAsync(strategy, It.IsAny<CancellationToken>()),
            Times.Once, "缺失的保护策略条件单应补挂");
        ctx.Data.Verify(
            d => d.UpdateStrategyConditionOrderFingerprintAsync(
                strategy.Id, ExchangeConditionOrderService.BuildFingerprint(strategy), It.IsAny<CancellationToken>()),
            Times.Once, "挂单成功后应回写参数指纹");
        Assert.IsTrue(ctx.Service.HasLiveOrder(strategy.Id), "挂单成功后在途映射应可见");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Futures_OrphanOrder_ShouldCancel()
    {
        var ctx = CreateContext(futures: true);
        var orphan = CreateManagedOrder("deleted-strategy");
        ctx.Strategies
            .Setup(s => s.GetStrategiesByStatusAsync(StrategyStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        ctx.Client
            .Setup(c => c.GetOpenOrdersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([orphan]);

        await ctx.Service.ReconcileAsync(CancellationToken.None);

        ctx.Client.Verify(
            c => c.CancelOrderAsync(Symbol, orphan.OrderId, It.IsAny<CancellationToken>()),
            Times.Once, "非活跃策略的残留条件单应作为孤儿单撤销");
        Assert.IsFalse(ctx.Service.HasLiveOrder("deleted-strategy"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Futures_MatchedOrder_SameFingerprint_ShouldSkipPlace()
    {
        var ctx = CreateContext(futures: true);
        var strategy = CreateStopLossStrategy();
        strategy.ConditionOrderFingerprint = ExchangeConditionOrderService.BuildFingerprint(strategy);
        var managedOrder = CreateManagedOrder(strategy.Id);
        ctx.Strategies
            .Setup(s => s.GetStrategiesByStatusAsync(StrategyStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([strategy]);
        ctx.Client
            .Setup(c => c.GetOpenOrdersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([managedOrder]);

        await ctx.Service.ReconcileAsync(CancellationToken.None);

        ctx.Executor.Verify(
            e => e.PlaceConditionOrderAsync(It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()),
            Times.Never, "挂单在位且指纹一致时不应重复挂单");
        ctx.Client.Verify(
            c => c.CancelOrderAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "挂单在位且指纹一致时不应撤单");
        Assert.IsTrue(ctx.Service.HasLiveOrder(strategy.Id), "在途挂单应记入映射供客户端执行跳过");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Futures_FingerprintDrift_ShouldCancelAndReorder()
    {
        var ctx = CreateContext(futures: true);
        var strategy = CreateStopLossStrategy();
        strategy.ConditionOrderFingerprint = "Stale|Fingerprint";
        var managedOrder = CreateManagedOrder(strategy.Id);
        ctx.Strategies
            .Setup(s => s.GetStrategiesByStatusAsync(StrategyStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([strategy]);
        ctx.Client
            .Setup(c => c.GetOpenOrdersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([managedOrder]);
        ctx.Executor
            .Setup(e => e.PlaceConditionOrderAsync(It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TradeResult { Success = true, Record = new TradeRecord { ExchangeOrderId = 888 } });

        await ctx.Service.ReconcileAsync(CancellationToken.None);

        ctx.Client.Verify(
            c => c.CancelOrderAsync(Symbol, managedOrder.OrderId, It.IsAny<CancellationToken>()),
            Times.Once, "参数漂移时应撤销旧条件单");
        ctx.Executor.Verify(
            e => e.PlaceConditionOrderAsync(strategy, It.IsAny<CancellationToken>()),
            Times.Once, "参数漂移撤旧后应按新参数重挂");
        ctx.Data.Verify(
            d => d.UpdateStrategyConditionOrderFingerprintAsync(
                strategy.Id, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Futures_TrailingStop_NotActivated_ShouldNotPlace()
    {
        var ctx = CreateContext(futures: true);
        var strategy = new TradingStrategy
        {
            Id = "trailing-1",
            Symbol = Symbol,
            Type = StrategyType.TrailingStop,
            Status = StrategyStatus.Active,
            Side = OrderSide.Sell,
            Quantity = 1m,
            // TrailingPeakPrice 为空 = 追踪尚未激活
        };
        ctx.Strategies
            .Setup(s => s.GetStrategiesByStatusAsync(StrategyStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([strategy]);
        ctx.Client
            .Setup(c => c.GetOpenOrdersAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await ctx.Service.ReconcileAsync(CancellationToken.None);

        ctx.Executor.Verify(
            e => e.PlaceConditionOrderAsync(It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()),
            Times.Never, "追踪未激活时提前挂交易所追踪单会过早止损，不应挂单");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Spot_ShouldShortCircuit_WithoutPlacing()
    {
        var ctx = CreateContext(futures: false);
        ctx.Strategies
            .Setup(s => s.GetStrategiesByStatusAsync(StrategyStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync([CreateStopLossStrategy()]);

        await ctx.Service.ReconcileAsync(CancellationToken.None);

        ctx.Executor.Verify(
            e => e.PlaceConditionOrderAsync(It.IsAny<TradingStrategy>(), It.IsAny<CancellationToken>()),
            Times.Never, "现货无条件单语义，退出型策略由客户端评估兜底");
        ctx.Client.Verify(
            c => c.GetOpenOrdersAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "现货模式不应查询交易所挂单");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PlaceConditionOrderAsync_StopLoss_ShouldPlaceReduceOnlyStopMarket_AndSavePendingRecord()
    {
        var ctx = CreateContext(futures: true);
        var strategy = CreateStopLossStrategy();
        ctx.Client
            .Setup(c => c.PlaceOrderAsync(
                Symbol, OrderSide.Sell, OrderType.StopMarket, 1m,
                null, TradeExecutor.BuildConditionOrderClientOrderId(strategy.Id),
                true, null, 90m, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExchangeOrderResult { OrderId = "123", Status = "NEW" });

        var result = await ctx.Executor.Object.PlaceConditionOrderAsync(strategy);

        Assert.IsTrue(result.Success, "止损策略应成功挂出 StopMarket 条件单");
        Assert.IsNotNull(result.Record);
        Assert.AreEqual(TradeRecordStatus.Pending, result.Record.Status, "条件单挂出即为待成交");
        Assert.AreEqual(123, result.Record.ExchangeOrderId);
        ctx.Data.Verify(
            d => d.SaveTradeRecordAsync(It.Is<TradeRecord>(r =>
                r.StrategyId == strategy.Id && r.Status == TradeRecordStatus.Pending),
                It.IsAny<CancellationToken>()),
            Times.Once, "挂单成功应落 Pending 交易记录供既有对账管线感知成交");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PlaceConditionOrderAsync_TakeProfitBuySide_ShouldReject()
    {
        var ctx = CreateContext(futures: true);
        var strategy = CreateStopLossStrategy();
        strategy.Type = StrategyType.TakeProfit;
        strategy.Side = OrderSide.Buy;

        var result = await ctx.Executor.Object.PlaceConditionOrderAsync(strategy);

        Assert.IsFalse(result.Success, "止盈 Buy 侧为限价建仓语义，不属于保护性条件单");
        ctx.Client.Verify(
            c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PlaceConditionOrderAsync_MissingTriggerPrice_ShouldRejectWithoutExchangeCall()
    {
        var ctx = CreateContext(futures: true);
        var strategy = CreateStopLossStrategy();
        strategy.TriggerPrice = 0;

        var result = await ctx.Executor.Object.PlaceConditionOrderAsync(strategy);

        Assert.IsFalse(result.Success, "缺少触发价的止损策略无法挂条件单");
        ctx.Client.Verify(
            c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never, "规格校验失败不应触达交易所");
    }
}
