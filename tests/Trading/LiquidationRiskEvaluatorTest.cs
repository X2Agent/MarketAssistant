using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 合约强平距离监控测试：距离口径、阈值分级与无强平价时的跳过。
/// </summary>
[TestClass]
public sealed class LiquidationRiskEvaluatorTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void EvaluatePosition_ShouldComputeDistanceFromMarkPrice()
    {
        // 多头：标记价 100000，强平价 90000 → 距离 10%
        var longPosition = CreatePosition(positionAmt: 1m, markPrice: 100000m, liquidationPrice: 90000m);
        var risk = LiquidationRiskEvaluator.EvaluatePosition(longPosition);

        Assert.IsNotNull(risk);
        Assert.AreEqual(10m, risk!.DistancePercent, 0.0001m);
        Assert.IsFalse(risk.IsShort);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EvaluatePosition_ShortPosition_ShouldUseAbsoluteDistance()
    {
        // 空头：标记价 100000，强平价 110000 → 距离同样是 10%
        var shortPosition = CreatePosition(positionAmt: -1m, markPrice: 100000m, liquidationPrice: 110000m);
        var risk = LiquidationRiskEvaluator.EvaluatePosition(shortPosition);

        Assert.IsNotNull(risk);
        Assert.AreEqual(10m, risk!.DistancePercent, 0.0001m);
        Assert.IsTrue(risk.IsShort);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void EvaluatePosition_WithoutLiquidationPrice_ShouldReturnNull()
    {
        var position = CreatePosition(positionAmt: 1m, markPrice: 100000m, liquidationPrice: 0m);

        Assert.IsNull(LiquidationRiskEvaluator.EvaluatePosition(position),
            "交易所未返回强平价（全仓低风险）时不得把 0 当强平价");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ResolveLevel_ShouldClassifyByThresholds()
    {
        Assert.AreEqual(LiquidationRiskLevel.None, LiquidationRiskEvaluator.ResolveLevel(30m, 25m, 10m));
        Assert.AreEqual(LiquidationRiskLevel.Warning, LiquidationRiskEvaluator.ResolveLevel(20m, 25m, 10m));
        Assert.AreEqual(LiquidationRiskLevel.Warning, LiquidationRiskEvaluator.ResolveLevel(25m, 25m, 10m));
        Assert.AreEqual(LiquidationRiskLevel.Critical, LiquidationRiskEvaluator.ResolveLevel(10m, 25m, 10m));
        Assert.AreEqual(LiquidationRiskLevel.Critical, LiquidationRiskEvaluator.ResolveLevel(5m, 25m, 10m));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ResolveLevel_WithDisabledThresholds_ShouldStayNone()
    {
        Assert.AreEqual(LiquidationRiskLevel.None, LiquidationRiskEvaluator.ResolveLevel(1m, 0m, 0m),
            "阈值为 0 表示用户关闭了强平告警");
    }

    private static ExchangePosition CreatePosition(decimal positionAmt, decimal markPrice, decimal liquidationPrice)
        => new()
        {
            Symbol = "BTCUSDT",
            PositionAmt = positionAmt,
            MarkPrice = markPrice,
            EntryPrice = markPrice,
            LiquidationPrice = liquidationPrice
        };
}
