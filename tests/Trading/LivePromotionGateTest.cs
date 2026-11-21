using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;

namespace TestMarketAssistant.Trading;

/// <summary>
/// 实盘准入闸门判定测试：纯逻辑、无 IO。
/// 重点验证"每个门槛都能独立拦住不合格策略"，以及影子模式策略本身永不放行实盘。
/// </summary>
[TestClass]
public sealed class LivePromotionGateTest
{
    private static ShadowPerformance CreateShadow(
        int closedTrades = 40,
        int winningTrades = 26,
        decimal totalPnl = 100m,
        decimal winRate = 65m,
        decimal? profitFactor = 1.5m,
        decimal maxDrawdown = 20m) =>
        new("strategy-1", closedTrades, winningTrades, totalPnl, winRate, profitFactor, maxDrawdown);

    [TestMethod]
    [TestCategory("Unit")]
    public void QualifiedStrategy_ShouldBeAllowed()
    {
        var decision = LivePromotionGate.Evaluate(CreateShadow(), shadowMode: false);

        Assert.IsTrue(decision.Allowed, decision.Reason);
        Assert.AreEqual(40, decision.SampleSize);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ShadowModeStrategy_ShouldNeverBePromoted()
    {
        // 影子策略本来就不下单，即使绩效再好也不能"准入"到实盘
        var decision = LivePromotionGate.Evaluate(CreateShadow(), shadowMode: true);

        Assert.IsFalse(decision.Allowed);
        StringAssert.Contains(decision.Reason, "影子模式");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void InsufficientSamples_ShouldBeRejected()
    {
        var decision = LivePromotionGate.Evaluate(CreateShadow(closedTrades: 29), shadowMode: false);

        Assert.IsFalse(decision.Allowed);
        StringAssert.Contains(decision.Reason, "29");
        StringAssert.Contains(decision.Reason, "30");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void NegativePnl_ShouldBeRejected_EvenWithGoodWinRate()
    {
        // 高胜率但盈亏比很差时累计亏损：胜率不能作为准入的唯一依据
        var decision = LivePromotionGate.Evaluate(
            CreateShadow(winRate: 80m, totalPnl: -10m), shadowMode: false);

        Assert.IsFalse(decision.Allowed);
        StringAssert.Contains(decision.Reason, "未转正");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LowProfitFactor_ShouldBeRejected()
    {
        var decision = LivePromotionGate.Evaluate(
            CreateShadow(profitFactor: 1.05m), shadowMode: false);

        Assert.IsFalse(decision.Allowed);
        StringAssert.Contains(decision.Reason, "盈亏比");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MissingProfitFactor_ShouldBeTreatedAsPassing()
    {
        // 无亏损交易时盈亏比无定义（null），这是好形态而非坏形态，不应拦单
        var decision = LivePromotionGate.Evaluate(
            CreateShadow(profitFactor: null), shadowMode: false);

        Assert.IsTrue(decision.Allowed, decision.Reason);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ExcessiveDrawdownRelativeToProfit_ShouldBeRejected()
    {
        // 累计盈利 100，最大回撤 120：盈利曲线极不稳定
        var decision = LivePromotionGate.Evaluate(
            CreateShadow(totalPnl: 100m, maxDrawdown: 120m), shadowMode: false);

        Assert.IsFalse(decision.Allowed);
        StringAssert.Contains(decision.Reason, "回撤");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OnlyShadowCapableStrategies_RequirePromotion()
    {
        // 影子模式目前仅 AI 信号策略具备（ShadowTradingService 只被 AI 执行器记账），
        // 定投/网格没有影子绩效可验证，纳入闸门只会让它们在开启闸门后永远无法开仓
        Assert.IsTrue(LivePromotionGate.RequiresPromotion(StrategyType.AISignal));
        Assert.IsFalse(LivePromotionGate.RequiresPromotion(StrategyType.DCA));
        Assert.IsFalse(LivePromotionGate.RequiresPromotion(StrategyType.GridTrading));

        // 手工一次性止损/止盈不属于策略选股逻辑
        Assert.IsFalse(LivePromotionGate.RequiresPromotion(StrategyType.StopLoss));
        Assert.IsFalse(LivePromotionGate.RequiresPromotion(StrategyType.TakeProfit));
        Assert.IsFalse(LivePromotionGate.RequiresPromotion(StrategyType.TrailingStop));
    }
}
