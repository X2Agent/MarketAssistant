using System.Globalization;
using System.Text.Json;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.DataProviders;
using MarketAssistant.DataProviders.AShare;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;
using static MarketAssistant.Infrastructure.Core.StockSymbolConverter;

namespace MarketAssistant.Applications.PriceAlert;

/// <summary>
/// 价格预警服务，监听 WebSocket 价格并触发通知。
/// 持久化通过 SQLite（market.db）实现，规则在启动时异步加载到内存。
/// </summary>
public sealed class PriceAlertService : SqliteServiceBase, IDisposable, IAsyncDisposable
{
    private static readonly TimeSpan ASharePollingInterval = TimeSpan.FromSeconds(20);

    private readonly BinanceWebSocketService _wsService;
    private readonly IAlertCenterService _alertCenterService;
    private readonly IUserSettingService _userSettingService;
    private readonly ClsQuoteClient _clsClient;

    private readonly object _syncRoot = new();
    private readonly object _initializationLock = new();
    private Task? _initializationTask;
    private List<PriceAlertRule> _rules = [];
    private readonly CancellationTokenSource _pollingCts = new();
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private Task? _pollingTask;
    private Task? _subscriptionTask;

    public IReadOnlyList<PriceAlertRule> Rules
    {
        get
        {
            lock (_syncRoot)
            {
                return _rules.ToList();
            }
        }
    }
    public event Action? RulesChanged;
    public event Action<PriceAlertRule>? RuleQuoteUpdated;

    public PriceAlertService(
        BinanceWebSocketService wsService,
        IAlertCenterService alertCenterService,
        IUserSettingService userSettingService,
        ClsQuoteClient clsClient,
        ILogger<PriceAlertService> logger)
        : base(logger)
    {
        _wsService = wsService;
        _alertCenterService = alertCenterService;
        _userSettingService = userSettingService;
        _clsClient = clsClient;

        _wsService.PriceUpdated += OnCryptoPriceUpdated;
        _pollingTask = Task.Run(() => PollASharePricesAsync(_pollingCts.Token));
    }

