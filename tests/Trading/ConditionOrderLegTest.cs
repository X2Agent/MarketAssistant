using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 条件单腿别测试：双腿 clientOrderId 解析、AI 护栏腿规格与指纹区分。
/// </summary>
[TestClass]
public sealed class ConditionOrderLegTest
{
    private static TradingStrategy CreateAiSignalStrategy() => new()
    {
        Id = "ai-strategy-1",
        Symbol = "BTCUSDT",
        Type = StrategyType.AISignal,
        Status = StrategyStatus.Active,
        Side = OrderSide.Buy,
        Quantity = 0.5m,
        ExecutionCount = 1,
        StopLossPrice = 90m,
        TakeProfitPrice = 120m
    };

    [TestMethod]
    [TestCategory("Unit")]
    public void ClientOrderId_ShouldEncodeLegAndStayWithinExchangeLimit()
    {
        var stopId = TradeExecutor.BuildConditionOrderClientOrderId("ai-strategy-1", ConditionOrderLeg.Stop);
        var takeProfitId = TradeExecutor.BuildConditionOrderClientOrderId("ai-strategy-1", ConditionOrderLeg.TakeProfit);

        Assert.AreEqual("MCai-strategy-1S", stopId);
        Assert.AreEqual("MCai-strategy-1T", takeProfitId);

        // 32 位 GUID "N" 策略 ID：2 + 32 + 1 = 35 ≤ 币安 36 字符上限
        var guidId = TradeExecutor.BuildConditionOrderClientOrderId(
            Guid.NewGuid().ToString("N"), ConditionOrderLeg.Stop);
        Assert.IsLessThanOrEqualTo(36, guidId.Length);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseClientOrderId_ShouldRecoverStrategyAndLeg()
    {
        var (stopStrategy, stopLeg) = ExchangeConditionOrderService.ParseClientOrderId("MCstrategy-1S");
        var (tpStrategy, tpLeg) = ExchangeConditionOrderService.ParseClientOrderId("MCstrategy-1T");

        Assert.AreEqual("strategy-1", stopStrategy);
        Assert.AreEqual(ConditionOrderLeg.Stop, stopLeg);
        Assert.AreEqual("strategy-1", tpStrategy);
        Assert.AreEqual(ConditionOrderLeg.TakeProfit, tpLeg);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ParseClientOrderId_ShouldRejectUnknownSuffix()
    {
        // 无腿别后缀（旧格式）或非本应用订单都无法识别，交由对账忽略
        var (strategyId, _) = ExchangeConditionOrderService.ParseClientOrderId("MCstrategy-1");
        Assert.IsNull(strategyId);

        var (foreignId, _) = ExchangeConditionOrderService.ParseClientOrderId("OTHER-123");
        Assert.IsNull(foreignId);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AiSignalStrategy_ShouldResolveBothLegs()
    {
        var strategy = CreateAiSignalStrategy();

        var legs = ExchangeConditionOrderService.ResolveLegs(strategy);

        Assert.HasCount(2, legs);
        Assert.Contains(ConditionOrderLeg.Stop, legs);
        Assert.Contains(ConditionOrderLeg.TakeProfit, legs);
        Assert.IsTrue(ExchangeConditionOrderService.IsProtectiveExitStrategy(strategy),
            "AI 策略首次成交后的固定护栏需要交易所侧保护");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AiSignalStrategy_BeforeFirstExecution_ShouldNotBeProtected()
    {
        var strategy = CreateAiSignalStrategy();
        strategy.ExecutionCount = 0;

        Assert.IsFalse(ExchangeConditionOrderService.IsProtectiveExitStrategy(strategy));
        Assert.IsEmpty(ExchangeConditionOrderService.ResolveLegs(strategy),
            "尚未建仓时没有可保护的持仓");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AiSignalLegSpec_ShouldUseInverseSideAndGuardrailPrices()
    {
        var strategy = CreateAiSignalStrategy();

        var stop = TradeExecutor.BuildConditionOrderSpec(strategy, ConditionOrderLeg.Stop);
        var takeProfit = TradeExecutor.BuildConditionOrderSpec(strategy, ConditionOrderLeg.TakeProfit);

        // 多头入场（Buy）→ 离场卖出（Sell）
        Assert.AreEqual(OrderSide.Sell, stop.Side);
        Assert.AreEqual(OrderType.StopMarket, stop.Type);
        Assert.AreEqual(90m, stop.StopPrice);

        Assert.AreEqual(OrderSide.Sell, takeProfit.Side);
        Assert.AreEqual(OrderType.TakeProfitMarket, takeProfit.Type);
        Assert.AreEqual(120m, takeProfit.StopPrice);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AiSignalLegSpec_ShortEntry_ShouldBuyToClose()
    {
        var strategy = CreateAiSignalStrategy();
        strategy.Side = OrderSide.Sell;

        var stop = TradeExecutor.BuildConditionOrderSpec(strategy, ConditionOrderLeg.Stop);

        Assert.AreEqual(OrderSide.Buy, stop.Side, "空头入场的止损应是买入平仓");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Fingerprint_ShouldDifferByLeg()
    {
        var strategy = CreateAiSignalStrategy();

        var stopFingerprint = ExchangeConditionOrderService.BuildFingerprint(strategy, ConditionOrderLeg.Stop);
        var takeProfitFingerprint = ExchangeConditionOrderService.BuildFingerprint(strategy, ConditionOrderLeg.TakeProfit);

        Assert.AreNotEqual(stopFingerprint, takeProfitFingerprint,
            "两条腿参数不同，指纹必须可区分，否则对账会误判为无漂移");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AiSignalFingerprint_ShouldReflectGuardrailPriceChanges_AndPreserveBothLegs()
    {
        var strategy = CreateAiSignalStrategy();

        var initialStop = ExchangeConditionOrderService.BuildFingerprint(strategy, ConditionOrderLeg.Stop);
        var initialTp = ExchangeConditionOrderService.BuildFingerprint(strategy, ConditionOrderLeg.TakeProfit);

        var merged = ExchangeConditionOrderService.MergeLegFingerprint(null, ConditionOrderLeg.Stop, initialStop);
        merged = ExchangeConditionOrderService.MergeLegFingerprint(merged, ConditionOrderLeg.TakeProfit, initialTp);

        Assert.IsTrue(ExchangeConditionOrderService.HasMatchingFingerprint(merged, initialStop));
        Assert.IsTrue(ExchangeConditionOrderService.HasMatchingFingerprint(merged, initialTp));

        // AI 护栏更新止损价后，Stop 腿指纹应漂移，而 TakeProfit 腿指纹保持匹配
        strategy.StopLossPrice = 95m;
        var updatedStop = ExchangeConditionOrderService.BuildFingerprint(strategy, ConditionOrderLeg.Stop);

        Assert.AreNotEqual(initialStop, updatedStop, "AI 护栏止损价变化必须反映到指纹中");
        Assert.IsFalse(ExchangeConditionOrderService.HasMatchingFingerprint(merged, updatedStop));
        Assert.IsTrue(ExchangeConditionOrderService.HasMatchingFingerprint(merged, initialTp));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AiSignalStrategy_InShadowMode_ShouldNotPlaceExchangeConditionOrders()
    {
        var strategy = CreateAiSignalStrategy();
        strategy.CustomParams = """{"shadowMode":true}""";

        Assert.IsFalse(ExchangeConditionOrderService.IsProtectiveExitStrategy(strategy),
            "影子策略不得在交易所挂真实条件单");
        Assert.IsEmpty(ExchangeConditionOrderService.ResolveLegs(strategy));
    }
}
