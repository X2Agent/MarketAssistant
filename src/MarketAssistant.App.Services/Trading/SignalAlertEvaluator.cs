using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 信号告警评估器：把策略生命周期与 AI 信号产出收敛为统一告警（AlertSource.Signal）。
/// 仅上报用户需要知晓的状态跃迁（自动暂停 / 完结 / 信号成交），tick 级过程不上报。
/// 风控拒绝类告警由 <see cref="RiskAlertEvaluator"/> 负责，二者按拒绝原因互斥分流，不重复告警。
/// </summary>
public sealed class SignalAlertEvaluator
{
    private readonly IAlertCenterService _alertCenterService;
    private readonly ILogger<SignalAlertEvaluator> _logger;

    public SignalAlertEvaluator(
        IAlertCenterService alertCenterService,
        ILogger<SignalAlertEvaluator> logger)
    {
        _alertCenterService = alertCenterService;
        _logger = logger;
    }

    /// <summary>
    /// 策略因触发被拒而自动暂停。风控拒绝已由 RiskAlertEvaluator 上报（其内容含暂停说明），
    /// 此处仅补报用户主动拒绝 / 确认超时的情形，保证"策略静默停摆"始终有告警留痕。
    /// </summary>
    public Task NotifyStrategyPausedSafeAsync(TradingStrategy strategy, TradeResult? tradeResult)
    {
        if (!TradeRejectionReason.IsUserRejection(tradeResult?.ErrorMessage))
            return Task.CompletedTask;

        var reason = tradeResult?.ErrorMessage ?? "交易被拒绝";
        return RaiseSafeAsync(
            strategy,
            AlertLevel.Warning,
            "策略已自动暂停",
            $"{strategy.Type} 策略（{strategy.Symbol}）因交易被拒绝已暂停：{reason}。请检查风控配置或在策略页重新启用");
    }

    /// <summary>策略完成全部计划执行次数并退出监控：Info 留痕。</summary>
    public Task NotifyStrategyCompletedSafeAsync(TradingStrategy strategy) =>
        RaiseSafeAsync(
            strategy,
            AlertLevel.Info,
            "策略已完结",
            $"{strategy.Type} 策略（{strategy.Symbol}）已执行完毕并退出监控");

    /// <summary>AI 信号产出并成交：Info 留痕，使告警历史成为自动化决策的完整审计线索。</summary>
    public Task NotifySignalExecutedSafeAsync(TradingStrategy strategy, decimal price) =>
        RaiseSafeAsync(
            strategy,
            AlertLevel.Info,
            "AI 信号已执行",
            $"{strategy.Symbol} 按 AI 信号完成交易，成交价 {price:N6}");

    private async Task RaiseSafeAsync(
        TradingStrategy strategy, AlertLevel level, string title, string content)
    {
        try
        {
            await _alertCenterService.RaiseAlertAsync(new AlertEvent
            {
                MarketType = MarketType.Crypto,
                Symbol = strategy.Symbol,
                Level = level,
                Source = AlertSource.Signal,
                Title = title,
                Content = content
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "上报策略信号告警失败: {StrategyId} {Title}", strategy.Id, title);
        }
    }
}
