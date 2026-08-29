using System.Text.Json;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using MarketAssistant.Services.Trading;
using MarketAssistant.Services.Trading.Exchanges;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// StrategyEngine 触发分支行为测试：覆盖基础止损/止盈、追踪止损参数回退、
/// 网格基准落库与穿越触发、DCA 买入与出场（止盈清仓/止损暂停）。
/// </summary>
[TestClass]
public sealed class StrategyEngineTriggerTest
{
    private const string Symbol = "BTCUSDT";

    /// <summary>
    /// 触发评估路径不应创建 MarketMonitor，一旦创建立即失败。
    /// </summary>
    private sealed class ThrowingMarketMonitorProvider : IMarketMonitorProvider
    {
        public MarketMonitor GetMonitor()
            => throw new InvalidOperationException("触发评估测试中不应创建 MarketMonitor");
    }

    private static (StrategyEngine Engine, Mock<TradingDataService> Data, Mock<TradingStrategyService> Strategies) CreateEngine()
    {
        var settingService = new Mock<IUserSettingService>();
        settingService.SetupGet(service => service.CurrentSetting).Returns(new UserSetting());

        var environment = new TradingEnvironmentService(
            settingService.Object,
            new ThrowingMarketMonitorProvider(),
            NullLogger<TradingEnvironmentService>.Instance);

        var data = new Mock<TradingDataService>(environment, NullLogger<TradingDataService>.Instance);
        var strategies = new Mock<TradingStrategyService>(data.Object);
        // RoutingExchangeClient 为 sealed，使用真实实例：触发评估路径不会调用交易所客户端
        var exchange = new RoutingExchangeClient(
            environment, new Dictionary<CryptoTradingMode, IExchangeClient>());

        var engine = new StrategyEngine(
            data.Object, strategies.Object, exchange, environment,
            NullLogger<StrategyEngine>.Instance);
        return (engine, data, strategies);
    }

    private static void SetupStrategies(
        (StrategyEngine Engine, Mock<TradingDataService> Data, Mock<TradingStrategyService> Strategies) ctx,
        params TradingStrategy[] strategyList)
    {
        ctx.Strategies
            .Setup(service => service.GetStrategiesByStatusAsync(StrategyStatus.Active, It.IsAny<CancellationToken>()))
            .ReturnsAsync(strategyList.ToList());
    }

    private static TradingStrategy CreateStrategy(StrategyType type, OrderSide side, string? customParams = null)
        => new()
        {
            Id = $"strategy-{type}-{side}",
            Symbol = Symbol,
            Type = type,
            Side = side,
            Status = StrategyStatus.Active,
            Quantity = 1m,
            CustomParams = customParams
        };

