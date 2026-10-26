using System.Globalization;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.Charts.Models;
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
    private readonly AlertRuleSymbolResolver _symbolResolver;

    private readonly object _syncRoot = new();
    private readonly object _initializationLock = new();
    private Task? _initializationTask;
    private List<PriceAlertRule> _rules = [];
    private readonly CancellationTokenSource _pollingCts = new();
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private Task? _pollingTask;
    private Task? _subscriptionTask;

    /// <summary>
    /// 自选股批量规则的运行时状态：(规则ID, 符号) → 规则副本。
    /// 持久化规则只是模板；每个符号需要独立的去抖/冷却/触发计数运行态。
    /// </summary>
    private readonly Dictionary<(string RuleId, string Symbol), PriceAlertRule> _scopedRuleStates = [];

    public IReadOnlyList<PriceAlertRule> Rules
    {
        get
        {
            lock (_syncRoot)
            {
                return [.. _rules];
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
        AlertRuleSymbolResolver symbolResolver,
        ILogger<PriceAlertService> logger)
        : base(logger)
    {
        _wsService = wsService;
        _alertCenterService = alertCenterService;
        _userSettingService = userSettingService;
        _clsClient = clsClient;
        _symbolResolver = symbolResolver;

        _wsService.PriceUpdated += OnCryptoPriceUpdated;
        WeakReferenceMessenger.Default.Register<AssetFavoritesChanged>(this, OnFavoritesChanged);
        _pollingTask = Task.Run(() => PollASharePricesAsync(_pollingCts.Token));
    }

    private void OnFavoritesChanged(object recipient, AssetFavoritesChanged message)
    {
        _symbolResolver.InvalidateCache();
        _ = RefreshWatchlistOnFavoritesChangedAsync();
    }

    private async Task RefreshWatchlistOnFavoritesChangedAsync()
    {
        await _symbolResolver.WarmWatchlistCacheAsync(MarketType.Crypto, _pollingCts.Token);
        await _symbolResolver.WarmWatchlistCacheAsync(MarketType.AShare, _pollingCts.Token);
        await RefreshCryptoSubscriptionSafeAsync();
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
        // 预热自选股缓存：首批价格 tick 到达时批量规则即可匹配，不必等冷缓存预热
        await _symbolResolver.WarmWatchlistCacheAsync(MarketType.Crypto, cancellationToken);
        await _symbolResolver.WarmWatchlistCacheAsync(MarketType.AShare, cancellationToken);
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
        // 规则作用域（单标的/自选股批量）与指标规则所用 K 线周期
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
            ["trading_impact"] = "ALTER TABLE price_alert_rules ADD COLUMN trading_impact INTEGER NOT NULL DEFAULT 0",
            ["scope"] = "ALTER TABLE price_alert_rules ADD COLUMN scope INTEGER NOT NULL DEFAULT 0",
            ["kline_type"] = "ALTER TABLE price_alert_rules ADD COLUMN kline_type INTEGER NOT NULL DEFAULT 2"
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
            INSERT INTO price_alert_rules (id, asset_code, asset_name, market_type, condition, target_price, is_one_time, triggered, enabled, created_at, max_trigger_count, confirm_ticks, confirm_seconds, cooldown_minutes, trading_impact, scope, kline_type)
            VALUES (@id, @assetCode, @assetName, @marketType, @condition, @targetPrice, @isOneTime, @triggered, @enabled, @createdAt, @maxTriggerCount, @confirmTicks, @confirmSeconds, @cooldownMinutes, @tradingImpact, @scope, @klineType)
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
        cmd.Parameters.AddWithValue("@scope", (int)rule.Scope);
        cmd.Parameters.AddWithValue("@klineType", (int)rule.KLineType);
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

        var removedScoped = RemoveScopedRuleStates(ruleId);

        // 规则删除后不再跟踪其条件区间，必须释放可能残留的确认级联动门，
        // 否则该标的的 AI 交易信号会被永久强制人工确认（仅重启可解）
        if (rule.TradingImpact == AlertTradingImpact.RequireConfirmation)
            await ClearRuleTradingGatesAsync(rule, removedScoped);

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

        // 停用时清理批量规则的符号运行时状态，避免自选股集合外的状态残留
        var removedScoped = !newEnabled ? RemoveScopedRuleStates(ruleId) : [];

        // 禁用确认级规则时释放交易联动门：规则已不再跟踪条件区间，
        // 否则该标的 AI 交易信号会被永久强制人工确认（仅重启可解）
        if (!newEnabled && rule.TradingImpact == AlertTradingImpact.RequireConfirmation)
            await ClearRuleTradingGatesAsync(rule, removedScoped);

        RulesChanged?.Invoke();
        QueueCryptoSubscriptionRefresh();
    }

    private void OnCryptoPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        List<PriceAlertRule> priceRules;
        lock (_syncRoot)
        {
            // 指标类规则由 IndicatorAlertEvaluator 周期评估，不走价格 tick 路径
            priceRules = [.. _rules.Where(r =>
                r.MarketType == MarketType.Crypto && r.Enabled && !r.IsIndicatorCondition)];
        }

        foreach (var rule in priceRules)
        {
            if (rule.Scope == AlertScope.Watchlist)
            {
                // 批量规则按缓存的自选股集合匹配；冷缓存时预热并跳过本次 tick
                var watchlist = _symbolResolver.GetCachedWatchlistSymbols(MarketType.Crypto);
                if (watchlist == null)
                {
                    _ = _symbolResolver.WarmWatchlistCacheAsync(MarketType.Crypto);
                    continue;
                }

                if (!watchlist.Any(code => ToBinanceFormat(code).Equals(symbol, StringComparison.OrdinalIgnoreCase)))
                    continue;

                EvaluateScopedPriceRule(rule, symbol, lastPrice, changePercent);
                continue;
            }

            if (!ToBinanceFormat(rule.AssetCode).Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            UpdateRuleQuoteAndEvaluate(rule.Id, lastPrice, changePercent);
        }
    }

    /// <summary>
    /// 评估自选股批量规则的单个符号：取（或新建）该符号的运行时副本后按既有状态机评估，
    /// 每个符号的去抖/冷却/触发计数互相独立。
    /// </summary>
    private void EvaluateScopedPriceRule(PriceAlertRule template, string symbol, decimal lastPrice, decimal? changePercent)
    {
        var scoped = ResolveScopedRule(template, symbol);
        if (scoped == null)
            return;

        EvaluateRule(scoped, lastPrice, changePercent, metric: null);
    }

    /// <summary>取（或新建）批量规则在某符号下的运行时副本；规则已停用返回 null。</summary>
    private PriceAlertRule? ResolveScopedRule(PriceAlertRule template, string symbol)
    {
        if (template.Scope != AlertScope.Watchlist)
            return template.Enabled ? template : null;

        var normalized = AlertEvent.NormalizeSymbol(symbol);
        lock (_syncRoot)
        {
            var key = (template.Id, normalized);
            if (!_scopedRuleStates.TryGetValue(key, out var scoped))
            {
                scoped = template.CloneForSymbol(symbol);
                _scopedRuleStates[key] = scoped;
            }

            scoped.Enabled = template.Enabled;
            return scoped.Enabled ? scoped : null;
        }
    }

    /// <summary>清理某条批量规则的全部符号运行时状态（规则删除/停用时调用，避免字典无限增长）。</summary>
    private List<PriceAlertRule> RemoveScopedRuleStates(string ruleId)
    {
        var removed = new List<PriceAlertRule>();
        lock (_syncRoot)
        {
            var keys = _scopedRuleStates.Keys.Where(key => key.RuleId == ruleId).ToList();
            foreach (var key in keys)
            {
                if (_scopedRuleStates.Remove(key, out var scoped))
                    removed.Add(scoped);
            }
        }
        return removed;
    }

    /// <summary>
    /// 删除或停用确认级规则时释放联动门：自选股批量规则按实际登记的每个符号副本逐一释放，
    /// 单标的规则直接按规则本体释放。
    /// </summary>
    private async Task ClearRuleTradingGatesAsync(PriceAlertRule rule, List<PriceAlertRule> removedScoped)
    {
        if (rule.Scope == AlertScope.Watchlist)
        {
            foreach (var scoped in removedScoped)
                await RaiseAlertClearedSafeAsync(scoped);
            return;
        }

        await RaiseAlertClearedSafeAsync(rule);
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
                    // 指标类规则由 IndicatorAlertEvaluator 周期评估，不走轮询路径
                    rules = [.. _rules.Where(r =>
                        r.MarketType == MarketType.AShare && r.Enabled && !r.IsIndicatorCondition)];
                }

                // 非交易时段仅评估确认级规则：休市行情无变化，普通规则反复触发无行动价值且浪费轮询请求；
                // 确认级规则映射为 Critical 告警，需保证交易联动门及时登记（弹窗静默由告警中心统一处理）
                if (!AShareTradingHours.IsTradingSession())
                    rules = [.. rules.Where(r => r.TradingImpact == AlertTradingImpact.RequireConfirmation)];

                if (rules.Count == 0)
                    continue;

                // 自选股批量规则在此展开为 (规则, 符号) 对；单标的规则取其 AssetCode
                var evaluations = new List<(PriceAlertRule Rule, string Symbol)>();
                foreach (var rule in rules)
                {
                    if (rule.Scope != AlertScope.Watchlist)
                    {
                        evaluations.Add((rule, rule.AssetCode));
                        continue;
                    }

                    try
                    {
                        var symbols = await _symbolResolver.ResolveWatchlistSymbolsAsync(
                            MarketType.AShare, cancellationToken).ConfigureAwait(false);
                        evaluations.AddRange(symbols.Select(symbol => (rule, symbol)));
                    }
                    catch (Exception ex)
                    {
                        // 自选股查询失败按规则隔离：跳过该规则本轮评估，不影响其它规则
                        Logger.LogWarning(ex, "展开自选股告警规则失败，本轮跳过: {RuleId}", rule.Id);
                    }
                }

                foreach (var (rule, symbol) in evaluations)
                {
                    var quote = await GetAShareLatestQuoteAsync(symbol, cancellationToken);
                    if (quote is not null)
                    {
                        if (rule.Scope == AlertScope.Watchlist)
                            EvaluateScopedPriceRule(rule, symbol, quote.Value.Price, quote.Value.ChangePercent);
                        else
                            UpdateRuleQuoteAndEvaluate(rule.Id, quote.Value.Price, quote.Value.ChangePercent);
                    }
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
            // 非正价格（停牌/无成交/数据缺失，CLS 以 null 或 0 表达）一律拒绝参与预警评估，
            // 否则 0 恒小于正目标价会对停牌股持续误报"跌破"
            if (data is null || data.LastPrice is not { } lastPrice || lastPrice <= 0)
                return null;

            decimal? changePercent = data.Change is { } change && change != 0 ? change * 100 : null;

            return (lastPrice, changePercent);
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
        lock (_syncRoot)
        {
            rule = _rules.FirstOrDefault(r => r.Id == ruleId && r.Enabled);
        }

        if (rule == null)
            return;

        EvaluateRule(rule, lastPrice, changePercent, metric: null);
    }

    /// <summary>
    /// 指标规则单符号评估（由 <see cref="IndicatorAlertEvaluator"/> 周期调用）：
    /// 对指定符号用度量快照更新该规则的触发状态并上报告警。
    /// </summary>
    public Task EvaluateIndicatorRuleAsync(
        PriceAlertRule rule, string symbol, AlertRuleMetric metric, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(metric);

        var scoped = ResolveScopedRule(rule, symbol);
        if (scoped == null)
            return Task.CompletedTask;

        EvaluateRule(scoped, metric.ClosePrice ?? 0m, changePercent: null, metric);
        return Task.CompletedTask;
    }

    /// <summary>指标规则本轮评估结束：清理已不属于自选股集合的运行时状态。</summary>
    public void CompleteIndicatorRuleEvaluation(PriceAlertRule rule, IReadOnlyList<string> symbols)
        => PruneScopedRuleStates(rule, symbols);

    /// <summary>清理批量规则中已不属于当前符号集合的运行时状态（自选股移除后防止字典无限增长）。</summary>
    private void PruneScopedRuleStates(PriceAlertRule template, IReadOnlyList<string> symbols)
    {
        if (template.Scope != AlertScope.Watchlist)
            return;

        var active = symbols.Select(AlertEvent.NormalizeSymbol).ToHashSet(StringComparer.Ordinal);
        var pruned = new List<PriceAlertRule>();
        lock (_syncRoot)
        {
            var stale = _scopedRuleStates.Keys
                .Where(key => key.RuleId == template.Id && !active.Contains(key.Symbol))
                .ToList();
            foreach (var key in stale)
            {
                if (_scopedRuleStates.Remove(key, out var scoped))
                    pruned.Add(scoped);
            }
        }

        if (template.TradingImpact == AlertTradingImpact.RequireConfirmation)
        {
            foreach (var scoped in pruned)
                _ = RaiseAlertClearedSafeAsync(scoped);
        }
    }

    /// <summary>
    /// 规则评估核心：更新行情与触发状态，按需上报告警、解除交易联动门、停用一次性规则。
    /// 价格与指标两类条件共用同一状态机（去抖 / 冷却 / 限次 / 联动门）。
    /// </summary>
    private void EvaluateRule(PriceAlertRule rule, decimal price, decimal? changePercent, AlertRuleMetric? metric)
    {
        bool shouldNotify;
        bool shouldClearGate;
        bool autoDisabled;
        lock (_syncRoot)
        {
            rule.UpdateQuote(price, changePercent, DateTime.UtcNow);
            shouldNotify = rule.UpdateTriggerState(price, changePercent, metric);
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

        // 批量规则的符号副本不向 UI 回写单一符号的行情（一行代表多个符号）
        if (!rule.IsScopedClone)
            RuleQuoteUpdated?.Invoke(rule);

        if (shouldClearGate)
            _ = RaiseAlertClearedSafeAsync(rule);

        if (autoDisabled)
        {
            // 一次性规则触发后即停用，不再跟踪条件区间。其确认级告警在上报时已按 IsOneTime
            // 降为 None、未登记联动门，无需在此释放；仅持久化停用状态并刷新订阅
            _ = PersistRuleDisabledAsync(rule.Id);
            QueueCryptoSubscriptionRefresh();
        }

        if (!shouldNotify)
            return;

        _ = RaiseAlertSafeAsync(rule, price, changePercent, metric);

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
    private async Task RaiseAlertSafeAsync(
        PriceAlertRule rule, decimal lastPrice, decimal? changePercent, AlertRuleMetric? metric = null)
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
                Content = metric != null
                    ? BuildIndicatorAlertContent(rule, metric)
                    : BuildAlertContent(rule, lastPrice, changePercent),
                // 一次性规则触发即停用，不存在"持续触发中"状态，不登记交易联动门，
                // 否则门随规则停用后永不释放，该标的 AI 信号会被永久强制人工确认
                TradingImpact = rule.IsOneTime ? AlertTradingImpact.None : rule.TradingImpact
            });
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "上报价格告警失败: {RuleId}", rule.Id);
        }
    }

    private static string BuildAlertTitle(PriceAlertRule rule)
    {
        var conditionText = ConditionText(rule.Condition);
        // 指标类条件中，交叉（金叉/死叉）与突破（布林上下轨）没有数值目标，标题只保留条件语义
        if (rule.IsIndicatorCondition && !HasNumericTarget(rule.Condition))
            return $"{rule.AssetName}({rule.AssetCode}) {conditionText}";

        var targetText = rule.IsPercentCondition ? $"{rule.TargetPrice:N2}%" : rule.TargetPrice.ToString("N2");
        return $"{rule.AssetName}({rule.AssetCode}) {conditionText} {targetText}";
    }

    /// <summary>指标条件是否携带数值目标（量比倍数 / RSI 阈值）。</summary>
    private static bool HasNumericTarget(AlertCondition condition) => condition is
        AlertCondition.VolumeRatioAbove or AlertCondition.RsiAbove or AlertCondition.RsiBelow;

    private static string ConditionText(AlertCondition condition) => condition switch
    {
        AlertCondition.PriceAbove => "涨破",
        AlertCondition.PriceBelow => "跌破",
        AlertCondition.ChangePercentAbove => "涨幅超过",
        AlertCondition.ChangePercentBelow => "跌幅超过",
        AlertCondition.VolumeRatioAbove => "量比超过",
        AlertCondition.MacdGoldenCross => "MACD金叉",
        AlertCondition.MacdDeadCross => "MACD死叉",
        AlertCondition.RsiAbove => "RSI超过",
        AlertCondition.RsiBelow => "RSI低于",
        AlertCondition.KdjGoldenCross => "KDJ金叉",
        AlertCondition.KdjDeadCross => "KDJ死叉",
        AlertCondition.BollUpperBreak => "突破布林上轨",
        AlertCondition.BollLowerBreak => "跌破布林下轨",
        AlertCondition.MaGoldenCross => "MA金叉",
        AlertCondition.MaDeadCross => "MA死叉",
        _ => "达到"
    };

    private static string BuildAlertContent(PriceAlertRule rule, decimal lastPrice, decimal? changePercent)
    {
        var valueText = rule.IsPercentCondition && changePercent.HasValue
            ? $"{changePercent.Value:N2}%"
            : lastPrice.ToString("N2");
        return $"当前 {valueText}，已进入告警区间";
    }

    /// <summary>指标类告警内容：给出触发时的实际指标读数与所用 K 线周期。</summary>
    private static string BuildIndicatorAlertContent(PriceAlertRule rule, AlertRuleMetric metric)
    {
        var period = rule.KLinePeriodText;
        return rule.Condition switch
        {
            AlertCondition.VolumeRatioAbove =>
                $"当前量比 {metric.VolumeRatio:N2}，已超过 {rule.TargetPrice:N2}（{period}）",
            AlertCondition.RsiAbove =>
                $"当前 RSI {metric.Rsi:N1}，已超过 {rule.TargetPrice:N0}（{period}）",
            AlertCondition.RsiBelow =>
                $"当前 RSI {metric.Rsi:N1}，已低于 {rule.TargetPrice:N0}（{period}）",
            AlertCondition.MacdGoldenCross or AlertCondition.MacdDeadCross =>
                $"MACD DIFF {metric.MacdDiff:N4} / DEA {metric.MacdDea:N4}（{period}）",
            AlertCondition.KdjGoldenCross or AlertCondition.KdjDeadCross =>
                $"KDJ K {metric.KdjK:N2} / D {metric.KdjD:N2}（{period}）",
            AlertCondition.MaGoldenCross or AlertCondition.MaDeadCross =>
                $"MA5 {metric.MaShort:N2} / MA10 {metric.MaLong:N2}（{period}）",
            AlertCondition.BollUpperBreak =>
                $"收盘 {metric.ClosePrice:N2} 突破布林上轨 {metric.BollUpper:N2}（{period}）",
            AlertCondition.BollLowerBreak =>
                $"收盘 {metric.ClosePrice:N2} 跌破布林下轨 {metric.BollLower:N2}（{period}）",
            _ => $"指标条件已满足（{period}）"
        };
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
            List<PriceAlertRule> cryptoRules;
            lock (_syncRoot)
            {
                // 指标类规则不依赖 WS 价格推送（由周期评估器按 K 线取数），无需订阅
                cryptoRules = [.. _rules.Where(r =>
                    r.MarketType == MarketType.Crypto && r.Enabled && !r.IsIndicatorCondition)];
            }

            IReadOnlyList<string> symbols;
            try
            {
                symbols = await _symbolResolver
                    .ResolveAllSymbolsAsync(MarketType.Crypto, cryptoRules, _pollingCts.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 自选股展开失败时退化为仅订阅单标的规则：批量规则本轮无行情，
                // 但绝不能让整个价格告警的 WS 订阅一起失败
                Logger.LogWarning(ex, "展开自选股告警符号失败，本次仅订阅单标的规则");
                symbols = [.. cryptoRules
                    .Where(r => r.Scope != AlertScope.Watchlist)
                    .Select(r => r.AssetCode)];
            }

            var binanceSymbols = symbols
                .Select(symbol => ToBinanceFormat(symbol))
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            await _wsService.SubscribeAsync(WebSocketSubscriberKeys.PriceAlerts, binanceSymbols);
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
                SELECT id, asset_code, asset_name, market_type, condition, target_price, is_one_time, triggered, enabled, created_at, max_trigger_count, confirm_ticks, confirm_seconds, cooldown_minutes, trading_impact, scope, kline_type
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
                    TradingImpact = (AlertTradingImpact)reader.GetInt32(14),
                    Scope = (AlertScope)reader.GetInt32(15),
                    KLineType = (KLineType)reader.GetInt32(16)
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
        WeakReferenceMessenger.Default.UnregisterAll(this);
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
        WeakReferenceMessenger.Default.UnregisterAll(this);
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
