using System.Globalization;
using System.Text.Json;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Infrastructure.Configuration;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;
using static MarketAssistant.Infrastructure.Core.StockSymbolConverter;

namespace MarketAssistant.Applications.PriceAlert;

/// <summary>
/// 价格预警服务，监听 WebSocket 价格并触发通知
/// </summary>
public sealed class PriceAlertService : IDisposable
{
    /// <summary>
    /// 历史遗留的单一存储键，仅用于一次性迁移到按市场分键存储。
    /// 迁移完成后此键会被清除，新规则通过 <see cref="PreferenceKeys.GetPriceAlertRulesKey"/> 存储。
    /// </summary>
    private static readonly string LegacyStorageKey = PreferenceKeys.PriceAlertRules;

    private static readonly TimeSpan ASharePollingInterval = TimeSpan.FromSeconds(20);

    private readonly BinanceWebSocketService _wsService;
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PriceAlertService> _logger;

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
                // 返回快照，避免外部枚举时内部修改引发竞态
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
    {
        _wsService = wsService;
        _notificationService = notificationService;
        _userSettingService = userSettingService;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        LoadRules();
        _wsService.PriceUpdated += OnCryptoPriceUpdated;
        SubscribeActiveCryptoRules();
        _pollingTask = Task.Run(() => PollASharePricesAsync(_pollingCts.Token));
    }

    public void AddRule(PriceAlertRule rule)
    {
        lock (_syncRoot)
        {
            _rules.Add(rule);
        }

        SaveRules();
        RulesChanged?.Invoke();

        if (rule.Enabled && rule.MarketType == MarketType.Crypto)
            _ = SubscribeSafeAsync([ToBinanceFormat(rule.AssetCode)]);
    }

    public void RemoveRule(string ruleId)
    {
        lock (_syncRoot)
        {
            _rules.RemoveAll(r => r.Id == ruleId);
        }

        SaveRules();
        RulesChanged?.Invoke();
    }

    public void ToggleRule(string ruleId)
    {
        PriceAlertRule? rule;
        lock (_syncRoot)
        {
            rule = _rules.FirstOrDefault(r => r.Id == ruleId);
            if (rule == null) return;

            rule.Enabled = !rule.Enabled;
            rule.Triggered = false;
        }
        SaveRules();
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
            _logger.LogWarning(ex, "A股预警轮询异常");
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
            _logger.LogWarning(ex, "获取A股价格失败: {AssetCode}", assetCode);
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

        SaveRules();

        if (IsNotificationEnabled())
        {
            var direction = rule.Condition == AlertCondition.PriceAbove ? "突破上方" : "跌破下方";
            _notificationService.ShowWarning(
                $"🔔 {rule.AssetName}({rule.AssetCode}) 当前价 {lastPrice}，已{direction}预警价 {rule.TargetPrice}",
                durationMs: 10000);
        }

        RulesChanged?.Invoke();
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
            _logger.LogWarning(ex, "WebSocket 订阅失败，价格预警可能无法实时推送: {Symbols}",
                string.Join(", ", symbols));
        }
    }

    private void LoadRules()
    {
        try
        {
            // 一次性迁移历史单一存储键到按市场分键存储
            MigrateLegacyStorageIfNeeded();

            // 从各市场独立键加载并合并
            var allRules = new List<PriceAlertRule>();
            foreach (MarketType market in Enum.GetValues<MarketType>())
            {
                var key = PreferenceKeys.GetPriceAlertRulesKey(market);
                var json = Preferences.Default.Get(key, string.Empty);
                if (!string.IsNullOrEmpty(json))
                {
                    var rules = JsonSerializer.Deserialize<List<PriceAlertRule>>(json) ?? [];
                    allRules.AddRange(rules);
                }
            }

            lock (_syncRoot)
            {
                _rules = allRules;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "加载价格预警规则失败");
            lock (_syncRoot)
            {
                _rules = [];
            }
        }
    }

    /// <summary>
    /// 将历史单一存储键 <see cref="LegacyStorageKey"/> 的规则按市场拆分迁移到新键，迁移后清除旧键。
    /// </summary>
    private void MigrateLegacyStorageIfNeeded()
    {
        var legacyJson = Preferences.Default.Get(LegacyStorageKey, string.Empty);
        if (string.IsNullOrEmpty(legacyJson))
            return;

        try
        {
            var rules = JsonSerializer.Deserialize<List<PriceAlertRule>>(legacyJson) ?? [];

            // 修复历史数据：A股规则不应以 USDT 结尾，归入虚拟币
            foreach (var rule in rules.Where(r =>
                r.MarketType == MarketType.AShare &&
                r.AssetCode.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)))
            {
                rule.MarketType = MarketType.Crypto;
            }

            // 按市场分组保存到新键
            foreach (var group in rules.GroupBy(r => r.MarketType))
            {
                var key = PreferenceKeys.GetPriceAlertRulesKey(group.Key);
                Preferences.Default.Set(key, JsonSerializer.Serialize(group.ToList()));
            }

            Preferences.Default.Remove(LegacyStorageKey);
            _logger.LogInformation("已将价格提醒规则从历史单一键迁移到按市场分键存储");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "迁移历史价格提醒规则失败");
        }
    }

    private void SaveRules()
    {
        try
        {
            List<PriceAlertRule> rules;
            lock (_syncRoot)
            {
                rules = [.. _rules];
            }

            // 按市场分组分别存储，实现多市场数据隔离
            var marketsWithRules = new HashSet<MarketType>();
            foreach (var group in rules.GroupBy(r => r.MarketType))
            {
                var key = PreferenceKeys.GetPriceAlertRulesKey(group.Key);
                Preferences.Default.Set(key, JsonSerializer.Serialize(group.ToList()));
                marketsWithRules.Add(group.Key);
            }

            // 清除没有规则的市场键，避免残留空数据
            foreach (MarketType market in Enum.GetValues<MarketType>())
            {
                if (!marketsWithRules.Contains(market))
                {
                    Preferences.Default.Remove(PreferenceKeys.GetPriceAlertRulesKey(market));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "保存价格预警规则失败");
        }
    }

    public void Dispose()
    {
        _wsService.PriceUpdated -= OnCryptoPriceUpdated;
        _pollingCts.Cancel();
        // 等待后台轮询任务退出，避免在 SaveRules 写入文件过程中被中断导致持久化数据损坏
        try
        {
            _pollingTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException ex)
        {
            // OperationCanceledException 是预期内的，仅记录其他异常
            ex.Handle(e => e is OperationCanceledException);
        }
        _pollingCts.Dispose();
    }

    private bool IsNotificationEnabled()
    {
        return _userSettingService.CurrentSetting.Notification;
    }
}
