using System.Globalization;
using System.Text.Json;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Notification;
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
public sealed class PriceAlertService : SqliteServiceBase, IDisposable
{
    private static readonly TimeSpan ASharePollingInterval = TimeSpan.FromSeconds(20);

    private readonly BinanceWebSocketService _wsService;
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private readonly IHttpClientFactory _httpClientFactory;

    private readonly object _syncRoot = new();
    private List<PriceAlertRule> _rules = [];
    private readonly CancellationTokenSource _pollingCts = new();
    private Task? _pollingTask;

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

    public PriceAlertService(
        BinanceWebSocketService wsService,
        INotificationService notificationService,
        IUserSettingService userSettingService,
        IHttpClientFactory httpClientFactory,
        ILogger<PriceAlertService> logger)
        : base(logger)
    {
        _wsService = wsService;
        _notificationService = notificationService;
        _userSettingService = userSettingService;
        _httpClientFactory = httpClientFactory;

        _wsService.PriceUpdated += OnCryptoPriceUpdated;
        _pollingTask = Task.Run(() => PollASharePricesAsync(_pollingCts.Token));
    }

    /// <summary>
    /// 从数据库异步加载规则到内存，并订阅活跃的虚拟币规则。
    /// 应在应用启动时调用。
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await LoadRulesAsync(cancellationToken);
        SubscribeActiveCryptoRules();
    }

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
                triggered INTEGER NOT NULL DEFAULT 0,
                enabled INTEGER NOT NULL DEFAULT 1,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_alert_mt ON price_alert_rules(market_type);
            CREATE INDEX IF NOT EXISTS idx_alert_enabled ON price_alert_rules(enabled, market_type);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task AddRuleAsync(PriceAlertRule rule, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _rules.Add(rule);
        }

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO price_alert_rules (id, asset_code, asset_name, market_type, condition, target_price, triggered, enabled, created_at)
                VALUES (@id, @assetCode, @assetName, @marketType, @condition, @targetPrice, @triggered, @enabled, @createdAt)
                """;
            cmd.Parameters.AddWithValue("@id", rule.Id);
            cmd.Parameters.AddWithValue("@assetCode", rule.AssetCode);
            cmd.Parameters.AddWithValue("@assetName", rule.AssetName);
            cmd.Parameters.AddWithValue("@marketType", (int)rule.MarketType);
            cmd.Parameters.AddWithValue("@condition", (int)rule.Condition);
            cmd.Parameters.AddWithValue("@targetPrice", (double)rule.TargetPrice);
            cmd.Parameters.AddWithValue("@triggered", rule.Triggered ? 1 : 0);
            cmd.Parameters.AddWithValue("@enabled", rule.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@createdAt", rule.CreatedAt.ToString("O"));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "保存价格预警规则失败");
        }

        RulesChanged?.Invoke();

        if (rule.Enabled && rule.MarketType == MarketType.Crypto)
            _ = SubscribeSafeAsync([ToBinanceFormat(rule.AssetCode)]);
    }

    public async Task RemoveRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _rules.RemoveAll(r => r.Id == ruleId);
        }

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM price_alert_rules WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", ruleId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "删除价格预警规则失败");
        }

        RulesChanged?.Invoke();
    }

    public async Task ToggleRuleAsync(string ruleId, CancellationToken cancellationToken = default)
    {
        PriceAlertRule? rule;
        lock (_syncRoot)
        {
            rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;

            rule.Enabled = !rule.Enabled;
            rule.Triggered = false;
        }

        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE price_alert_rules SET enabled = @enabled, triggered = 0 WHERE id = @id";
            cmd.Parameters.AddWithValue("@enabled", rule.Enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", ruleId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "更新价格预警规则失败");
        }

        RulesChanged?.Invoke();

        if (rule.Enabled && rule.MarketType == MarketType.Crypto)
            _ = SubscribeSafeAsync([ToBinanceFormat(rule.AssetCode)]);
    }

    private void OnCryptoPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        if (!IsNotificationEnabled()) return;

        List<PriceAlertRule> rules;
        lock (_syncRoot)
        {
            rules = _rules
                .Where(r => r.MarketType == MarketType.Crypto && r.Enabled && !r.Triggered)
                .ToList();
        }

        foreach (var rule in rules)
        {
            var ruleSymbol = ToBinanceFormat(rule.AssetCode);
            if (!ruleSymbol.Equals(symbol, StringComparison.OrdinalIgnoreCase))
                continue;

            TryTriggerRule(rule.Id, lastPrice);
        }
    }

    private async Task PollASharePricesAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(ASharePollingInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                if (!IsNotificationEnabled()) continue;

                List<PriceAlertRule> rules;
                lock (_syncRoot)
                {
                    rules = _rules
                        .Where(r => r.MarketType == MarketType.AShare && r.Enabled && !r.Triggered)
                        .ToList();
                }

                foreach (var rule in rules)
                {
                    var latestPrice = await GetAShareLatestPriceAsync(rule.AssetCode, cancellationToken);
                    if (latestPrice.HasValue)
                        TryTriggerRule(rule.Id, latestPrice.Value);
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

    private async Task<decimal?> GetAShareLatestPriceAsync(string assetCode, CancellationToken cancellationToken)
    {
        try
        {
            var clsCode = ToClsFormat(assetCode);
            if (string.IsNullOrWhiteSpace(clsCode))
                return null;

            var url =
                $"https://x-quote.cls.cn/quote/stock/basic?secu_code={clsCode}&fields=last_px&app=CailianpressWeb&os=web&sv=8.4.6";

            using var httpClient = _httpClientFactory.CreateClient("Cls");
            using var response = await httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDocument = JsonDocument.Parse(json);
            if (!jsonDocument.RootElement.TryGetProperty("data", out var data) || data.ValueKind == JsonValueKind.Null)
                return null;

            if (!data.TryGetProperty("last_px", out var priceElement))
                return null;

            if (priceElement.ValueKind == JsonValueKind.Number && priceElement.TryGetDecimal(out var price))
                return price;

            if (priceElement.ValueKind == JsonValueKind.String &&
                decimal.TryParse(priceElement.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var stringPrice))
                return stringPrice;

            return null;
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

    private void TryTriggerRule(string ruleId, decimal lastPrice)
    {
        PriceAlertRule? rule;
        lock (_syncRoot)
        {
            rule = _rules.FirstOrDefault(r => r.Id == ruleId && r.Enabled && !r.Triggered);
            if (rule == null) return;

            var triggered = rule.Condition switch
            {
                AlertCondition.PriceAbove => lastPrice >= rule.TargetPrice,
                AlertCondition.PriceBelow => lastPrice <= rule.TargetPrice,
                _ => false
            };

            if (!triggered) return;

            rule.Triggered = true;
            rule.Enabled = false;
        }

        // 异步持久化触发状态，不阻塞通知流程
        _ = PersistRuleTriggeredAsync(ruleId);

        if (IsNotificationEnabled())
        {
            var direction = rule.Condition == AlertCondition.PriceAbove ? "突破上方" : "跌破下方";
            _notificationService.ShowWarning(
                $"🔔 {rule.AssetName}({rule.AssetCode}) 当前价 {lastPrice}，已{direction}预警价 {rule.TargetPrice}",
                durationMs: 10000);
        }

        RulesChanged?.Invoke();
    }

    private async Task PersistRuleTriggeredAsync(string ruleId)
    {
        try
        {
            await EnsureInitializedAsync(InitializeDatabaseAsync);
            await using var conn = await OpenConnectionAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE price_alert_rules SET triggered = 1, enabled = 0 WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", ruleId);
            await cmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "持久化预警触发状态失败: {RuleId}", ruleId);
        }
    }

    private void SubscribeActiveCryptoRules()
    {
        List<string> symbols;
        lock (_syncRoot)
        {
            symbols = _rules
                .Where(r => r.MarketType == MarketType.Crypto && r.Enabled && !r.Triggered)
                .Select(r => ToBinanceFormat(r.AssetCode))
                .Distinct()
                .ToList();
        }

        if (symbols.Count > 0)
            _ = SubscribeSafeAsync(symbols);
    }

    /// <summary>
    /// 安全的 WebSocket 订阅封装：捕获并记录异常，避免 fire-and-forget 调用吞掉错误。
    /// </summary>
    private async Task SubscribeSafeAsync(IReadOnlyCollection<string> symbols)
    {
        try
        {
            await _wsService.SubscribeAsync(symbols);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "WebSocket 订阅失败，价格预警可能无法实时推送: {Symbols}",
                string.Join(", ", symbols));
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
                SELECT id, asset_code, asset_name, market_type, condition, target_price, triggered, enabled, created_at
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
                    Triggered = reader.GetInt32(6) != 0,
                    Enabled = reader.GetInt32(7) != 0,
                    CreatedAt = DateTime.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
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

    public void Dispose()
    {
        _wsService.PriceUpdated -= OnCryptoPriceUpdated;
        _pollingCts.Cancel();
        try
        {
            _pollingTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex)
        {
            ex.Handle(e => e is OperationCanceledException);
        }
        _pollingCts.Dispose();
    }

    private bool IsNotificationEnabled()
    {
        return _userSettingService.CurrentSetting.Notification;
    }
}
