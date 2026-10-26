using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 合约持仓强平距离监控：杠杆交易的真正风险不是浮亏，而是距强平价还有多远。
/// 10 倍杠杆下价格反向 10% 即触发强平，该指标必须在告警中心可见。
/// </summary>
/// <remarks>
/// <para>告警标题使用固定常量：联动门（AlertGate）按 (来源, 市场, 标的, 标题) 登记与解除，
/// 标题必须是稳定字符串，否则条件恢复后无法精确释放门控。</para>
/// <para>Critical 级登记 <see cref="AlertTradingImpact.RequireConfirmation"/>——
/// 持仓已接近强平时再向该标的加仓是高风险动作，必须人工确认；距离恢复后解除。</para>
/// </remarks>
public sealed class LiquidationRiskEvaluator(
    [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
    TradingDataService dataService,
    IAlertCenterService alertCenterService,
    ILogger<LiquidationRiskEvaluator> logger)
{
    /// <summary>紧急级告警标题（同时作为联动门登记键）。</summary>
    public const string CriticalTitle = "合约持仓即将强平";

    /// <summary>警告级告警标题（同时作为联动门登记键）。</summary>
    public const string WarningTitle = "合约持仓强平距离偏近";

    private readonly object _stateSync = new();
    private readonly Dictionary<string, LiquidationRiskLevel> _activeLevels = new(StringComparer.OrdinalIgnoreCase);
    private List<LiquidationRiskInfo> _latestRisks = [];

    /// <summary>最近一次评估的强平风险快照（UI 展示用）。</summary>
    public IReadOnlyList<LiquidationRiskInfo> LatestRisks
    {
        get
        {
            lock (_stateSync)
            {
                return [.. _latestRisks];
            }
        }
    }

    /// <summary>
    /// 评估全部合约持仓的强平距离。异常仅记录（行情/账户查询失败不得影响交易主链）。
    /// </summary>
    public async Task EvaluateSafeAsync(CancellationToken ct = default)
    {
        try
        {
            // 现货无强平概念：直接清空状态并解除可能残留的门控
            if (!exchangeClient.IsFutures)
            {
                await ClearAllAsync().ConfigureAwait(false);
                return;
            }

            var positions = await exchangeClient.GetPositionsAsync(null, ct).ConfigureAwait(false);
            var risks = new List<LiquidationRiskInfo>();

            foreach (var position in positions)
            {
                var risk = EvaluatePosition(position);
                if (risk != null)
                    risks.Add(risk);
            }

            var config = await LoadConfigAsync(ct).ConfigureAwait(false);
            var graded = risks
                .Select(risk => risk with
                {
                    Level = ResolveLevel(
                        risk.DistancePercent, config.LiquidationWarnPercent, config.LiquidationCriticalPercent)
                })
                .ToList();

            lock (_stateSync)
            {
                _latestRisks = graded;
            }

            await ApplyAlertsAsync(graded, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "合约强平距离评估失败");
        }
    }

    /// <summary>计算单个持仓的强平距离；无强平价（交易所未返回）时返回 null。</summary>
    internal static LiquidationRiskInfo? EvaluatePosition(ExchangePosition position)
    {
        var isShort = position.PositionAmt < 0;
        var markPrice = position.MarkPrice > 0 ? position.MarkPrice : position.EntryPrice;
        if (position.LiquidationPrice <= 0 || markPrice <= 0)
            return null;

        var distance = Math.Abs(markPrice - position.LiquidationPrice) / markPrice * 100m;

        return new LiquidationRiskInfo(
            position.Symbol,
            isShort,
            markPrice,
            position.LiquidationPrice,
            distance,
            LiquidationRiskLevel.None);
    }

    /// <summary>按阈值判定级别（供 EvaluatePosition 之外的单测与 UI 复用）。</summary>
    internal static LiquidationRiskLevel ResolveLevel(
        decimal distancePercent, decimal warnPercent, decimal criticalPercent)
    {
        if (criticalPercent > 0 && distancePercent <= criticalPercent)
            return LiquidationRiskLevel.Critical;

        if (warnPercent > 0 && distancePercent <= warnPercent)
            return LiquidationRiskLevel.Warning;

        return LiquidationRiskLevel.None;
    }

    private async Task ApplyAlertsAsync(List<LiquidationRiskInfo> risks, CancellationToken ct)
    {
        foreach (var risk in risks)
        {
            var level = risk.Level;

            var previous = GetPreviousLevel(risk.Symbol);
            if (level == previous)
                continue;

            // 级别变化：先解除旧级别登记的门控，再按新级别登记
            if (previous == LiquidationRiskLevel.Critical)
                await ClearAsync(risk.Symbol, CriticalTitle, ct).ConfigureAwait(false);
            else if (previous == LiquidationRiskLevel.Warning)
                await ClearAsync(risk.Symbol, WarningTitle, ct).ConfigureAwait(false);

            if (level == LiquidationRiskLevel.Critical)
            {
                await alertCenterService.RaiseAlertAsync(new AlertEvent
                {
                    MarketType = MarketType.Crypto,
                    Symbol = risk.Symbol,
                    Level = AlertLevel.Critical,
                    Source = AlertSource.Risk,
                    Title = CriticalTitle,
                    Content = $"{risk.Symbol} {(risk.IsShort ? "空头" : "多头")}持仓距强平价仅 {risk.DistancePercent:F1}%"
                              + $"（强平价 {risk.LiquidationPrice:N4}，标记价 {risk.MarkPrice:N4}），请立即补仓或减仓",
                    TradingImpact = AlertTradingImpact.RequireConfirmation
                }, ct).ConfigureAwait(false);
            }
            else if (level == LiquidationRiskLevel.Warning)
            {
                await alertCenterService.RaiseAlertAsync(new AlertEvent
                {
                    MarketType = MarketType.Crypto,
                    Symbol = risk.Symbol,
                    Level = AlertLevel.Warning,
                    Source = AlertSource.Risk,
                    Title = WarningTitle,
                    Content = $"{risk.Symbol} {(risk.IsShort ? "空头" : "多头")}持仓距强平价 {risk.DistancePercent:F1}%"
                              + $"（强平价 {risk.LiquidationPrice:N4}），接近预警阈值"
                }, ct).ConfigureAwait(false);
            }

            SetLevel(risk.Symbol, level);
        }

        // 持仓已平（不在本次快照中）的标的解除门控
        foreach (var symbol in GetTrackedSymbols())
        {
            if (risks.Any(risk => risk.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)))
                continue;

            await ClearAsync(symbol, CriticalTitle, ct).ConfigureAwait(false);
            await ClearAsync(symbol, WarningTitle, ct).ConfigureAwait(false);
            RemoveLevel(symbol);
        }
    }

    private async Task ClearAllAsync()
    {
        foreach (var symbol in GetTrackedSymbols())
        {
            await ClearAsync(symbol, CriticalTitle).ConfigureAwait(false);
            await ClearAsync(symbol, WarningTitle).ConfigureAwait(false);
        }

        lock (_stateSync)
        {
            _activeLevels.Clear();
            _latestRisks = [];
        }
    }

    private async Task<RiskConfig> LoadConfigAsync(CancellationToken ct)
    {
        try
        {
            return await dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "加载风控配置失败，强平距离按默认阈值评估");
            return new RiskConfig();
        }
    }

    private async Task ClearAsync(string symbol, string title, CancellationToken ct = default)
    {
        try
        {
            await alertCenterService.RaiseAlertClearedAsync(
                AlertSource.Risk, MarketType.Crypto, symbol, title, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "解除强平风险联动门失败: {Symbol} {Title}", symbol, title);
        }
    }

    private LiquidationRiskLevel GetPreviousLevel(string symbol)
    {
        lock (_stateSync)
        {
            return _activeLevels.TryGetValue(symbol, out var level) ? level : LiquidationRiskLevel.None;
        }
    }

    private void SetLevel(string symbol, LiquidationRiskLevel level)
    {
        lock (_stateSync)
        {
            _activeLevels[symbol] = level;
        }
    }

    private void RemoveLevel(string symbol)
    {
        lock (_stateSync)
        {
            _activeLevels.Remove(symbol);
        }
    }

    private List<string> GetTrackedSymbols()
    {
        lock (_stateSync)
        {
            return [.. _activeLevels.Keys];
        }
    }
}
