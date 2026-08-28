using MarketAssistant.Trading.Models;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 策略管理工具 —— TradingAgent 可查询和更新策略状态
/// </summary>
public interface IStrategyTools : IToolsProvider
{
    Task<List<TradingStrategy>> GetActiveStrategiesAsync(CancellationToken cancellationToken = default);
    Task<TradingStrategy?> GetStrategyAsync(string strategyId, CancellationToken cancellationToken = default);
    Task UpdateStrategyStatusAsync(string strategyId, StrategyStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// 为已有持仓创建护栏策略（止损/止盈/追踪止损），保护当前仓位。
    /// </summary>
    Task<string> CreateGuardrailAsync(
        string symbol,
        decimal? stopLossPrice,
        decimal? takeProfitPrice,
        decimal? trailingPercent,
        CancellationToken cancellationToken = default);
}
