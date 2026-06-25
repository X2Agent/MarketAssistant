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
}
