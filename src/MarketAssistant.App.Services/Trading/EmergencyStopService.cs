using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 紧急停止（kill switch）结果：供 UI 展示执行摘要。
/// </summary>
public sealed record EmergencyStopResult(bool Success, string Message, int PausedStrategyCount);

/// <summary>
/// 全部平仓结果：逐标的执行，单个失败不影响其余标的。
/// </summary>
public sealed record CloseAllPositionsResult(
    bool Success, string Message, int ClosedSymbolCount, IReadOnlyList<string> Failures);

/// <summary>
/// 紧急停止服务（kill switch）：成熟产品必须具备的资金安全总闸。
/// </summary>
/// <remarks>
/// <para>两条相互独立的路径，由用户按场景选择：</para>
/// <list type="bullet">
/// <item><see cref="StopStrategiesAndOrdersAsync"/>：停止监控 + 暂停全部策略 + 撤销本应用条件单 +
/// 登记持久化停机标记。持仓保留，由用户自行决定去留。</item>
/// <item><see cref="CloseAllPositionsAsync"/>：在先完成上述停机的前提下，逐标的市价平仓
/// （合约 reduceOnly / 现货卖出非 USDT 资产）。有滑点风险，必须由用户显式触发。</item>
/// </list>
/// <para>停机标记持久化且全局生效：重启后策略不会自动复活，风控拒单，监控拒绝启动。</para>
/// </remarks>
public sealed class EmergencyStopService(
    IMarketMonitorProvider monitorProvider,
    TradingStrategyService strategyService,
    ExchangeConditionOrderService conditionOrderService,
    CryptoPortfolioService portfolioService,
    TradeExecutor tradeExecutor,
    TradingEnvironmentService environmentService,
    TradingHaltState haltState,
    RiskAlertEvaluator riskAlertEvaluator,
    ILogger<EmergencyStopService> logger)
{
    /// <summary>紧急平仓的策略标识（交易记录中可追溯来源）。</summary>
    public const string EmergencyCloseStrategyId = "emergency-close";

    /// <summary>
    /// 停止全部策略并撤销本应用挂单（不平仓）。
    /// 重复调用是幂等的：已停机时直接返回当前状态。
    /// </summary>
    public async Task<EmergencyStopResult> StopStrategiesAndOrdersAsync(
        string reason, CancellationToken ct = default)
    {
        await haltState.EnsureLoadedAsync(ct).ConfigureAwait(false);
        if (haltState.IsHalted && !haltState.HasLoadError)
        {
            return new EmergencyStopResult(
                true, $"交易已处于紧急停止状态：{haltState.HaltReason}", 0);
        }

        // 监控未能完整停止（存在在途策略任务）时中止停机：在途订单可能仍在执行，
        // 此时登记停机会造成"以为停了其实还在跑"的错觉
        var monitor = monitorProvider.GetMonitor();
        if (monitor.IsRunning && !await monitor.TryStopAsync().ConfigureAwait(false))
        {
            logger.LogError("紧急停止中止：市场监控未能在超时内停止，存在在途策略任务");
            return new EmergencyStopResult(
                false, "存在正在执行的策略任务，监控未能及时停止，紧急停止已中止。请稍后重试", 0);
        }

        var activeStrategies = await strategyService
            .GetStrategiesByStatusAsync(StrategyStatus.Active, ct)
            .ConfigureAwait(false);

        var pausedCount = 0;
        foreach (var strategy in activeStrategies)
        {
            try
            {
                await strategyService.UpdateStrategyStatusAsync(strategy.Id, StrategyStatus.Paused, ct)
                    .ConfigureAwait(false);
                pausedCount++;
            }
            catch (Exception ex)
            {
                // 单个策略暂停失败不中止整体停机：停机标记仍会登记，失败策略由风控拒单兜底
                logger.LogError(ex, "紧急停止时暂停策略失败: {StrategyId}", strategy.Id);
            }
        }

        // 全部策略已暂停：对账会把交易所侧条件单识别为孤儿单并撤销
        await conditionOrderService.ReconcileSafeAsync().ConfigureAwait(false);

        await haltState.HaltAsync(reason, ct).ConfigureAwait(false);
        await riskAlertEvaluator.NotifyTradingHaltedSafeAsync(reason).ConfigureAwait(false);

        logger.LogWarning(
            "紧急停止完成：暂停 {Count} 个策略，交易模式 {Mode}", pausedCount, environmentService.CurrentMode);

        return new EmergencyStopResult(
            true, $"已停止 {pausedCount} 个策略并撤销本应用条件单，新订单将被拒绝", pausedCount);
    }

    /// <summary>
    /// 停止策略并全部平仓。先执行 <see cref="StopStrategiesAndOrdersAsync"/>，
    /// 再逐标的市价平仓；单个标的失败不影响其余标的，最终汇总告警。
    /// </summary>
    public async Task<CloseAllPositionsResult> CloseAllPositionsAsync(
        string reason, CancellationToken ct = default)
    {
        var stopResult = await StopStrategiesAndOrdersAsync(reason, ct).ConfigureAwait(false);
        if (!stopResult.Success)
            return new CloseAllPositionsResult(false, stopResult.Message, 0, []);

        // 入口固定交易模式快照：防止多标的循环平仓期间模式被切换导致跨环境平仓
        var modeAtEntry = environmentService.CurrentMode;

        List<PositionInfo> positions;
        try
        {
            positions = await portfolioService.GetCurrentPositionsAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "紧急全部平仓：获取持仓失败");
            return new CloseAllPositionsResult(
                false, "已停止策略，但获取持仓失败，请手动在交易所处理持仓", 0, ["获取持仓失败"]);
        }

        var failures = new List<string>();
        var closedCount = 0;

        foreach (var position in positions)
        {
            if (position.Quantity <= 0)
                continue;

            try
            {
                // 多头卖出、空头买入；requireClose 保证合约下只减仓不反向开仓
                var side = position.IsShort ? OrderSide.Buy : OrderSide.Sell;
                var result = await tradeExecutor.ExecuteEmergencyCloseAsync(
                    position.Symbol, side, position.Quantity, position.CurrentPrice, ct, modeAtEntry).ConfigureAwait(false);

                if (result.Success)
                {
                    closedCount++;
                    logger.LogWarning(
                        "紧急全部平仓成功: {Symbol} {Side} 数量 {Qty}", position.Symbol, side, position.Quantity);
                }
                else
                {
                    failures.Add($"{position.Symbol}: {result.ErrorMessage}");
                    logger.LogError(
                        "紧急全部平仓失败: {Symbol} {Error}", position.Symbol, result.ErrorMessage);
                }
            }
            catch (Exception ex)
            {
                failures.Add($"{position.Symbol}: {ex.Message}");
                logger.LogError(ex, "紧急全部平仓异常: {Symbol}", position.Symbol);
            }
        }

        var message = failures.Count == 0
            ? $"已停止全部策略并平掉 {closedCount} 个标的的持仓"
            : $"已停止全部策略，平仓 {closedCount} 个标的，{failures.Count} 个标的失败（请手动处理）：{string.Join("；", failures)}";

        logger.LogWarning("紧急全部平仓完成: {Message}", message);
        return new CloseAllPositionsResult(failures.Count == 0, message, closedCount, failures);
    }

    /// <summary>
    /// 解除紧急停止。策略保持暂停状态——是否恢复自动交易由用户逐个显式启用，
    /// 不提供"一键全部恢复"，避免解除停止的瞬间所有策略同时复工。
    /// </summary>
    public async Task ResumeAsync(CancellationToken ct = default)
    {
        await haltState.ResumeAsync(ct).ConfigureAwait(false);
        logger.LogInformation("紧急停止已解除，策略保持暂停，等待用户逐个启用");
    }
}