    [TestMethod]
    [TestCategory("Unit")]
    public async Task StopLoss_SellSide_TriggersBelowTriggerPrice()
    {
        var ctx = CreateEngine();
        var strategy = CreateStrategy(StrategyType.StopLoss, OrderSide.Sell);
        strategy.TriggerPrice = 95m;
        SetupStrategies(ctx, strategy);

        var above = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 96m);
        var below = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 94m);

        Assert.AreEqual(0, above.Count, "高于触发价时不应触发卖出止损");
        Assert.AreEqual(1, below.Count, "跌破触发价应触发止损");
        Assert.AreEqual(OrderSide.Sell, below[0].Side);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task TakeProfit_SellSide_TriggersAboveTriggerPrice()
    {
        var ctx = CreateEngine();
        var strategy = CreateStrategy(StrategyType.TakeProfit, OrderSide.Sell);
        strategy.TriggerPrice = 105m;
        SetupStrategies(ctx, strategy);

        var below = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 104m);
        var above = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 106m);

        Assert.AreEqual(0, below.Count, "低于触发价时不应触发止盈");
        Assert.AreEqual(1, above.Count, "涨破触发价应触发止盈");
        Assert.AreEqual(OrderSide.Sell, above[0].Side);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task TrailingStop_MissingParams_FallsBackToProfileDefault_AndTriggers()
    {
        var ctx = CreateEngine();
        // 仅含风险档案，缺少 trailingPercent/activationPrice → 按稳健档回退 5%，立即激活
        var strategy = CreateStrategy(StrategyType.TrailingStop, OrderSide.Sell, """{"riskProfile":"Balanced"}""");
        SetupStrategies(ctx, strategy);

        var first = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 100m);
        var second = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 94m);

        Assert.AreEqual(0, first.Count, "首次评估仅记录峰值，不应触发");
        Assert.AreEqual(1, second.Count, "从峰值回撤 6% > 5% 回退比例应触发");
        Assert.AreEqual(OrderSide.Sell, second[0].Side);
        ctx.Data.Verify(
            data => data.UpdateStrategyTrailingPeakAsync(strategy.Id, 100m, It.IsAny<CancellationToken>()),
            Times.Once, "峰值 100 应被持久化");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Grid_FirstEvaluation_PersistsBaselineIndex_WithoutTrigger()
    {
        var ctx = CreateEngine();
        var gridParams = new GridTradingParams
        {
            UpperPrice = 110m,
            LowerPrice = 90m,
            GridCount = 10,
            QuantityPerGrid = 1m,
            LastTriggeredIndex = -1
        };
        var strategy = CreateStrategy(
            StrategyType.GridTrading, OrderSide.Buy, JsonSerializer.Serialize(gridParams));
        SetupStrategies(ctx, strategy);

        string? persisted = null;
        ctx.Data
            .Setup(data => data.UpdateStrategyCustomParamsAsync(strategy.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, customParams, _) => persisted = customParams)
            .Returns(Task.CompletedTask);

        var triggered = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 100m);

        Assert.AreEqual(0, triggered.Count, "首次评估只记录基准，不应触发交易");
        Assert.IsNotNull(persisted, "基准索引必须立即落库（修复重启后基准丢失缺陷）");
        var saved = JsonSerializer.Deserialize<GridTradingParams>(persisted!);
        Assert.AreEqual(5, saved!.LastTriggeredIndex, "价格 100 位于 90-110 十格网格的第 5 格");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task Grid_CrossingLines_TriggersBuyBelow_AndSellAbove()
    {
        var ctx = CreateEngine();
        var gridParams = new GridTradingParams
        {
            UpperPrice = 110m,
            LowerPrice = 90m,
            GridCount = 10,
            QuantityPerGrid = 1m,
            LastTriggeredIndex = 5
        };
        var strategy = CreateStrategy(
            StrategyType.GridTrading, OrderSide.Buy, JsonSerializer.Serialize(gridParams));
        SetupStrategies(ctx, strategy);

        string? persistedAfterDown = null;
        ctx.Data
            .Setup(data => data.UpdateStrategyCustomParamsAsync(strategy.Id, It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, customParams, _) => persistedAfterDown = customParams)
            .Returns(Task.CompletedTask);

        var down = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 94m);
        // 引擎会原地修改策略对象（文档化副作用），上穿评估会覆盖 Side，需先快照下穿结果
        var downCount = down.Count;
        var downSide = downCount > 0 ? down[0].Side : default(OrderSide);
        var downQty = downCount > 0 ? down[0].Quantity : 0m;
        var up = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 106m);

        Assert.AreEqual(1, downCount, $"下穿应触发一次，落库参数: {persistedAfterDown}");
        Assert.AreEqual(OrderSide.Buy, downSide, "下穿网格线应买入");
        Assert.AreEqual(1m, downQty);
        Assert.AreEqual(1, up.Count);
        Assert.AreEqual(OrderSide.Sell, up[0].Side, "上穿网格线应卖出");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task DCA_NoPosition_TriggersBuyAfterInterval()
    {
        var ctx = CreateEngine();
        var dcaParams = new DCAParams
        {
            AmountPerInterval = 100m,
            IntervalSeconds = 1,
            TakeProfitPercent = 10m,
            StopLossPercent = 20m
        };
        var strategy = CreateStrategy(StrategyType.DCA, OrderSide.Buy, JsonSerializer.Serialize(dcaParams));
        strategy.LastTriggeredAt = DateTime.UtcNow.AddHours(-1);
        SetupStrategies(ctx, strategy);

        ctx.Data
            .Setup(data => data.GetOpenPositionsAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var triggered = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 100m);

        Assert.AreEqual(1, triggered.Count, "无持仓且间隔已过应触发买入");
        Assert.AreEqual(OrderSide.Buy, triggered[0].Side);
        Assert.AreEqual(1m, triggered[0].Quantity, "100 USDT ÷ 100 价格 = 1 BTC");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task DCA_TakeProfitReached_SellsFullRemainingPosition()
    {
        var ctx = CreateEngine();
        var dcaParams = new DCAParams
        {
            AmountPerInterval = 100m,
            IntervalSeconds = 1,
            TakeProfitPercent = 10m,
            StopLossPercent = 20m
        };
        var strategy = CreateStrategy(StrategyType.DCA, OrderSide.Buy, JsonSerializer.Serialize(dcaParams));
        strategy.LastTriggeredAt = DateTime.UtcNow;
        SetupStrategies(ctx, strategy);

        ctx.Data
            .Setup(data => data.GetOpenPositionsAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Position { Symbol = Symbol, Side = PositionSide.Long, Quantity = 2m, ClosedQuantity = 0.5m }]);
        ctx.Data
            .Setup(data => data.GetOpenPositionAvgEntryPriceAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100m);

        // 115 >= 100 × (1 + 10%) → 止盈清仓剩余 1.5；出场评估不受定投间隔限制
        var triggered = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 115m);

        Assert.AreEqual(1, triggered.Count);
        Assert.AreEqual(OrderSide.Sell, triggered[0].Side, "止盈应卖出");
        Assert.AreEqual(1.5m, triggered[0].Quantity, "应卖出剩余持仓（2 - 0.5）");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task DCA_StopLossReached_WithoutSellOut_PausesStrategy()
    {
        var ctx = CreateEngine();
        var dcaParams = new DCAParams
        {
            AmountPerInterval = 100m,
            IntervalSeconds = 1,
            TakeProfitPercent = 10m,
            StopLossPercent = 20m,
            StopLossSellOut = false
        };
        var strategy = CreateStrategy(StrategyType.DCA, OrderSide.Buy, JsonSerializer.Serialize(dcaParams));
        strategy.LastTriggeredAt = DateTime.UtcNow;
        SetupStrategies(ctx, strategy);

        ctx.Data
            .Setup(data => data.GetOpenPositionsAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Position { Symbol = Symbol, Side = PositionSide.Long, Quantity = 1m, ClosedQuantity = 0m }]);
        ctx.Data
            .Setup(data => data.GetOpenPositionAvgEntryPriceAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100m);

        var triggered = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 79m);

        Assert.AreEqual(0, triggered.Count, "保守止损动作是暂停策略而非卖出");
        ctx.Strategies.Verify(
            strategies => strategies.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Paused, It.IsAny<CancellationToken>()),
            Times.Once, "触及止损线应暂停定投");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task DCA_StopLossReached_WithSellOut_SellsFullPosition()
    {
        var ctx = CreateEngine();
        var dcaParams = new DCAParams
        {
            AmountPerInterval = 100m,
            IntervalSeconds = 1,
            TakeProfitPercent = 10m,
            StopLossPercent = 20m,
            StopLossSellOut = true
        };
        var strategy = CreateStrategy(StrategyType.DCA, OrderSide.Buy, JsonSerializer.Serialize(dcaParams));
        strategy.LastTriggeredAt = DateTime.UtcNow;
        SetupStrategies(ctx, strategy);

        ctx.Data
            .Setup(data => data.GetOpenPositionsAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new Position { Symbol = Symbol, Side = PositionSide.Long, Quantity = 1m, ClosedQuantity = 0m }]);
        ctx.Data
            .Setup(data => data.GetOpenPositionAvgEntryPriceAsync(Symbol, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100m);

        var triggered = await ctx.Engine.EvaluateAndUpdateStrategiesAsync(Symbol, 79m);

        Assert.AreEqual(1, triggered.Count);
        Assert.AreEqual(OrderSide.Sell, triggered[0].Side);
        Assert.AreEqual(1m, triggered[0].Quantity);
    }
}