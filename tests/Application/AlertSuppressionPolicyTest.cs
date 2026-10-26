using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;

namespace TestMarketAssistant.Application;

[TestClass]
public sealed class AlertSuppressionPolicyTest
{
    private static AlertEvent CreateAlert(
        AlertLevel level = AlertLevel.Warning,
        string title = "测试告警",
        string symbol = "BTCUSDT",
        MarketType marketType = MarketType.Crypto,
        AlertSource source = AlertSource.PriceAlert) => new()
        {
            MarketType = marketType,
            Symbol = symbol,
            Level = level,
            Source = source,
            Title = title,
            Content = "内容"
        };

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_FirstAlert_ShouldNotify()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var decision = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, now);

        Assert.IsFalse(decision.IsMerge);
        Assert.IsTrue(decision.ShouldNotify);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_WithinMergeWindow_ShouldMergeAndNotNotify()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var first = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, now);
        var second = policy.Evaluate(CreateAlert(), first.NewMergeState, now.AddMinutes(2));

        Assert.IsTrue(second.IsMerge);
        Assert.IsFalse(second.ShouldNotify);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_AfterMergeWindow_ShouldNotifyAgain()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var first = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, now);
        var second = policy.Evaluate(
            CreateAlert(), first.NewMergeState,
            now + AlertSuppressionPolicy.MergeWindow + TimeSpan.FromSeconds(1));

        Assert.IsFalse(second.IsMerge);
        Assert.IsTrue(second.ShouldNotify);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_DifferentDedupeKey_ShouldNotMerge()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var first = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, now);

        // 另一去重键有独立的合并状态，窗口内仍可触达
        var other = policy.Evaluate(
            CreateAlert(title: "另一标的", symbol: "ETHUSDT"),
            AlertSuppressionPolicy.MergeState.Empty, now.AddMinutes(1));

        Assert.IsFalse(other.IsMerge);
        Assert.IsTrue(other.ShouldNotify);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_Suppressed_ShouldNotNotify_AndResumeAfterUnsuppressed()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        // 静默期（休市/免打扰/关闭通知）内多条告警：只落库不弹窗
        for (var i = 0; i < 3; i++)
        {
            var decision = policy.Evaluate(
                CreateAlert(title: $"静默告警{i}"), AlertSuppressionPolicy.MergeState.Empty,
                now.AddMinutes(i), suppressNotification: true);
            Assert.IsFalse(decision.ShouldNotify, "静默告警不应弹窗");
        }

        // 静默解除后立即恢复触达（合并窗口按各自键独立，不受影响）
        var resumed = policy.Evaluate(
            CreateAlert(title: "交易时段告警"), AlertSuppressionPolicy.MergeState.Empty,
            now.AddMinutes(10));

        Assert.IsTrue(resumed.ShouldNotify, "静默解除后应可正常触达");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_SuppressedCritical_ShouldNotNotify()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        // Critical 被抑制时同样不弹窗（口径统一：抑制优先于级别）
        var decision = policy.Evaluate(
            CreateAlert(level: AlertLevel.Critical, title: "静默紧急告警"),
            AlertSuppressionPolicy.MergeState.Empty, now, suppressNotification: true);

        Assert.IsFalse(decision.ShouldNotify);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsSilencedByTradingSession_ASharePriceWarning_ShouldSilence()
    {
        var alert = CreateAlert(marketType: MarketType.AShare);

        Assert.IsTrue(AlertSuppressionPolicy.IsSilencedByTradingSession(alert, isTradingSession: false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsSilencedByTradingSession_CriticalOrInSessionOrCrypto_ShouldNotSilence()
    {
        // Critical（确认级价格告警）例外：休市也须即时触达并驱动交易联动门
        Assert.IsFalse(AlertSuppressionPolicy.IsSilencedByTradingSession(
            CreateAlert(level: AlertLevel.Critical, marketType: MarketType.AShare), isTradingSession: false));

        // 交易时段内不静默
        Assert.IsFalse(AlertSuppressionPolicy.IsSilencedByTradingSession(
            CreateAlert(marketType: MarketType.AShare), isTradingSession: true));

        // 虚拟币 7×24，不受 A 股休市影响
        Assert.IsFalse(AlertSuppressionPolicy.IsSilencedByTradingSession(
            CreateAlert(), isTradingSession: false));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsSilencedByTradingSession_NonPriceSource_ShouldNotSilence()
    {
        var alert = CreateAlert(marketType: MarketType.AShare, source: AlertSource.Signal);

        Assert.IsFalse(AlertSuppressionPolicy.IsSilencedByTradingSession(alert, isTradingSession: false));
    }
}