    /// <summary>
    /// 从数据库异步加载规则到内存，并订阅活跃的虚拟币规则。
    /// 应在应用启动时调用。
    /// </summary>
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        lock (_initializationLock)
        {
            // 初始化是共享单飞任务，内部固定用 None：
            // 首个调用者的令牌被固化进任务后，其取消会让后续所有调用方都拿到失败结果
            _initializationTask ??= InitializeCoreAsync(CancellationToken.None);
            return _initializationTask;
        }
    }

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        await LoadRulesAsync(cancellationToken);
        await RefreshCryptoSubscriptionSafeAsync();
    }

    private Task EnsureServiceInitializedAsync(CancellationToken cancellationToken) =>
        InitializeAsync(cancellationToken);

    protected override async Task InitializeDatabaseAsync()
    {
        await using var conn = await OpenConnectionAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS price_alert_rules (
                id TEXT PRIMARY KEY,
                asset_code TEXT NOT NULL,
                asset_name TEXT NOT NULL,
                market_type INTEGER NOT NULL,
                condition INTEGER NOT NULL,
                target_price REAL NOT NULL,
                is_one_time INTEGER NOT NULL DEFAULT 0,
                triggered INTEGER NOT NULL DEFAULT 0,
                enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL,
                max_trigger_count INTEGER NOT NULL DEFAULT 0,
                confirm_ticks INTEGER NOT NULL DEFAULT 0,
                confirm_seconds INTEGER NOT NULL DEFAULT 0,
                cooldown_minutes INTEGER NOT NULL DEFAULT 0,
                trading_impact INTEGER NOT NULL DEFAULT 0
            );
            CREATE INDEX IF NOT EXISTS idx_alert_mt ON price_alert_rules(market_type);
            CREATE INDEX IF NOT EXISTS idx_alert_enabled ON price_alert_rules(enabled, market_type);
            """;
        await cmd.ExecuteNonQueryAsync();

        // 兼容旧库：早期版本没有 is_one_time 列，此处补齐，旧规则默认按持续告警处理
        await using var checkCmd = conn.CreateCommand();
        checkCmd.CommandText = "PRAGMA table_info(price_alert_rules)";
        await using var reader = await checkCmd.ExecuteReaderAsync();
        var hasOneTimeColumn = false;
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), "is_one_time", StringComparison.OrdinalIgnoreCase))
            {
                hasOneTimeColumn = true;
                break;
            }
        }

        if (!hasOneTimeColumn)
        {
            await using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE price_alert_rules ADD COLUMN is_one_time INTEGER NOT NULL DEFAULT 0";
            await alterCmd.ExecuteNonQueryAsync();
        }

        // 新增列迁移：max_trigger_count / confirm_ticks / confirm_seconds / cooldown_minutes / trading_impact
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var columnsCmd = conn.CreateCommand();
        columnsCmd.CommandText = "PRAGMA table_info(price_alert_rules)";
        await using (var columnsReader = await columnsCmd.ExecuteReaderAsync())
        {
            while (await columnsReader.ReadAsync())
                existingColumns.Add(columnsReader.GetString(1));
        }

        var migrations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["max_trigger_count"] = "ALTER TABLE price_alert_rules ADD COLUMN max_trigger_count INTEGER NOT NULL DEFAULT 0",
            ["confirm_ticks"] = "ALTER TABLE price_alert_rules ADD COLUMN confirm_ticks INTEGER NOT NULL DEFAULT 0",
            ["confirm_seconds"] = "ALTER TABLE price_alert_rules ADD COLUMN confirm_seconds INTEGER NOT NULL DEFAULT 0",
            ["cooldown_minutes"] = "ALTER TABLE price_alert_rules ADD COLUMN cooldown_minutes INTEGER NOT NULL DEFAULT 0",
            ["trading_impact"] = "ALTER TABLE price_alert_rules ADD COLUMN trading_impact INTEGER NOT NULL DEFAULT 0"
        };

        foreach (var (column, alterSql) in migrations)
        {
            if (existingColumns.Contains(column))
                continue;

            await using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = alterSql;
            await alterCmd.ExecuteNonQueryAsync();
        }
    }

    public async Task AddRuleAsync(PriceAlertRule rule, CancellationToken cancellationToken = default)
    {
        await EnsureServiceInitializedAsync(cancellationToken);
        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO price_alert_rules (id, asset_code, asset_name, market_type, condition, target_price, is_one_time, triggered, enabled, created_at, max_trigger_count, confirm_ticks, confirm_seconds, cooldown_minutes, trading_impact)
            VALUES (@id, @assetCode, @assetName, @marketType, @condition, @targetPrice, @isOneTime, @triggered, @enabled, @createdAt, @maxTriggerCount, @confirmTicks, @confirmSeconds, @cooldownMinutes, @tradingImpact)
            """;
        cmd.Parameters.AddWithValue("@id", rule.Id);
        cmd.Parameters.AddWithValue("@assetCode", rule.AssetCode);
        cmd.Parameters.AddWithValue("@assetName", rule.AssetName);
        cmd.Parameters.AddWithValue("@marketType", (int)rule.MarketType);
        cmd.Parameters.AddWithValue("@condition", (int)rule.Condition);
        cmd.Parameters.AddWithValue("@targetPrice", (double)rule.TargetPrice);
        cmd.Parameters.AddWithValue("@isOneTime", rule.IsOneTime ? 1 : 0);
        cmd.Parameters.AddWithValue("@triggered", rule.Triggered ? 1 : 0);
        cmd.Parameters.AddWithValue("@enabled", rule.Enabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@createdAt", rule.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@maxTriggerCount", rule.MaxTriggerCount);
        cmd.Parameters.AddWithValue("@confirmTicks", rule.ConfirmTicks);
        cmd.Parameters.AddWithValue("@confirmSeconds", rule.ConfirmSeconds);
        cmd.Parameters.AddWithValue("@cooldownMinutes", rule.CooldownMinutes);
        cmd.Parameters.AddWithValue("@tradingImpact", (int)rule.TradingImpact);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        lock (_syncRoot)
        {
            _rules.Add(rule);
        }

        RulesChanged?.Invoke();
        QueueCryptoSubscriptionRefresh();
    }

    public async Task RemoveRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        await EnsureServiceInitializedAsync(cancellationToken);

        PriceAlertRule? rule;
        lock (_syncRoot)
        {
            rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        }

        if (rule == null)
            return;

        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM price_alert_rules WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", ruleId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        lock (_syncRoot)
        {
            _rules.RemoveAll(r => r.Id == ruleId);
        }

        // 规则删除后不再跟踪其条件区间，必须释放可能残留的确认级联动门，
        // 否则该标的的 AI 交易信号会被永久强制人工确认（仅重启可解）
        if (rule.TradingImpact == AlertTradingImpact.RequireConfirmation)
            await RaiseAlertClearedSafeAsync(rule);

        RulesChanged?.Invoke();
        QueueCryptoSubscriptionRefresh();
    }

    public async Task ToggleRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        await EnsureServiceInitializedAsync(cancellationToken);

        PriceAlertRule? rule;
        bool newEnabled;
        lock (_syncRoot)
        {
            rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;

            newEnabled = !rule.Enabled;
        }

        await EnsureInitializedAsync(InitializeDatabaseAsync);
        await using var conn = await OpenConnectionAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE price_alert_rules SET enabled = @enabled, triggered = 0 WHERE id = @id";
        cmd.Parameters.AddWithValue("@enabled", newEnabled ? 1 : 0);
        cmd.Parameters.AddWithValue("@id", ruleId);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        lock (_syncRoot)
        {
            rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;

            rule.Enabled = newEnabled;
            rule.Triggered = false;
        }

        RulesChanged?.Invoke();
        QueueCryptoSubscriptionRefresh();
    }

    private void OnCryptoPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        List<PriceAlertRule> rules;
        lock (_syncRoot)
        {
            rules = _rules
                .Where(r => r.MarketType == MarketType.Crypto && r.Enabled)
                .ToList();
        }

        foreach (var rule in rules)
        {
            var ruleSymbol = ToBinanceFormat(rule.AssetCode);
            if (!ruleSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            UpdateRuleQuoteAndEvaluate(rule.Id, lastPrice, changePercent);
        }
    }

    private async Task PollASharePricesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(ASharePollingInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                List<PriceAlertRule> rules;
                lock (_syncRoot)
                {
                    rules = _rules
                        .Where(r => r.MarketType == MarketType.AShare && r.Enabled)
                        .ToList();
                }

                // 非交易时段仅评估确认级规则：休市行情无变化，普通规则反复触发无行动价值且浪费轮询请求；
                // 确认级规则映射为 Critical 告警，需保证交易联动门及时登记（弹窗静默由告警中心统一处理）
                if (!AShareTradingHours.IsTradingSession())
                    rules = rules
                        .Where(r => r.TradingImpact == AlertTradingImpact.RequireConfirmation)
                        .ToList();

                if (rules.Count == 0)
                    continue;

                foreach (var rule in rules)
                {
                    var quote = await GetAShareLatestQuoteAsync(rule.AssetCode, cancellationToken);
                    if (quote is not null)
                        UpdateRuleQuoteAndEvaluate(rule.Id, quote.Value.Price, quote.Value.ChangePercent);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "A股预警轮询异常");
        }
    }

    /// <summary>
    /// 获取A股实时行情（财联社 CLS 行情接口，与搜索/详情数据源一致）。
    /// 注意 CLS 的 change 为小数比率（如 -0.0082 表示 -0.82%）。
    /// </summary>
    private async Task<(decimal Price, decimal? ChangePercent)?> GetAShareLatestQuoteAsync(string assetCode, CancellationToken cancellationToken)
    {
        try
        {
            var clsCode = ToClsFormat(assetCode);
            if (string.IsNullOrWhiteSpace(clsCode))
                return null;

            // HTTP 访问与容错解析由 ClsQuoteClient 负责
            var data = await _clsClient.GetStockQuoteAsync(clsCode, "last_px,change", cancellationToken);
            // 非正价格（停牌/无成交/数据缺失）一律拒绝参与预警评估，
            // 否则 0 恒小于正目标价会对停牌股持续误报"跌破"
            if (data is null || data.LastPrice <= 0)
                return null;

            decimal? changePercent = data.Change != 0 ? data.Change * 100 : null;

            return (data.LastPrice, changePercent);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "获取A股价格失败: {AssetCode}", assetCode);
            return null;
        }
    }

    private void UpdateRuleQuoteAndEvaluate(string ruleId, decimal lastPrice, decimal? changePercent = null)
    {
        PriceAlertRule? rule;
        bool shouldNotify;
        bool shouldClearGate;
        bool autoDisabled;
        lock (_syncRoot)
        {
            rule = _rules.FirstOrDefault(r => r.Id == ruleId && r.Enabled);
            if (rule == null) return;

            rule.UpdateQuote(lastPrice, changePercent, DateTime.UtcNow);
            shouldNotify = rule.UpdateTriggerState(lastPrice, changePercent);
            if (shouldNotify)
                rule.NotifyTriggered();

            // 条件离开区间：确认级告警释放交易联动门（AI 信号恢复正常决策路径）
            shouldClearGate = rule.ShouldClearTradingGate;
            if (shouldClearGate)
                rule.ResetTradingGate();

            // 一次性告警首次触发后自动停用；重新启用需用户手动操作开关
            autoDisabled = shouldNotify && rule.IsOneTime;
            if (autoDisabled)
                rule.Enabled = false;
        }

        RuleQuoteUpdated?.Invoke(rule);

        if (shouldClearGate)
            _ = RaiseAlertClearedSafeAsync(rule);

        if (autoDisabled)
        {
            // 一次性规则触发后即停用，不再跟踪条件区间：同步释放联动门，避免残留阻塞该标的自动交易
            if (!shouldClearGate && rule.TradingImpact == AlertTradingImpact.RequireConfirmation)
                _ = RaiseAlertClearedSafeAsync(rule);

            _ = PersistRuleDisabledAsync(ruleId);
            QueueCryptoSubscriptionRefresh();
        }

        if (!shouldNotify)
            return;

        _ = RaiseAlertSafeAsync(rule, lastPrice, changePercent);

        RulesChanged?.Invoke();
    }

    /// <summary>确认级告警条件解除后释放交易联动门（fire-and-forget，异常仅记录）。</summary>
    private async Task RaiseAlertClearedSafeAsync(PriceAlertRule rule)
    {
        try
        {
            await _alertCenterService.RaiseAlertClearedAsync(
                AlertSource.PriceAlert, rule.MarketType, rule.AssetCode, BuildAlertTitle(rule));
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "解除价格告警交易联动失败: {RuleId}", rule.Id);
        }
    }

    /// <summary>价格告警统一上报告警中心（fire-and-forget，异常仅记录，不阻断行情链路）。</summary>
    private async Task RaiseAlertSafeAsync(PriceAlertRule rule, decimal lastPrice, decimal? changePercent)
    {
        try
        {
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = rule.MarketType,
                Symbol = rule.AssetCode,
                Level = rule.TradingImpact == AlertTradingImpact.RequireConfirmation
                    ? AlertLevel.Critical
                    : AlertLevel.Warning,
                Source = AlertSource.PriceAlert,
                Title = BuildAlertTitle(rule),
                Content = BuildAlertContent(rule, lastPrice, changePercent),
                TradingImpact = rule.TradingImpact
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "上报价格告警失败: {RuleId}", rule.Id);
        }
    }

    private static string BuildAlertTitle(PriceAlertRule rule)
    {
        var targetText = rule.IsPercentCondition ? $"{rule.TargetPrice:N2}%" : rule.TargetPrice.ToString("N2");
        return $"{rule.AssetName}({rule.AssetCode}) {ConditionText(rule.Condition)} {targetText}";
    }

    private static string ConditionText(AlertCondition condition) => condition switch
    {
        AlertCondition.PriceAbove => "涨破",
        AlertCondition.PriceBelow => "跌破",
        AlertCondition.ChangePercentAbove => "涨幅超过",
        AlertCondition.ChangePercentBelow => "跌幅超过",
        _ => "达到"
    };

    private static string BuildAlertContent(PriceAlertRule rule, decimal lastPrice, decimal? changePercent)
    {
        var valueText = rule.IsPercentCondition && changePercent.HasValue
            ? $"{changePercent.Value:N2}%"
            : lastPrice.ToString("N2");
        return $"当前 {valueText}，已进入告警区间";
    }

    private void QueueCryptoSubscriptionRefresh()
    {
        var nextTask = RefreshCryptoSubscriptionSafeAsync();
        lock (_syncRoot)
        {
            _subscriptionTask = nextTask;
        }
    }

    private async Task RefreshCryptoSubscriptionSafeAsync()
    {
        await _subscriptionLock.WaitAsync(_pollingCts.Token);
        try
        {
            List<string> symbols;
            lock (_syncRoot)
            {
                symbols = _rules
                    .Where(r => r.MarketType == MarketType.Crypto && r.Enabled)
                    .Select(r => ToBinanceFormat(r.AssetCode))
                    .Distinct()
                    .ToList();
            }

            await _wsService.SubscribeAsync(WebSocketSubscriberKeys.PriceAlerts, symbols);
        }
        catch (OperationCanceledException) when (_pollingCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "WebSocket 订阅失败，价格预警可能无法实时推送");
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }

    /// <summary>
    /// 一次性告警触发后持久化停用状态，避免应用重启后规则再次生效。
    /// </summary>
    private async Task PersistRuleDisabledAsync(string ruleId)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE price_alert_rules SET enabled = 0, triggered = 1 WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", ruleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "持久化一次性预警停用状态失败: {RuleId}", ruleId);
        }
    }

    private async Task LoadRulesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, asset_code, asset_name, market_type, condition, target_price, is_one_time, triggered, enabled, created_at, max_trigger_count, confirm_ticks, confirm_seconds, cooldown_minutes, trading_impact
                FROM price_alert_rules
                """;

            var allRules = new List<PriceAlertRule>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                allRules.Add(new PriceAlertRule
                {
                    Id = reader.GetString(0),
                    AssetCode = reader.GetString(1),
                    AssetName = reader.GetString(2),
                    MarketType = (MarketType)reader.GetInt32(3),
                    Condition = (AlertCondition)reader.GetInt32(4),
                    TargetPrice = (decimal)reader.GetDouble(5),
                    IsOneTime = reader.GetInt32(6) != 0,
                    Triggered = false,
                    Enabled = reader.GetInt32(8) != 0,
                    CreatedAt = DateTime.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    MaxTriggerCount = reader.GetInt32(10),
                    ConfirmTicks = reader.GetInt32(11),
                    ConfirmSeconds = reader.GetInt32(12),
                    CooldownMinutes = reader.GetInt32(13),
                    TradingImpact = (AlertTradingImpact)reader.GetInt32(14)
                });
            }

            lock (_syncRoot)
            {
                _rules = allRules;
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "加载价格预警规则失败");
            lock (_syncRoot)
            {
                _rules = [];
            }
        }
    }

    /// <summary>
    /// 异步释放：取消轮询并等待后台任务收尾，超时（5 秒）后不再等待；
    /// OperationCanceledException 属正常取消路径，静默处理，其余异常记录后不重抛。
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _wsService.PriceUpdated -= OnCryptoPriceUpdated;
        _ = _wsService.UnsubscribeAllAsync(WebSocketSubscriberKeys.PriceAlerts);
        _pollingCts.Cancel();
        try
        {
            await Task.WhenAll(_pollingTask ?? Task.CompletedTask, _subscriptionTask ?? Task.CompletedTask)
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // 正常取消路径
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "价格预警服务后台任务清理异常（已忽略）");
        }

        _subscriptionLock.Dispose();
        _pollingCts.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 同步释放：与异步释放相同的清理逻辑，但不再向调用方重抛非取消异常，
    /// 避免 Dispose 在应用退出时抛出导致终止流程失败。
    /// </summary>
    public void Dispose()
    {
        _wsService.PriceUpdated -= OnCryptoPriceUpdated;
        _ = _wsService.UnsubscribeAllAsync(WebSocketSubscriberKeys.PriceAlerts);
        _pollingCts.Cancel();
        try
        {
            Task.WhenAll(_pollingTask ?? Task.CompletedTask, _subscriptionTask ?? Task.CompletedTask)
                .Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex)
        {
            // 取消属正常路径；其余异常仅记录，不再重抛（同步 Dispose 中重抛会中断应用退出）
            ex.Handle(e => e is OperationCanceledException);
            foreach (var inner in ex.InnerExceptions.Where(e => e is not OperationCanceledException))
                Logger.LogWarning(inner, "价格预警服务后台任务清理异常（已忽略）");
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "价格预警服务后台任务清理异常（已忽略）");
        }

        _subscriptionLock.Dispose();
        _pollingCts.Dispose();
        GC.SuppressFinalize(this);
    }
}
