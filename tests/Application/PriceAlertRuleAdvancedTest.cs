using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.PriceAlert;

namespace TestMarketAssistant.Application;

/// <summary>
/// 价格告警规则高级触发特性：去抖（ConfirmTicks/ConfirmSeconds）、冷却（CooldownMinutes）、
/// 触发次数上限（MaxTriggerCount）与交易联动门状态（NotifyTriggered/ShouldClearTradingGate）。
/// </summary>
[TestClass]
public sealed class PriceAlertRuleAdvancedTest
{
    private static PriceAlertRule CreateRule(Action<PriceAlertRule>? configure = null)
    {
        var rule = new PriceAlertRule
        {
            Condition = AlertCondition.PriceAbove,
            TargetPrice = 100m
        };
        configure?.Invoke(rule);
        return rule;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ConfirmTicks_ShouldRequireConsecutiveMetTicks()
    {
        var rule = CreateRule(r => r.ConfirmTicks = 3);

        Assert.IsFalse(rule.UpdateTriggerState(101m));
        Assert.IsFalse(rule.UpdateTriggerState(102m));
        Assert.IsTrue(rule.UpdateTriggerState(103m));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ConfirmTicks_ConditionBreak_ShouldResetProgress()
    {
        var rule = CreateRule(r => r.ConfirmTicks = 3);

        Assert.IsFalse(rule.UpdateTriggerState(101m));
        Assert.IsFalse(rule.UpdateTriggerState(102m));
        // 中途离开区间重置去抖进度
        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsFalse(rule.UpdateTriggerState(101m));
        Assert.IsFalse(rule.UpdateTriggerState(102m));
        Assert.IsTrue(rule.UpdateTriggerState(103m));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CooldownMinutes_ShouldBlockRetriggerWithinInterval()
    {
        var rule = CreateRule(r => r.CooldownMinutes = 5);

        Assert.IsTrue(rule.UpdateTriggerState(101m));
        rule.NotifyTriggered();

        // 条件离开后快速回到区间：冷却期内不触发
        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsFalse(rule.UpdateTriggerState(101m));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MaxTriggerCount_ShouldStopNotifyingAfterLimit()
    {
        var rule = CreateRule(r =>
        {
            r.MaxTriggerCount = 2;
            r.CooldownMinutes = 0;
        });

        // 第一次触发
        Assert.IsTrue(rule.UpdateTriggerState(101m));
        rule.NotifyTriggered();
        // 离开后重新进入（同秒内无冷却问题）
        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsTrue(rule.UpdateTriggerState(101m));
        rule.NotifyTriggered();

        // 达到上限后不再触发
        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsFalse(rule.UpdateTriggerState(101m));
        Assert.IsTrue(rule.IsTriggerLimitReached);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void OneTimeRule_ShouldCountAsSingleTrigger()
    {
        var rule = CreateRule(r => r.IsOneTime = true);

        Assert.IsTrue(rule.UpdateTriggerState(101m));
        Assert.IsTrue(rule.IsTriggerLimitReached);

        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsFalse(rule.UpdateTriggerState(101m));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TradingGate_ShouldRequireClearAfterConditionExit()
    {
        var rule = CreateRule(r => r.TradingImpact = AlertTradingImpact.RequireConfirmation);

        Assert.IsTrue(rule.UpdateTriggerState(101m));
        rule.NotifyTriggered();

        // 条件仍在区间内：联动门保持
        Assert.IsFalse(rule.ShouldClearTradingGate);

        // 条件离开区间：需要解除联动门
        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsTrue(rule.ShouldClearTradingGate);

        rule.ResetTradingGate();
        Assert.IsFalse(rule.ShouldClearTradingGate);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TradingGate_NonConfirmationImpact_ShouldNeverRequireClear()
    {
        var rule = CreateRule(); // 默认 None

        Assert.IsTrue(rule.UpdateTriggerState(101m));
        rule.NotifyTriggered();
        Assert.IsFalse(rule.UpdateTriggerState(99m));

        Assert.IsFalse(rule.ShouldClearTradingGate);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void LegacyRule_AllZeroConfig_ShouldKeepOriginalBehavior()
    {
        // 旧规则（未配置去抖/冷却/限次）保持原有"进入区间提醒一次、离开自动复位"语义
        var rule = CreateRule();

        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsTrue(rule.UpdateTriggerState(100m));
        Assert.IsFalse(rule.UpdateTriggerState(101m));
        Assert.IsFalse(rule.UpdateTriggerState(99m));
        Assert.IsTrue(rule.UpdateTriggerState(100m));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void RuleOptionsText_ShouldSummarizeAdvancedConfig()
    {
        var plain = CreateRule();
        Assert.AreEqual(string.Empty, plain.RuleOptionsText);

        var configured = CreateRule(r =>
        {
            r.ConfirmTicks = 3;
            r.CooldownMinutes = 30;
            r.MaxTriggerCount = 5;
            r.TradingImpact = AlertTradingImpact.RequireConfirmation;
        });
        Assert.AreEqual("去抖 3 次 · 冷却 30 分 · 限 5 次 · 确认级", configured.RuleOptionsText);

        // 一次性规则限次固定为 1，不额外展示
        var oneTime = CreateRule(r =>
        {
            r.IsOneTime = true;
            r.MaxTriggerCount = 1;
        });
        Assert.AreEqual(string.Empty, oneTime.RuleOptionsText);
    }
}
