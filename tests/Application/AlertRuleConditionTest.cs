using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.PriceAlert;
using MarketAssistant.Infrastructure.Core;

namespace TestMarketAssistant.Application;

/// <summary>
/// 告警规则新维度测试：量比 / 技术指标条件的状态机语义、交叉条件、
/// 样本不足时的行为，以及自选股批量规则的符号绑定副本。
/// </summary>
[TestClass]
public sealed class AlertRuleConditionTest
{
    private static PriceAlertRule CreateRule(AlertCondition condition, decimal target = 0m) => new()
    {
        Id = "rule-1",
        AssetCode = "BTC",
        AssetName = "BTC",
        MarketType = MarketType.Crypto,
        Condition = condition,
        TargetPrice = target
    };

    [TestMethod]
    [TestCategory("Unit")]
    public void VolumeRatioAbove_ShouldTriggerWhenRatioReachesTarget()
    {
        var rule = CreateRule(AlertCondition.VolumeRatioAbove, 3m);
        var metric = new AlertRuleMetric(VolumeRatio: 3.5m, null, null, null, null, null, null, null, null, null, null);

        Assert.IsTrue(rule.IsConditionMet(0m, null, metric));
        Assert.IsTrue(rule.UpdateTriggerState(0m, null, metric));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void VolumeRatioAbove_WithMissingSample_ShouldNotTrigger()
    {
        var rule = CreateRule(AlertCondition.VolumeRatioAbove, 3m);

        // 量比样本不足（null）不得伪装成"条件未满足"以外的状态——直接不触发
        Assert.IsFalse(rule.IsConditionMet(0m, null, metric: null));
        Assert.IsFalse(rule.UpdateTriggerState(0m, null, metric: null));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RsiAboveAndBelow_ShouldCompareAgainstThreshold()
    {
        var overbought = CreateRule(AlertCondition.RsiAbove, 70m);
        var oversold = CreateRule(AlertCondition.RsiBelow, 30m);
        var high = new AlertRuleMetric(null, null, null, 75m, null, null, null, null, null, null, null);
        var low = new AlertRuleMetric(null, null, null, 25m, null, null, null, null, null, null, null);

        Assert.IsTrue(overbought.IsConditionMet(0m, null, high));
        Assert.IsFalse(overbought.IsConditionMet(0m, null, low));
        Assert.IsTrue(oversold.IsConditionMet(0m, null, low));
        Assert.IsFalse(oversold.IsConditionMet(0m, null, high));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MacdGoldenCross_ShouldHoldWhileDiffAboveDea()
    {
        var rule = CreateRule(AlertCondition.MacdGoldenCross);
        var golden = new AlertRuleMetric(null, 1.2m, 0.8m, null, null, null, null, null, null, null, null);
        var dead = new AlertRuleMetric(null, 0.8m, 1.2m, null, null, null, null, null, null, null, null);

        Assert.IsTrue(rule.IsConditionMet(0m, null, golden), "DIFF 在 DEA 上方即金叉状态成立");
        Assert.IsFalse(rule.IsConditionMet(0m, null, dead), "DIFF 跌到 DEA 下方后金叉状态消失");
        Assert.IsFalse(rule.IsConditionMet(0m, null, metric: null), "样本不足不得判定为金叉");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MaGoldenAndDeadCross_ShouldFollowFastSlowRelation()
    {
        var golden = CreateRule(AlertCondition.MaGoldenCross);
        var dead = CreateRule(AlertCondition.MaDeadCross);
        var fastAbove = new AlertRuleMetric(null, null, null, null, null, null, null, null, null, 12m, 10m);
        var fastBelow = new AlertRuleMetric(null, null, null, null, null, null, null, null, null, 9m, 10m);

        Assert.IsTrue(golden.IsConditionMet(0m, null, fastAbove));
        Assert.IsFalse(golden.IsConditionMet(0m, null, fastBelow));
        Assert.IsTrue(dead.IsConditionMet(0m, null, fastBelow));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BollBreak_ShouldCompareCloseAgainstBands()
    {
        var upper = CreateRule(AlertCondition.BollUpperBreak);
        var lower = CreateRule(AlertCondition.BollLowerBreak);
        var metric = new AlertRuleMetric(null, null, null, null, null, null, 105m, 100m, 106m, null, null);

        Assert.IsTrue(upper.IsConditionMet(0m, null, metric), "收盘价在上轨上方应突破");
        Assert.IsFalse(lower.IsConditionMet(0m, null, metric), "收盘价远离下轨不应判定跌破");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IndicatorCondition_ShouldReuseDebounceAndCooldown()
    {
        var rule = CreateRule(AlertCondition.RsiAbove, 70m);
        rule.ConfirmTicks = 2;
        var metric = new AlertRuleMetric(null, null, null, 75m, null, null, null, null, null, null, null);

        Assert.IsFalse(rule.UpdateTriggerState(0m, null, metric), "首次满足去抖要求，不应触发");
        Assert.IsTrue(rule.UpdateTriggerState(0m, null, metric), "连续第二次满足应触发");
        Assert.IsFalse(rule.UpdateTriggerState(0m, null, metric), "触发后需离开区间再进入才会再次触发");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CloneForSymbol_ShouldIsolateRuntimeStateAndBindSymbol()
    {
        var template = CreateRule(AlertCondition.PriceAbove, 100m);
        template.Scope = AlertScope.Watchlist;
        template.KLineType = KLineType.Daily;
        template.ConfirmTicks = 1;
        template.IsOneTime = true;

        var clone = template.CloneForSymbol("ETH", "Ethereum");

        Assert.AreEqual("ETH", clone.AssetCode, "副本必须绑定到具体符号");
        Assert.AreEqual("Ethereum", clone.AssetName);
        Assert.AreEqual(AlertScope.Single, clone.Scope, "副本已是单符号语义");
        Assert.AreEqual(AlertCondition.PriceAbove, clone.Condition);
        Assert.AreEqual(100m, clone.TargetPrice);

        // 模板已触发不影响副本：运行态互相隔离
        template.UpdateTriggerState(101m);
        Assert.IsTrue(template.Triggered);
        Assert.IsFalse(clone.Triggered, "副本的运行态必须独立");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RuleOptionsText_ShouldIncludeScopeAndPeriodForIndicatorRules()
    {
        var rule = CreateRule(AlertCondition.MacdGoldenCross);
        rule.Scope = AlertScope.Watchlist;
        rule.KLineType = KLineType.Minute15;

        Assert.Contains("自选股", rule.RuleOptionsText);
        Assert.Contains("15分钟", rule.RuleOptionsText);
    }
}
