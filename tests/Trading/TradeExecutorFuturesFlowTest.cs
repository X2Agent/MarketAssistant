using System.Net.Http;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Trading;

/// <summary>
/// TradeExecutor 合约执行语义测试：开/平仓判定驱动的 reduceOnly 推导、
/// 杠杆设置 fail-closed、退出型触发无持仓拒单，以及现货保护性 OCO 挂单。
/// </summary>
[TestClass]
public sealed class TradeExecutorFuturesFlowTest
{
    private const string Symbol = "BTCUSDT";
    private OrderLedgerFixture? _ledger;
    [TestCleanup] public void Cleanup() => _ledger?.Dispose();

    private static Mock<IExchangeClient> CreateFuturesClient(params ExchangePosition[] positions)
    {
        var client = new Mock<IExchangeClient>();
        client.SetupGet(c => c.IsFutures).Returns(true);
        client.Setup(c => c.GetPositionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([.. positions]);
        return client;
    }

    private static Mock<IExchangeClient> CreateSpotClient()
    {
        var client = new Mock<IExchangeClient>();
        client.SetupGet(c => c.IsFutures).Returns(false);
        return client;
    }

    private static Mock<TradingDataService> CreateData(int leverage = 10)
    {
        var data = new Mock<TradingDataService>(null!, NullLogger<TradingDataService>.Instance);
        data.Setup(d => d.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RiskConfig { FuturesLeverage = leverage });
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

    private static Mock<RiskManager> CreatePassingRisk()
    {
        var risk = new Mock<RiskManager>(null!, null!, null!, null!, null!, NullLogger<RiskManager>.Instance, null!, null!);
        risk.Setup(r => r.ValidateOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<decimal>(), It.IsAny<decimal>(),
                It.IsAny<OrderType>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RiskCheckResult.Pass());
        return risk;
    }

    private static ExchangeOrderResult FilledResult(decimal executedQty, decimal price = 100m) => new()
    {
        OrderId = "1001",
        Status = "FILLED",
        RequestedQty = executedQty,
        ExecutedQty = executedQty,
        Price = price
    };

    private TradeExecutor CreateExecutor(
        Mock<IExchangeClient> client, Mock<RiskManager> risk, Mock<TradingDataService> data)
    {
        if (client.Object.IsFutures) _ledger = new OrderLedgerFixture(data, futures: true);
        var executor = new TradeExecutor(
            client.Object, risk.Object, data.Object, NullLogger<TradeExecutor>.Instance);
        return executor;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task CloseLong_WithRequireClose_ShouldPassReduceOnly_AndSkipLeverage()
    {
        var client = CreateFuturesClient(new ExchangePosition { Symbol = Symbol, PositionAmt = 1m });
        client.Setup(c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FilledResult(0.5m, 101m));

        var risk = CreatePassingRisk();
        var data = CreateData();
        using var executor = CreateExecutor(client, risk, data);

        await _ledger!.Positions.OpenPositionAsync(new Position { Symbol = Symbol, Side = PositionSide.Long, Quantity = 1m, EntryPrice = 100m });
        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Market, 0.5m, 100m, requireClose: true);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        client.Verify(c => c.PlaceOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Market, 0.5m,
            It.IsAny<decimal?>(), It.IsAny<string?>(),
            It.Is<bool>(reduceOnly => reduceOnly),
            It.IsAny<string?>(), It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            "持多卖出必须以 reduceOnly=true 下单");
        client.Verify(c => c.SetLeverageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "平仓路径不得被杠杆问题阻塞");
        Assert.AreEqual(0.5m, (await _ledger.Positions.GetOpenPositionsAsync(Symbol)).Single().RemainingQuantity);
        Assert.AreEqual(0.5m, result.Record!.RealizedPnl);
        risk.Verify(r => r.ValidateOrderAsync(
            Symbol, OrderSide.Sell, 0.5m, 100m, OrderType.Market, It.IsAny<string?>(),
            It.Is<bool>(isExit => isExit), It.IsAny<CancellationToken>()),
            Times.Once, "requireClose 语义必须透传给风控（退出单豁免开仓侧闸门）");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task OpenNewLong_ShouldSetLeverageBeforeOrder_AndOpenFifoPosition()
    {
        var client = CreateFuturesClient();
        client.Setup(c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FilledResult(0.2m));

        Position? opened = null;
        var data = CreateData(leverage: 10);
        data.Setup(d => d.OpenPositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .Callback<Position, CancellationToken>((position, _) => opened = position)
            .Returns(Task.CompletedTask);

        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.2m, 100m);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        client.Verify(c => c.SetLeverageAsync(Symbol, 10, It.IsAny<CancellationToken>()),
            Times.Once, "合约开仓前必须按风控配置设置杠杆");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(),
            It.Is<bool>(reduceOnly => !reduceOnly),
            It.IsAny<string?>(), It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            "无持仓时买入是新开仓，reduceOnly 必须为 false");
        opened = (await _ledger!.Positions.GetOpenPositionsAsync(Symbol)).Single();
        Assert.IsNotNull(opened, "开仓成交必须写入本地 FIFO 持仓");
        Assert.AreEqual(PositionSide.Long, opened!.Side);
        Assert.AreEqual(0.2m, opened.Quantity);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RequireClose_WithoutCorrespondingPosition_ShouldReject_ToPreventReverseOpen()
    {
        var client = CreateFuturesClient();
        var data = CreateData();
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Market, 0.5m, 100m, requireClose: true);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Rejected, result.FailureCategory);
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "无对应方向持仓");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never, "退出型触发在无持仓时绝不能反向开出新仓");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PositionQueryFailure_ShouldRejectAsNetwork_FailClosed()
    {
        // 持仓查询失败时无法判定开/平仓：默认按开仓会把平仓单变成反向开仓（10x 风险敞口），必须中止
        var client = CreateFuturesClient();
        client.Setup(c => c.GetPositionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟网络中断"));

        var data = CreateData();
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Sell, OrderType.Market, 0.5m, 100m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Network, result.FailureCategory,
            "持仓查询失败属可恢复网络问题，应走短冷却重试而非停用策略");
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "无法确认 BTCUSDT 合约持仓状态");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SetLeverageFailure_ShouldRejectOpen_FailClosed()
    {
        // 杠杆设置失败仍下单会以交易所默认（常 20x）成交，UI 显示 10x 的静默偏差不可接受
        var client = CreateFuturesClient();
        client.Setup(c => c.SetLeverageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟杠杆接口异常"));

        var data = CreateData();
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.2m, 100m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Network, result.FailureCategory);
        StringAssert.Contains(result.ErrorMessage ?? string.Empty, "杠杆");
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never, "杠杆设置失败时绝不能以交易所默认杠杆下单");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LeverageConfigReadFailure_ShouldRejectOpenWithoutSettingLeverageOrOrdering()
    {
        var client = CreateFuturesClient();
        var data = CreateData();
        data.Setup(d => d.LoadRiskConfigAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new System.Text.Json.JsonException("损坏配置"));
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.2m, 100m);

