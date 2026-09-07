using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Application;

/// <summary>
/// SignalAlertEvaluator 单元测试：验证策略状态跃迁收敛为 AlertSource.Signal，
/// 并与风控拒绝告警（AlertSource.Risk）按拒绝原因互斥分流，不产生重复告警。
/// </summary>
[TestClass]
public sealed class SignalAlertEvaluatorTest
{
    /// <summary>记录所有上报告警的桩实现，用于断言来源/级别/标题映射。</summary>
    private sealed class RecordingAlertCenter : IAlertCenterService
    {
        public List<AlertEvent> Raised { get; } = new();

        public event Action<AlertEvent>? AlertRaised { add { } remove { } }
        public event Action? AlertsChanged { add { } remove { } }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RaiseAlertAsync(AlertEvent alert, CancellationToken cancellationToken = default)
        {
            Raised.Add(alert);
            return Task.CompletedTask;
        }

        public Task RaiseAlertClearedAsync(
            AlertSource source, MarketType marketType, string symbol, string title,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AlertEvent>> GetRecentAlertsAsync(
            int limit = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AlertEvent>>(Raised);

        public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task MarkAllReadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>上报即抛异常的桩实现，用于验证 Safe 语义不向外冒泡。</summary>
    private sealed class ThrowingAlertCenter : IAlertCenterService
    {
        public bool Called { get; private set; }

        public event Action<AlertEvent>? AlertRaised { add { } remove { } }
        public event Action? AlertsChanged { add { } remove { } }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RaiseAlertAsync(AlertEvent alert, CancellationToken cancellationToken = default)
        {
            Called = true;
            throw new InvalidOperationException("告警中心不可用");
        }

        public Task RaiseAlertClearedAsync(
            AlertSource source, MarketType marketType, string symbol, string title,
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<AlertEvent>> GetRecentAlertsAsync(
            int limit = 200, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AlertEvent>>(Array.Empty<AlertEvent>());

        public Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);

        public Task MarkAllReadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private RecordingAlertCenter _alertCenter = null!;
    private SignalAlertEvaluator _evaluator = null!;

    [TestInitialize]
    public void Setup()
    {
        _alertCenter = new RecordingAlertCenter();
        _evaluator = new SignalAlertEvaluator(
            _alertCenter,
            LoggerFactory.Create(_ => { }).CreateLogger<SignalAlertEvaluator>());
    }

    private static TradingStrategy CreateStrategy() => new()
    {
        Symbol = "BTCUSDT",
        Type = StrategyType.AISignal
    };

    private static TradeResult Rejection(string errorMessage) => new()
    {
        Success = false,
        ErrorMessage = errorMessage,
        FailureCategory = TradeFailureCategory.Rejected
    };

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NotifyStrategyPaused_UserRejection_ShouldRaiseSignalWarning()
    {
        await _evaluator.NotifyStrategyPausedSafeAsync(
            CreateStrategy(), Rejection("用户拒绝交易: 风控要求人工确认"));

        Assert.AreEqual(1, _alertCenter.Raised.Count);
        var alert = _alertCenter.Raised[0];
        Assert.AreEqual(AlertSource.Signal, alert.Source);
        Assert.AreEqual(AlertLevel.Warning, alert.Level);
        Assert.AreEqual("策略已自动暂停", alert.Title);
        Assert.AreEqual(MarketType.Crypto, alert.MarketType);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NotifyStrategyPaused_RiskRejection_ShouldLeaveAlertToRiskEvaluator()
    {
        await _evaluator.NotifyStrategyPausedSafeAsync(
            CreateStrategy(), Rejection("风控拒绝: 超过单笔仓位上限"));

        Assert.AreEqual(0, _alertCenter.Raised.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NotifyStrategyCompleted_ShouldRaiseSignalInfo()
    {
        await _evaluator.NotifyStrategyCompletedSafeAsync(CreateStrategy());

        Assert.AreEqual(1, _alertCenter.Raised.Count);
        Assert.AreEqual(AlertSource.Signal, _alertCenter.Raised[0].Source);
        Assert.AreEqual(AlertLevel.Info, _alertCenter.Raised[0].Level);
        Assert.AreEqual("策略已完结", _alertCenter.Raised[0].Title);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NotifySignalExecuted_ShouldRaiseSignalInfoWithPrice()
    {
        await _evaluator.NotifySignalExecutedSafeAsync(CreateStrategy(), 43250.5m);

        Assert.AreEqual(1, _alertCenter.Raised.Count);
        var alert = _alertCenter.Raised[0];
        Assert.AreEqual(AlertSource.Signal, alert.Source);
        Assert.AreEqual(AlertLevel.Info, alert.Level);
        Assert.AreEqual("BTCUSDT", alert.Symbol);
        StringAssert.Contains(alert.Content, 43250.5m.ToString("N6"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task NotifyStrategyCompleted_AlertCenterFailure_ShouldNotThrow()
    {
        var throwingCenter = new ThrowingAlertCenter();
        var evaluator = new SignalAlertEvaluator(
            throwingCenter, LoggerFactory.Create(_ => { }).CreateLogger<SignalAlertEvaluator>());

        await evaluator.NotifyStrategyCompletedSafeAsync(CreateStrategy());

        Assert.IsTrue(throwingCenter.Called);
    }
}
