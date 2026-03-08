using Microsoft.Extensions.AI;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 策略管理工具 —— TradingAgent 可查询和更新策略状态
/// </summary>
public interface IStrategyTools
{
    Task<List<TradingStrategy>> GetActiveStrategiesAsync();
    Task<TradingStrategy?> GetStrategyAsync(string strategyId);
    Task UpdateStrategyStatusAsync(string strategyId, StrategyStatus status);
    IEnumerable<AIFunction> GetFunctions();
}