        Assert.IsFalse(result.Success);
        client.Verify(c => c.SetLeverageAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
        client.Verify(c => c.PlaceOrderAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
            It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
            It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task OutOfRangeLeverageConfig_ShouldClampToExchangeMaximum()
    {
        var client = CreateFuturesClient();
        client.Setup(c => c.PlaceOrderAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<OrderType>(), It.IsAny<decimal>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<bool>(), It.IsAny<string?>(),
                It.IsAny<decimal?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FilledResult(0.2m));

        // 配置可能来自手工编辑的数据库，越界值必须钳制到交易所合法区间（1-125）
        var data = CreateData(leverage: 500);
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.ExecuteOrderAsync(
            Symbol, OrderSide.Buy, OrderType.Market, 0.2m, 100m);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        client.Verify(c => c.SetLeverageAsync(Symbol, 125, It.IsAny<CancellationToken>()),
            Times.Once, "越界杠杆配置必须钳制为 125");
    }

    private static TradingStrategy CreateAiStrategy(decimal stopLoss = 90m, decimal takeProfit = 120m) => new()
    {
        Id = "ai-oco",
        Symbol = Symbol,
        Type = StrategyType.AISignal,
        Status = StrategyStatus.Active,
        Side = OrderSide.Buy,
        Quantity = 0.5m,
        ExecutionCount = 1,
        StopLossPrice = stopLoss,
        TakeProfitPrice = takeProfit
    };

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotOco_ShouldPlaceBothLegsWithDeterministicIds_AndPersistPendingRecords()
    {
        var client = CreateSpotClient();
        client.Setup(c => c.PlaceSpotOcoAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<decimal>(),
                It.IsAny<string?>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<ExchangeOrderResult>)
            [
                new ExchangeOrderResult { OrderId = "500", Status = "NEW" },
                new ExchangeOrderResult { OrderId = "501", Status = "NEW" }
            ]);

        var data = CreateData();
        var saved = new List<TradeRecord>();
        data.Setup(d => d.SaveTradeRecordAsync(It.IsAny<TradeRecord>(), It.IsAny<CancellationToken>()))
            .Callback<TradeRecord, CancellationToken>((record, _) => saved.Add(record))
            .Returns(Task.CompletedTask);

        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.PlaceSpotProtectiveOcoAsync(CreateAiStrategy(), 0.5m);

        Assert.IsTrue(result.Success, result.Error);
        client.Verify(c => c.PlaceSpotOcoAsync(
            Symbol, OrderSide.Sell, 0.5m,
            "MCai-ocoS", 90m, "MCai-ocoT", 120m,
            It.IsAny<CancellationToken>()),
            Times.Once, "双腿必须以确定性 MC 幂等 ID 打包为单个 OCO（对账按腿匹配）");
        Assert.AreEqual(2, saved.Count, "两腿各落一条 Pending 交易记录");
        Assert.AreEqual(TradeRecordStatus.Pending, result.StopRecord!.Status);
        Assert.AreEqual(TradeRecordStatus.Pending, result.TakeProfitRecord!.Status);
        Assert.AreEqual(OrderType.StopLoss, result.StopRecord.OrderType);
        Assert.AreEqual(OrderType.TakeProfit, result.TakeProfitRecord.OrderType);
        Assert.AreEqual(OrderSide.Sell, result.StopRecord.Side, "多头入场的护栏腿必须是反向卖出");
        Assert.AreEqual(OrderSide.Sell, result.TakeProfitRecord.Side);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotOco_OnFuturesMode_ShouldReject()
    {
        var client = CreateFuturesClient();
        var data = CreateData();
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.PlaceSpotProtectiveOcoAsync(CreateAiStrategy(), 0.5m);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error ?? string.Empty, "仅现货");
        client.Verify(c => c.PlaceSpotOcoAsync(
            It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<decimal>(),
            It.IsAny<string?>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<decimal>(),
            It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotOco_MissingGuardrailPrice_ShouldRejectWithExplicitError()
    {
        var client = CreateSpotClient();
        var data = CreateData();
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var missingTakeProfit = await executor.PlaceSpotProtectiveOcoAsync(
            CreateAiStrategy(takeProfit: 0m), 0.5m);
        Assert.IsFalse(missingTakeProfit.Success);
        StringAssert.Contains(missingTakeProfit.Error ?? string.Empty, "止盈",
            "缺止盈价必须显式报错而非静默少挂一腿");

        var missingStopLoss = await executor.PlaceSpotProtectiveOcoAsync(
            CreateAiStrategy(stopLoss: 0m), 0.5m);
        Assert.IsFalse(missingStopLoss.Success);
        StringAssert.Contains(missingStopLoss.Error ?? string.Empty, "止损");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotOco_NonPositiveQuantity_ShouldReject()
    {
        var client = CreateSpotClient();
        var data = CreateData();
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.PlaceSpotProtectiveOcoAsync(CreateAiStrategy(), 0m);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Error ?? string.Empty, "数量必须大于 0");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task SpotOco_NetworkFailure_ShouldClassifyAsNetwork()
    {
        var client = CreateSpotClient();
        client.Setup(c => c.PlaceSpotOcoAsync(
                It.IsAny<string>(), It.IsAny<OrderSide>(), It.IsAny<decimal>(),
                It.IsAny<string?>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<decimal>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("模拟网络中断"));

        var data = CreateData();
        using var executor = CreateExecutor(client, CreatePassingRisk(), data);

        var result = await executor.PlaceSpotProtectiveOcoAsync(CreateAiStrategy(), 0.5m);

        Assert.IsFalse(result.Success);
        Assert.AreEqual(TradeFailureCategory.Network, result.FailureCategory,
            "网络异常应归类为可重试失败，让调用方按冷却策略处理");
    }
}
