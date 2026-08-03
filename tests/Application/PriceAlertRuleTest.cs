using MarketAssistant.Applications.PriceAlert;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class PriceAlertRuleTest
{
    [TestMethod]
    public void PriceCondition_ShouldCalculatePriceDifferenceAndPercent()
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.PriceAbove,
            TargetPrice = 100m
        };

        rule.UpdateQuote(105m, 2.5m, DateTime.UtcNow);

        Assert.AreEqual(105m, rule.CurrentValue);
        Assert.AreEqual(5m, rule.FloatingValue);
        Assert.AreEqual(5m, rule.FloatingPercent);
        Assert.AreEqual("+5（+5.00%）", rule.FloatingValueText);
        Assert.IsTrue(rule.HasCurrentQuote);
    }

    [TestMethod]
    public void PercentCondition_ShouldCalculatePercentagePointDifference()
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.ChangePercentBelow,
            TargetPrice = 3m
        };

        rule.UpdateQuote(42m, -4.25m, DateTime.UtcNow);

        Assert.AreEqual(-4.25m, rule.CurrentValue);
        Assert.AreEqual(-1.25m, rule.FloatingValue);
        Assert.IsNull(rule.FloatingPercent);
        Assert.AreEqual("-1.25 个百分点", rule.FloatingValueText);
    }

    [TestMethod]
    [DataRow(AlertCondition.PriceAbove, 100, 101, 0, true)]
    [DataRow(AlertCondition.PriceAbove, 100, 99, 0, false)]
    [DataRow(AlertCondition.PriceBelow, 100, 99, 0, true)]
    [DataRow(AlertCondition.PriceBelow, 100, 101, 0, false)]
    [DataRow(AlertCondition.ChangePercentAbove, 3, 100, 3.5, true)]
    [DataRow(AlertCondition.ChangePercentAbove, 3, 100, 2.5, false)]
    [DataRow(AlertCondition.ChangePercentBelow, 3, 100, -3.5, true)]
    [DataRow(AlertCondition.ChangePercentBelow, 3, 100, -2.5, false)]
    public void IsConditionMet_ShouldUseExpectedThresholdDirection(
        AlertCondition condition,
        double target,
        double price,
        double changePercent,
        bool expected)
    {
        var rule = new PriceAlertRule
        {
            Condition = condition,
            TargetPrice = (decimal)target
        };

        var actual = rule.IsConditionMet((decimal)price, (decimal)changePercent);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void PercentBelowCondition_ShouldCalculateDifferenceFromNegativeThreshold()
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.ChangePercentBelow,
            TargetPrice = 3m
        };

        rule.UpdateQuote(42m, -4.25m, DateTime.UtcNow);

        Assert.AreEqual(-1.25m, rule.FloatingValue);
        Assert.AreEqual("-1.25 个百分点", rule.FloatingValueText);
    }

    [TestMethod]
    public void UpdateTriggerState_ShouldNotifyOnlyOnConditionEntryAndRearmAfterExit()
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.PriceAbove,
            TargetPrice = 100m
        };

        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsTrue(rule.UpdateTriggerState(100m));
        Assert.IsFalse(rule.UpdateTriggerState(101m));
        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsTrue(rule.UpdateTriggerState(100m));
    }

    [TestMethod]
    public void OneTimeRule_ShouldNotifyOnceAndKeepTriggeredLatch()
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.PriceAbove,
            TargetPrice = 100m,
            IsOneTime = true
        };

        Assert.IsTrue(rule.UpdateTriggerState(100m));
        Assert.IsTrue(rule.Triggered);
        // 持续处于区间内不再重复提醒
        Assert.IsFalse(rule.UpdateTriggerState(101m));
        Assert.IsTrue(rule.Triggered);
        // 离开区间后不自动复位，仍保持触发锁存
        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsTrue(rule.Triggered);
        // 再次进入区间不再提醒
        Assert.IsFalse(rule.UpdateTriggerState(100m));
        Assert.IsTrue(rule.Triggered);
    }

    [TestMethod]
    public void OneTimeRule_ShouldExposeReenableStatus()
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.PriceBelow,
            TargetPrice = 50m,
            IsOneTime = true
        };

        Assert.AreEqual("一次性", rule.AlertModeText);
        Assert.AreEqual("禁用", rule.StatusText);

        rule.Enabled = true;
        rule.Triggered = true;
        Assert.AreEqual("重新启用", rule.StatusText);
    }

    [TestMethod]
    public void ContinuousRule_ShouldExposeContinuousMode()
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.PriceBelow,
            TargetPrice = 50m,
            IsOneTime = false
        };

        Assert.AreEqual("持续", rule.AlertModeText);
        Assert.AreEqual("禁用", rule.StatusText);

        rule.Enabled = false;
        Assert.AreEqual("启用", rule.StatusText);
    }

    [TestMethod]
    public void MissingQuote_ShouldExposeWaitingState()
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.PriceBelow,
            TargetPrice = 50m
        };

        Assert.IsNull(rule.CurrentValue);
        Assert.IsNull(rule.FloatingValue);
        Assert.AreEqual("--", rule.CurrentPriceText);
        Assert.AreEqual("--", rule.FloatingValueText);
        Assert.AreEqual("等待行情", rule.QuoteStatusText);
    }
}
