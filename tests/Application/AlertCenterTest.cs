using System.Globalization;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace TestMarketAssistant.Application;

/// <summary>
/// AlertCenterService 集成测试：告警落库、同类合并、未读统计、配额与免打扰抑制、
/// 以及确认级告警的交易联动门（IAlertGate）激活与释放。
/// 使用独立测试标的，用例前后清理 alert_events 中的测试数据。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AlertCenterTest
{
    private const string TestSymbol = "ALERTTEST";

    private sealed class StubNotificationService : INotificationService
    {
        public List<string> Messages { get; } = new();

        public void ShowSuccess(string message, int durationMs = 3000) => Messages.Add(message);
        public void ShowError(string message, int durationMs = 3000) => Messages.Add(message);
        public void ShowInfo(string message, int durationMs = 3000) => Messages.Add(message);
        public void ShowWarning(string message, int durationMs = 3000) => Messages.Add(message);
    }

    private sealed class StubUserSettingService : IUserSettingService
    {
        public UserSetting CurrentSetting { get; } = new();

        public void LoadSettings() { }
        public void SaveSettings() { }
        public void UpdateSettings(UserSetting setting) { }
        public void UpdateSetting(Action<UserSetting> mutate) => mutate(CurrentSetting);
        public void ResetSettings() { }
    }

    private AlertCenterService _service = null!;
    private StubNotificationService _notification = null!;
    private StubUserSettingService _settings = null!;
    private AlertGate _gate = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _notification = new StubNotificationService();
        _settings = new StubUserSettingService();
        _gate = new AlertGate();
        _service = new AlertCenterService(
            _notification,
            _settings,
            _gate,
            LoggerFactory.Create(_ => { }).CreateLogger<AlertCenterService>());

        await _service.InitializeAsync();
        await ExecuteAsync("DELETE FROM alert_events WHERE symbol = @symbol");
    }

    [TestCleanup]
    public async Task Cleanup() => await ExecuteAsync("DELETE FROM alert_events WHERE symbol = @symbol");

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RaiseAlert_ShouldPersistAndRaiseAlertRaisedEvent()
    {
        AlertEvent? raised = null;
        _service.AlertRaised += alert => raised = alert;

        await _service.RaiseAlertAsync(CreateAlert("涨破 100"));

        Assert.IsNotNull(raised);
        Assert.AreEqual("涨破 100", raised.Title);

        var stored = await GetTestAlertsAsync();
        Assert.AreEqual(1, stored.Count);
        Assert.AreEqual(1, stored[0].OccurrenceCount);
        Assert.IsFalse(stored[0].IsRead);
        // 首条告警弹窗一次
        Assert.AreEqual(1, _notification.Messages.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RaiseAlert_SameDedupeKeyWithinMergeWindow_ShouldMergeIntoSingleRecord()
    {
        for (var i = 0; i < 3; i++)
            await _service.RaiseAlertAsync(CreateAlert("重复告警"));

        var stored = await GetTestAlertsAsync();

        Assert.AreEqual(1, stored.Count);
        Assert.AreEqual(3, stored[0].OccurrenceCount);
        // 合并触发不重复弹窗
        Assert.AreEqual(1, _notification.Messages.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RaiseAlert_QuotaExceeded_ShouldPersistWithoutNotify()
    {
        _settings.CurrentSetting.AlertHourlyQuota = 1;

        await _service.RaiseAlertAsync(CreateAlert("配额内告警"));
        await _service.RaiseAlertAsync(CreateAlert("超配额告警"));

        var stored = await GetTestAlertsAsync();

        // 两条都落库，但仅第一条弹窗
        Assert.AreEqual(2, stored.Count);
        Assert.AreEqual(1, _notification.Messages.Count);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RaiseAlert_DuringQuietHours_ShouldSuppressWarningButNotCritical()
    {
        var nowHour = DateTime.Now.Hour;
        _settings.CurrentSetting.AlertQuietHoursEnabled = true;
        _settings.CurrentSetting.AlertQuietStartHour = nowHour;
        _settings.CurrentSetting.AlertQuietEndHour = (nowHour + 1) % 24;

        await _service.RaiseAlertAsync(CreateAlert("静默期警告"));
        await _service.RaiseAlertAsync(CreateAlert("静默期紧急", AlertLevel.Critical));

        var stored = await GetTestAlertsAsync();

        Assert.AreEqual(2, stored.Count);
        // 免打扰时段内仅 Critical 触达
        Assert.AreEqual(1, _notification.Messages.Count);
        Assert.IsTrue(_notification.Messages[0].Contains("静默期紧急"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RaiseAlert_ConfirmationImpact_ShouldActivateGateUntilCleared()
    {
        var alert = CreateAlert(
            "确认级告警", AlertLevel.Critical, AlertTradingImpact.RequireConfirmation);

        await _service.RaiseAlertAsync(alert);
        Assert.IsTrue(_gate.IsGated(MarketType.Crypto, TestSymbol));

        await _service.RaiseAlertClearedAsync(
            AlertSource.PriceAlert, MarketType.Crypto, TestSymbol, "确认级告警");
        Assert.IsFalse(_gate.IsGated(MarketType.Crypto, TestSymbol));
    }

    /// <summary>
    /// 同一标的多条不同条件的确认级规则：门键含标题，各规则独立登记/释放，
    /// 防止后触发覆盖先触发导致门计数无法归零（标的水久门控）。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task RaiseAlert_MultipleConfirmationRulesSameSymbol_ShouldTrackGatesIndependently()
    {
        var above = CreateAlert("确认级涨破", AlertLevel.Critical, AlertTradingImpact.RequireConfirmation);
        var below = CreateAlert("确认级跌破", AlertLevel.Critical, AlertTradingImpact.RequireConfirmation);

        await _service.RaiseAlertAsync(above);
        await _service.RaiseAlertAsync(below);
        Assert.IsTrue(_gate.IsGated(MarketType.Crypto, TestSymbol));

        // 释放其中一条，另一条仍触发中，门保持生效
        await _service.RaiseAlertClearedAsync(
            AlertSource.PriceAlert, MarketType.Crypto, TestSymbol, "确认级涨破");
        Assert.IsTrue(_gate.IsGated(MarketType.Crypto, TestSymbol));

        // 两条全部释放，门应彻底关闭（修复前第二次 Remove 因键被覆盖而失败，门残留）
        await _service.RaiseAlertClearedAsync(
            AlertSource.PriceAlert, MarketType.Crypto, TestSymbol, "确认级跌破");
        Assert.IsFalse(_gate.IsGated(MarketType.Crypto, TestSymbol));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RaiseAlert_NonConfirmationImpact_ShouldNotTouchGate()
    {
        await _service.RaiseAlertAsync(CreateAlert("普通告警"));

        Assert.IsFalse(_gate.IsGated(MarketType.Crypto, TestSymbol));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task MarkAllRead_ShouldClearUnreadCount()
    {
        await _service.RaiseAlertAsync(CreateAlert("未读告警"));
        Assert.AreEqual(1, await CountAsync(
            "SELECT COUNT(*) FROM alert_events WHERE symbol = @symbol AND is_read = 0"));

        await _service.MarkAllReadAsync();

        Assert.AreEqual(0, await CountAsync(
            "SELECT COUNT(*) FROM alert_events WHERE symbol = @symbol AND is_read = 0"));
        Assert.AreEqual(1, (await GetTestAlertsAsync()).Count(a => a.IsRead));
    }

    /// <summary>
    /// 历史告警保留期裁剪：超过保留期的记录在初始化（应用重启）时被清除，保留期内记录不受影响。
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task Initialize_ShouldPruneAlertsBeyondRetentionPeriod()
    {
        await _service.RaiseAlertAsync(CreateAlert("保留期内告警"));
        await InsertRawAlertAsync("超期告警", DateTime.UtcNow.AddDays(-60));
        Assert.AreEqual(2, (await GetTestAlertsAsync()).Count);

        // 新建实例并初始化，模拟应用重启触发裁剪
        var restarted = new AlertCenterService(
            _notification,
            _settings,
            new AlertGate(),
            LoggerFactory.Create(_ => { }).CreateLogger<AlertCenterService>());
        await restarted.InitializeAsync();

        var stored = await GetTestAlertsAsync();
        Assert.AreEqual(1, stored.Count);
        Assert.AreEqual("保留期内告警", stored[0].Title);
    }

    private static AlertEvent CreateAlert(
        string title,
        AlertLevel level = AlertLevel.Warning,
        AlertTradingImpact tradingImpact = AlertTradingImpact.None) => new()
        {
            MarketType = MarketType.Crypto,
            Symbol = TestSymbol,
            Level = level,
            Source = AlertSource.PriceAlert,
            Title = title,
            Content = "测试内容",
            TradingImpact = tradingImpact
        };

    private async Task<List<AlertEvent>> GetTestAlertsAsync()
    {
        var recent = await _service.GetRecentAlertsAsync(500);
        return recent.Where(a => a.Symbol == TestSymbol).ToList();
    }

    private static string DbPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        AppInfo.AppName,
        // 与 SqliteServiceBase.UnifiedDbFileName 一致：所有持久化服务共享同一库文件
        "market.db");

    private static async Task ExecuteAsync(string sql)
    {
        await using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@symbol", TestSymbol);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>直接写入一条指定时间的告警，用于构造超出保留期的历史数据。</summary>
    private static async Task InsertRawAlertAsync(string title, DateTime createdAtUtc)
    {
        await using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alert_events (id, market_type, symbol, level, source, title, content, created_at, is_read, trading_impact, occurrence_count)
            VALUES (@id, @marketType, @symbol, @level, @source, @title, @content, @createdAt, 0, 0, 1)
            """;
        cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString("N"));
        cmd.Parameters.AddWithValue("@marketType", (int)MarketType.Crypto);
        cmd.Parameters.AddWithValue("@symbol", TestSymbol);
        cmd.Parameters.AddWithValue("@level", (int)AlertLevel.Warning);
        cmd.Parameters.AddWithValue("@source", (int)AlertSource.PriceAlert);
        cmd.Parameters.AddWithValue("@title", title);
        cmd.Parameters.AddWithValue("@content", "测试内容");
        cmd.Parameters.AddWithValue("@createdAt", createdAtUtc.ToString("O", CultureInfo.InvariantCulture));
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> CountAsync(string sql)
    {
        await using var conn = new SqliteConnection($"Data Source={DbPath}");
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@symbol", TestSymbol);
        var result = await cmd.ExecuteScalarAsync();
        return result is long count ? (int)count : 0;
    }
}

