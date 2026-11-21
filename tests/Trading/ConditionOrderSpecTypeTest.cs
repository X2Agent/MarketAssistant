using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 条件单订单类型映射测试。
///
/// 回归背景：早期实现用 <c>type.ToString().ToUpperInvariant()</c> 生成交易所类型串，
/// 把 <see cref="OrderType.StopMarket"/> 拼成 "STOPMARKET"（缺下划线），
/// 币安按参数错误拒绝，导致交易所侧条件单从未真正挂出——持仓在应用关闭期间完全没有保护。
/// </summary>
[TestClass]
public sealed class ConditionOrderSpecTypeTest
{
    private static TradingStrategy CreateStopLossStrategy() => new()
    {
        Id = "stoploss-1",
        Symbol = "BTCUSDT",
        Type = StrategyType.StopLoss,
        Status = StrategyStatus.Active,
        Side = OrderSide.Sell,
        TriggerPrice = 90m,
        Quantity = 1m
    };

    private static TradingStrategy CreateAiStrategy() => new()
    {
        Id = "ai-1",
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
    public void Futures_ShouldUseMarketConditionTypes()
    {
        var stop = TradeExecutor.BuildConditionOrderSpec(
            CreateStopLossStrategy(), ConditionOrderLeg.Stop, isFutures: true);

        Assert.AreEqual(OrderType.StopMarket, stop.Type);
        Assert.AreEqual(90m, stop.StopPrice);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Spot_ShouldUseMarketConditionTypes()
    {
        // 现货用 STOP_LOSS / TAKE_PROFIT：触发后以 MARKET 成交（币安现货 API 语义），
        // 不存在"限价=触发价、跳空后挂盘不成交"的风险
        var stop = TradeExecutor.BuildConditionOrderSpec(
            CreateStopLossStrategy(), ConditionOrderLeg.Stop, isFutures: false);

        Assert.AreEqual(OrderType.StopLoss, stop.Type);
        Assert.AreEqual(90m, stop.StopPrice);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Spot_AiTakeProfit_ShouldUseTakeProfitMarket()
    {
        var leg = TradeExecutor.BuildConditionOrderSpec(
            CreateAiStrategy(), ConditionOrderLeg.TakeProfit, isFutures: false);

        Assert.AreEqual(OrderType.TakeProfit, leg.Type);
        Assert.AreEqual(OrderSide.Sell, leg.Side);
        Assert.AreEqual(120m, leg.StopPrice);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Spot_TrailingStop_ShouldBeUnsupported_WithClientFallback()
    {
        // 现货无 TRAILING_STOP_MARKET：返回 error 让调用方靠客户端评估兜底，而不是静默不挂
        var strategy = CreateStopLossStrategy();
        strategy.Type = StrategyType.TrailingStop;
        strategy.CustomParams = """{"trailingPercent":5}""";

        var spec = TradeExecutor.BuildConditionOrderSpec(strategy, ConditionOrderLeg.Stop, isFutures: false);

        Assert.IsNull(spec.Type);
        Assert.IsNotNull(spec.Error);
        StringAssert.Contains(spec.Error, "现货");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void AiMissingGuardrails_ShouldReturnError_NotSilent()
    {
        var strategy = CreateAiStrategy();
        strategy.StopLossPrice = 0;

        var spec = TradeExecutor.BuildConditionOrderSpec(strategy, ConditionOrderLeg.Stop, isFutures: true);

        Assert.IsNull(spec.Type);
        Assert.IsNotNull(spec.Error);
    }
}
