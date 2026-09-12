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
    public void Evaluate_FirstAlert_ShouldNotifyAndCountQuota()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var decision = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, AlertSuppressionPolicy.QuotaState.Empty, 5, now);

        Assert.IsFalse(decision.IsMerge);
        Assert.IsTrue(decision.ShouldNotify);
        Assert.AreEqual(1, decision.NewQuotaState.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_WithinMergeWindow_ShouldMergeAndNotNotify()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var first = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, AlertSuppressionPolicy.QuotaState.Empty, 5, now);
        var second = policy.Evaluate(CreateAlert(), first.NewMergeState, first.NewQuotaState, 5, now.AddMinutes(2));

        Assert.IsTrue(second.IsMerge);
        Assert.IsFalse(second.ShouldNotify);
        // 合并不消耗配额
        Assert.AreEqual(1, second.NewQuotaState.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_AfterMergeWindow_ShouldNotifyAgain()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var first = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, AlertSuppressionPolicy.QuotaState.Empty, 5, now);
        var second = policy.Evaluate(
            CreateAlert(), first.NewMergeState, first.NewQuotaState, 5,
            now + AlertSuppressionPolicy.MergeWindow + TimeSpan.FromSeconds(1));

        Assert.IsFalse(second.IsMerge);
        Assert.IsTrue(second.ShouldNotify);
        Assert.AreEqual(2, second.NewQuotaState.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_DifferentDedupeKey_ShouldNotMerge()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var first = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, AlertSuppressionPolicy.QuotaState.Empty, 5, now);

        // 另一去重键有独立的合并状态，窗口内仍可触达
        var other = policy.Evaluate(
            CreateAlert(title: "另一标的", symbol: "ETHUSDT"),
            AlertSuppressionPolicy.MergeState.Empty, first.NewQuotaState, 5, now.AddMinutes(1));

        Assert.IsFalse(other.IsMerge);
        Assert.IsTrue(other.ShouldNotify);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_QuotaExceeded_ShouldSuppressNonCritical()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;
        var quota = AlertSuppressionPolicy.QuotaState.Empty;

        // 不同去重键（各自空合并状态）连续触发直至全局配额耗尽
        for (var i = 0; i < 3; i++)
        {
            var decision = policy.Evaluate(
                CreateAlert(title: $"告警{i}"), AlertSuppressionPolicy.MergeState.Empty, quota, 3,
                now.AddMinutes(i));
            quota = decision.NewQuotaState;
            Assert.IsTrue(decision.ShouldNotify);
        }

        // 配额耗尽后不再弹窗（仍落库）
        var suppressed = policy.Evaluate(
            CreateAlert(title: "超额告警"), AlertSuppressionPolicy.MergeState.Empty, quota, 3, now.AddMinutes(10));

        Assert.IsFalse(suppressed.IsMerge);
        Assert.IsFalse(suppressed.ShouldNotify);
        Assert.AreEqual(3, suppressed.NewQuotaState.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_CriticalAlert_ShouldBypassQuota()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        // 首条已耗尽配额（每小时 1 条）
        var first = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, AlertSuppressionPolicy.QuotaState.Empty, 1, now);
        Assert.IsTrue(first.ShouldNotify);

        // Critical 不受配额限制
        var critical = policy.Evaluate(
            CreateAlert(level: AlertLevel.Critical, title: "紧急告警"),
            AlertSuppressionPolicy.MergeState.Empty, first.NewQuotaState, 1, now.AddMinutes(1));

        Assert.IsTrue(critical.ShouldNotify);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_NewQuotaWindow_ShouldResetCount()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        var first = policy.Evaluate(
            CreateAlert(), AlertSuppressionPolicy.MergeState.Empty, AlertSuppressionPolicy.QuotaState.Empty, 1, now);
        Assert.AreEqual(1, first.NewQuotaState.Count);

        // 超过 1 小时后配额窗口重置并重新起算
        var later = policy.Evaluate(
            CreateAlert(title: "一小时后"), AlertSuppressionPolicy.MergeState.Empty, first.NewQuotaState, 1,
            now.AddHours(1).AddSeconds(1));

        Assert.IsTrue(later.ShouldNotify);
        Assert.AreEqual(1, later.NewQuotaState.Count);
        Assert.AreEqual(now.AddHours(1).AddSeconds(1), later.NewQuotaState.WindowStartUtc);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_Suppressed_ShouldNotConsumeQuota_AndResumeAfterUnsuppressed()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        // 静默期（休市/免打扰/关闭通知）内多条告警：只落库不弹窗，配额零消耗
        var quota = AlertSuppressionPolicy.QuotaState.Empty;
        for (var i = 0; i < 3; i++)
        {
            var decision = policy.Evaluate(
                CreateAlert(title: $"静默告警{i}"), AlertSuppressionPolicy.MergeState.Empty, quota, 3,
                now.AddMinutes(i), suppressNotification: true);
            quota = decision.NewQuotaState;
            Assert.IsFalse(decision.ShouldNotify, "静默告警不应弹窗");
        }

        Assert.AreEqual(0, quota.Count, "静默告警不应消耗配额");

        // 静默解除后，完整配额立即可用（合并窗口按各自键独立，不受影响）
        var resumed = policy.Evaluate(
            CreateAlert(title: "交易时段告警"), AlertSuppressionPolicy.MergeState.Empty, quota, 3,
            now.AddMinutes(10));

        Assert.IsTrue(resumed.ShouldNotify, "静默解除后配额未被占用，应可正常触达");
        Assert.AreEqual(1, resumed.NewQuotaState.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Evaluate_SuppressedCritical_ShouldNotConsumeQuota()
    {
        var policy = new AlertSuppressionPolicy();
        var now = DateTime.UtcNow;

        // Critical 原不受配额限制，但被抑制时同样不弹窗不计数（口径统一）
        var decision = policy.Evaluate(
            CreateAlert(level: AlertLevel.Critical, title: "静默紧急告警"),
            AlertSuppressionPolicy.MergeState.Empty, AlertSuppressionPolicy.QuotaState.Empty, 5,
            now, suppressNotification: true);

        Assert.IsFalse(decision.ShouldNotify);
        Assert.AreEqual(0, decision.NewQuotaState.Count);
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
