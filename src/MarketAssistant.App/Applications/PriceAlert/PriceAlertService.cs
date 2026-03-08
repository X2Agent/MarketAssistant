using System.Text.Json;
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
    private const string StorageKey = "PriceAlertRules";
    private static readonly TimeSpan ASharePollingInterval = TimeSpan.FromSeconds(20);

    private readonly BinanceWebSocketService _wsService;
    private readonly INotificationService _notificationService;
    private readonly IUserSettingService _userSettingService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PriceAlertService> _logger;

    private readonly object _syncRoot = new();
    private List<PriceAlertRule> _rules = [];
    private readonly CancellationTokenSource _pollingCts = new();

    public IReadOnlyList<PriceAlertRule> Rules => _rules;
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
        _ = Task.Run(() => PollASharePricesAsync(_pollingCts.Token));
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
            _ = _wsService.SubscribeAsync([ToBinanceFormat(rule.AssetCode)]);
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
            _ = _wsService.SubscribeAsync([ToBinanceFormat(rule.AssetCode)]);
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

            using var httpClient = _httpClientFactory.CreateClient();
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
                decimal.TryParse(priceElement.GetString(), out var stringPrice))
                return stringPrice;

            return null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "获取A股价格失败: {AssetCode}", assetCode);
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
            _ = _wsService.SubscribeAsync(symbols);
    }

    private void LoadRules()
    {
        try
        {
            var json = Preferences.Default.Get(StorageKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
            {
                var rules = JsonSerializer.Deserialize<List<PriceAlertRule>>(json) ?? [];

                foreach (var rule in rules.Where(r =>
                    r.MarketType == MarketType.AShare &&
                    r.AssetCode.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)))
                {
                    rule.MarketType = MarketType.Crypto;
                }

                lock (_syncRoot)
                {
                    _rules = rules;
                }
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

    private void SaveRules()
    {
        try
        {
            List<PriceAlertRule> rules;
            lock (_syncRoot)
            {
                rules = [.. _rules];
            }

            var json = JsonSerializer.Serialize(rules);
            Preferences.Default.Set(StorageKey, json);
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
        _pollingCts.Dispose();
    }

    private bool IsNotificationEnabled()
    {
        return _userSettingService.CurrentSetting.Notification;
    }
}
