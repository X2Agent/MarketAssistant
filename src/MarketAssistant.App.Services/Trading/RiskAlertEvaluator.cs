using System.Collections.Concurrent;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 风险告警评估器：把风险状态收敛为统一告警（AlertSource.Risk）。
/// - 账户回撤达熔断阈值的 80% → Warning（60 秒节流，避免每 tick 重复）；
/// - 策略信号被风控拒绝 → Warning；
/// - 行情连接中断 → Critical / 恢复 → Info。
/// </summary>
public sealed class RiskAlertEvaluator(
    IAlertCenterService alertCenterService,
    TradingDataService dataService,
    CryptoPortfolioService portfolioService,
    LiquidationRiskEvaluator liquidationRiskEvaluator,
    ILogger<RiskAlertEvaluator> logger)
{
    private static readonly TimeSpan RiskCheckThrottle = TimeSpan.FromSeconds(60);

    private readonly IAlertCenterService _alertCenterService = alertCenterService;
    private readonly TradingDataService _dataService = dataService;
    private readonly CryptoPortfolioService _portfolioService = portfolioService;
    private readonly LiquidationRiskEvaluator _liquidationRiskEvaluator = liquidationRiskEvaluator;
    private readonly ILogger<RiskAlertEvaluator> _logger = logger;

    private DateTime _lastAccountRiskCheckUtc = DateTime.MinValue;
    private volatile bool _connectionAlertActive;

    /// <summary>
    /// 评估账户回撤风险：回撤达熔断阈值 80% 时发 Warning。
    /// 自带 60 秒节流，可安全地在每个价格 tick 调用。
    /// </summary>
    public async Task EvaluateAccountRiskSafeAsync(CancellationToken ct)
    {
        // 节流：价格 tick 高频，账户快照/峰值查询成本较高
        var now = DateTime.UtcNow;
        if (now - _lastAccountRiskCheckUtc < RiskCheckThrottle)
            return;
        _lastAccountRiskCheckUtc = now;

        // 合约强平距离监控与账户回撤共用同一节流窗口（同为账户级风险巡检）
        await _liquidationRiskEvaluator.EvaluateSafeAsync(ct).ConfigureAwait(false);

        try
        {
            var config = await _dataService.LoadRiskConfigAsync(ct).ConfigureAwait(false);
            if (config.MaxDrawdownPercent <= 0)
                return;

            var summary = await _portfolioService.GetAccountBalanceSummaryAsync(ct).ConfigureAwait(false);
            var totalValue = summary.TotalValueUSDT;
            if (totalValue <= 0)
                return;

            var peakValue = await _dataService
                .GetPeakAccountValueAsync(now.AddDays(-30), ct)
                .ConfigureAwait(false);
            if (peakValue <= 0)
                return;

            var drawdownPercent = (peakValue - totalValue) / peakValue * 100;
            var warningThreshold = config.MaxDrawdownPercent * 0.8m;
            if (drawdownPercent < warningThreshold)
                return;

            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = string.Empty,
                Level = AlertLevel.Warning,
                Source = AlertSource.Risk,
                Title = "账户回撤接近熔断阈值",
                Content = $"当前回撤 {drawdownPercent:F1}%，熔断阈值 {config.MaxDrawdownPercent:F0}%（预警线 80%），请关注仓位风险",
                TradingImpact = AlertTradingImpact.None
            }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "账户回撤风险评估失败");
        }
    }

    /// <summary>策略信号被风控拒绝时发 Warning（用户主动拒绝的交易不上报）。</summary>
    public async Task EvaluateRejectionSafeAsync(TradingStrategy strategy, TradeResult? tradeResult)
    {
        var reason = tradeResult?.ErrorMessage ?? "交易被拒绝";
        // 用户主动拒绝 / 确认超时类失败由策略状态信号告警（AlertSource.Signal）覆盖，避免同一事件双告警
        if (TradeRejectionReason.IsUserRejection(reason))
            return;

        try
        {
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = strategy.Symbol,
                Level = AlertLevel.Warning,
                Source = AlertSource.Risk,
                Title = $"策略信号被风控拒绝（{strategy.Type}）",
                Content = $"{reason}。策略已自动暂停，请检查风控配置或持仓状态",
                TradingImpact = AlertTradingImpact.None
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "上报风控拒绝告警失败: {StrategyId}", strategy.Id);
        }
    }

    /// <summary>行情连接中断：Critical 告警（重复中断由告警中心 5 分钟合并窗口去重）。</summary>
    public async Task NotifyConnectionInterruptedSafeAsync()
    {
        _connectionAlertActive = true;
        try
        {
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = string.Empty,
                Level = AlertLevel.Critical,
                Source = AlertSource.System,
                Title = "行情连接中断",
                Content = "Binance WebSocket 连接中断，正在自动重连。中断期间止损/止盈等策略可能无法及时执行",
                TradingImpact = AlertTradingImpact.None
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "上报行情连接中断告警失败");
        }
    }

    /// <summary>行情连接恢复：Info 告警。</summary>
    public async Task NotifyConnectionRestoredSafeAsync()
    {
        if (!_connectionAlertActive)
            return;
        _connectionAlertActive = false;

        try
        {
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = string.Empty,
                Level = AlertLevel.Info,
                Source = AlertSource.System,
                Title = "行情连接恢复",
                Content = "Binance WebSocket 连接已恢复，策略监控继续运行"
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "上报行情连接恢复告警失败");
        }
    }

    /// <summary>
    /// 交易处于紧急停止状态：Critical 告警（登记停机或监控启动被拒时上报），
    /// 确保用户明确知道"自动交易已停止"及其原因，而非面对静默无响应的策略列表。
    /// </summary>
    public async Task NotifyTradingHaltedSafeAsync(string reason)
    {
        try
        {
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = string.Empty,
                Level = AlertLevel.Critical,
                Source = AlertSource.System,
                Title = "交易已紧急停止",
                Content = $"全部策略已停止，新订单会被拒绝：{reason}。请在交易监控页解除紧急停止后逐个重新启用策略"
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "上报交易紧急停止告警失败");
        }
    }
}