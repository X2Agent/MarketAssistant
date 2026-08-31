using System.Globalization;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// 统一告警中心服务：SQLite 持久化告警事件（alert_events 表），执行冷却合并、
/// 每小时配额、免打扰静默等抑制逻辑，按级别经 INotificationService 处理触达，
/// 并维护确认级交易联动门（AlertGate）的登记与解除。
/// </summary>
public sealed class AlertCenterService : SqliteServiceBase, IAlertCenterService
{
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private readonly AlertGate _alertGate;
    private readonly AlertSuppressionPolicy _policy = new();

    /// <summary>历史告警保留期：超期记录在启动初始化时裁剪，避免 alert_events 无界增长。</summary>
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(30);

    private readonly object _stateSync = new();
    private readonly Dictionary<string, AlertSuppressionPolicy.MergeState> _mergeStates =
        new(StringComparer.Ordinal);
    private AlertSuppressionPolicy.QuotaState _quotaState = AlertSuppressionPolicy.QuotaState.Empty;

    /// <summary>触发中的确认级告警：(来源, 市场, 标的) → 告警 Id，供告警解除时释放联动门。</summary>
    private readonly Dictionary<(AlertSource Source, MarketType MarketType, string Symbol), string>
        _activeGatedAlerts = new();

    /// <inheritdoc />
    public event Action<AlertEvent>? AlertRaised;

    /// <inheritdoc />
    public event Action? AlertsChanged;

    public AlertCenterService(
        INotificationService notificationService,
        IUserSettingService userSettingService,
        AlertGate alertGate,
        ILogger<AlertCenterService> logger)
        : base(logger)
    {
        _notificationService = notificationService;
        _userSettingService = userSettingService;
        _alertGate = alertGate;
    }

    /// <inheritdoc />
    public Task InitializeAsync(CancellationToken cancellationToken = default) =>
        EnsureInitializedAsync(InitializeDatabaseAsync);

    /// <inheritdoc />
    public async Task RaiseAlertAsync(AlertEvent alert, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alert);
        await EnsureInitializedAsync(InitializeDatabaseAsync);

        var settings = _userSettingService.CurrentSetting;
        AlertSuppressionPolicy.Decision decision;
        lock (_stateSync)
        {
            var mergeState = _mergeStates.GetValueOrDefault(alert.DedupeKey)
                             ?? AlertSuppressionPolicy.MergeState.Empty;
            decision = _policy.Evaluate(
                alert, mergeState, _quotaState, settings.AlertHourlyQuota, DateTime.UtcNow);
            _mergeStates[alert.DedupeKey] = decision.NewMergeState;
            _quotaState = decision.NewQuotaState;
        }

        // 命中合并窗口：并入最近一条同类记录（累计次数），不新增行
        var mergedAlertId = decision.IsMerge ? await FindLatestAlertIdAsync(alert, cancellationToken) : null;
        if (mergedAlertId != null)
            await IncrementOccurrenceAsync(mergedAlertId, alert.Content, cancellationToken);
        else
            await InsertAlertAsync(alert, cancellationToken);

        // 确认级交易联动：登记触发中的告警，TradeExecutor 据此强制人工确认
        if (alert.TradingImpact == AlertTradingImpact.RequireConfirmation)
        {
            lock (_stateSync)
            {
                _activeGatedAlerts[(alert.Source, alert.MarketType, AlertEvent.NormalizeSymbol(alert.Symbol))] =
                    mergedAlertId ?? alert.Id;
            }

            _alertGate.Activate(alert.MarketType, alert.Symbol);
        }

        // 触达抑制三重：每小时配额（policy 内判定）+ A 股休市静默 + 用户免打扰时段；
        // 均只压弹窗，告警仍落库、确认级交易联动门仍登记
        var isSessionSilenced = AlertSuppressionPolicy.IsSilencedByTradingSession(
            alert, AShareTradingHours.IsTradingSession());
        if (decision.ShouldNotify && !isSessionSilenced && settings.Notification && !IsInQuietHours(settings, alert.Level))
            Notify(alert);

        AlertRaised?.Invoke(alert);
        AlertsChanged?.Invoke();
    }

    /// <inheritdoc />
    public Task RaiseAlertClearedAsync(
        AlertSource source, MarketType marketType, string symbol, string title,
        CancellationToken cancellationToken = default)
    {
        var gateKey = (source, marketType, AlertEvent.NormalizeSymbol(symbol));
        lock (_stateSync)
        {
            if (_activeGatedAlerts.Remove(gateKey))
                _alertGate.Deactivate(marketType, symbol);

            // 条件已解除，清掉合并状态：下次重新进入区间可立即告警
            _mergeStates.Remove(AlertEvent.BuildDedupeKey(source, marketType, symbol, title));
        }

        AlertsChanged?.Invoke();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AlertEvent>> GetRecentAlertsAsync(
        int limit = 200, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        var alerts = new List<AlertEvent>();

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, market_type, symbol, level, source, title, content, created_at, is_read, trading_impact, occurrence_count
            FROM alert_events
            ORDER BY created_at DESC
            LIMIT @limit
            """;
        cmd.Parameters.AddWithValue("@limit", Math.Max(1, limit));

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            alerts.Add(new AlertEvent
            {
                Id = reader.GetString(0),
                MarketType = (MarketType)reader.GetInt32(1),
                Symbol = reader.GetString(2),
                Level = (AlertLevel)reader.GetInt32(3),
                Source = (AlertSource)reader.GetInt32(4),
                Title = reader.GetString(5),
                Content = reader.GetString(6),
                CreatedAt = DateTime.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                IsRead = reader.GetInt64(8) != 0,
                TradingImpact = (AlertTradingImpact)reader.GetInt32(9),
                OccurrenceCount = reader.GetInt32(10)
            });
        }

        return alerts;
    }

    /// <inheritdoc />
    public async Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM alert_events WHERE is_read = 0";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return result is long count ? (int)count : 0;
    }

    /// <inheritdoc />
    public async Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(InitializeDatabaseAsync);

        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE alert_events SET is_read = 1 WHERE is_read = 0";
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        AlertsChanged?.Invoke();
    }

    protected override async Task InitializeDatabaseAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS alert_events (
                id TEXT PRIMARY KEY,
                market_type INTEGER NOT NULL,
                symbol TEXT NOT NULL DEFAULT '',
                level INTEGER NOT NULL,
                source INTEGER NOT NULL,
                title TEXT NOT NULL,
                content TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                is_read INTEGER NOT NULL DEFAULT 0,
                trading_impact INTEGER NOT NULL DEFAULT 0,
                occurrence_count INTEGER NOT NULL DEFAULT 1
            );
            CREATE INDEX IF NOT EXISTS idx_alert_events_created ON alert_events(created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_alert_events_unread ON alert_events(is_read);
            """;
        await cmd.ExecuteNonQueryAsync();

        await PruneExpiredAlertsAsync();
    }

    /// <summary>
    /// 裁剪超过保留期的历史告警（随启动初始化执行一次）。
    /// created_at 以 ISO-8601 round-trip 文本存储，用 datetime() 解析后比较，
    /// 避免不同时区偏移写法下的字符串长度差异导致误判。
    /// </summary>
    private async Task PruneExpiredAlertsAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM alert_events WHERE datetime(created_at) < datetime(@cutoff)";
        cmd.Parameters.AddWithValue(
            "@cutoff", (DateTime.UtcNow - RetentionPeriod).ToString("O", CultureInfo.InvariantCulture));

        var deleted = await cmd.ExecuteNonQueryAsync();
        if (deleted > 0)
            Logger.LogInformation("已裁剪 {Count} 条超过 {Days} 天保留期的历史告警", deleted, RetentionPeriod.Days);
    }

    private async Task InsertAlertAsync(AlertEvent alert, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO alert_events (id, market_type, symbol, level, source, title, content, created_at, is_read, trading_impact, occurrence_count)
            VALUES (@id, @marketType, @symbol, @level, @source, @title, @content, @createdAt, @isRead, @tradingImpact, @occurrenceCount)
            """;
        FillParameters(cmd, alert);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task IncrementOccurrenceAsync(string alertId, string latestContent, CancellationToken cancellationToken)
    {
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            UPDATE alert_events
            SET occurrence_count = occurrence_count + 1, content = @content
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@content", latestContent);
        cmd.Parameters.AddWithValue("@id", alertId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>查找最近一条同类告警（合并目标）。失败时返回 null 退化为新增，不影响告警主链路。</summary>
    private async Task<string?> FindLatestAlertIdAsync(AlertEvent alert, CancellationToken cancellationToken)
    {
        try
        {
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id FROM alert_events
                WHERE source = @source AND market_type = @marketType AND symbol = @symbol AND title = @title
                ORDER BY created_at DESC
                LIMIT 1
                """;
            cmd.Parameters.AddWithValue("@source", (int)alert.Source);
            cmd.Parameters.AddWithValue("@marketType", (int)alert.MarketType);
            cmd.Parameters.AddWithValue("@symbol", AlertEvent.NormalizeSymbol(alert.Symbol));
            cmd.Parameters.AddWithValue("@title", alert.Title);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result as string;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "查找合并目标告警失败，将新增记录");
            return null;
        }
    }

    private static void FillParameters(SqliteCommand cmd, AlertEvent alert)
    {
        cmd.Parameters.AddWithValue("@id", alert.Id);
        cmd.Parameters.AddWithValue("@marketType", (int)alert.MarketType);
        cmd.Parameters.AddWithValue("@symbol", AlertEvent.NormalizeSymbol(alert.Symbol));
        cmd.Parameters.AddWithValue("@level", (int)alert.Level);
        cmd.Parameters.AddWithValue("@source", (int)alert.Source);
        cmd.Parameters.AddWithValue("@title", alert.Title);
        cmd.Parameters.AddWithValue("@content", alert.Content);
        cmd.Parameters.AddWithValue("@createdAt", alert.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@isRead", alert.IsRead ? 1 : 0);
        cmd.Parameters.AddWithValue("@tradingImpact", (int)alert.TradingImpact);
        cmd.Parameters.AddWithValue("@occurrenceCount", alert.OccurrenceCount);
    }

    /// <summary>按级别分发弹窗：Critical 最醒目且停留最久。</summary>
    private void Notify(AlertEvent alert)
    {
        var message = alert.OccurrenceCount > 1
            ? $"[{LevelText(alert.Level)}] {alert.Title} {alert.Content}（已发生 {alert.OccurrenceCount} 次）"
            : $"[{LevelText(alert.Level)}] {alert.Title} {alert.Content}";

        switch (alert.Level)
        {
            case AlertLevel.Critical:
                _notificationService.ShowError(message, durationMs: 15000);
                break;
            case AlertLevel.Warning:
                _notificationService.ShowWarning(message, durationMs: 10000);
                break;
            default:
                _notificationService.ShowInfo(message, durationMs: 5000);
                break;
        }
    }

    private static string LevelText(AlertLevel level) => level switch
    {
        AlertLevel.Critical => "紧急",
        AlertLevel.Warning => "警告",
        _ => "提示"
    };

    /// <summary>
    /// 免打扰时段判断（按本地时间）。Critical 不受免打扰限制，保证紧急告警必达。
    /// </summary>
    private static bool IsInQuietHours(UserSetting settings, AlertLevel level)
    {
        if (!settings.AlertQuietHoursEnabled || level == AlertLevel.Critical)
            return false;

        var start = settings.AlertQuietStartHour;
        var end = settings.AlertQuietEndHour;
        if (start == end)
            return false;

        var hour = DateTime.Now.Hour;
        return start < end
            ? hour >= start && hour < end
            : hour >= start || hour < end;
    }
}

