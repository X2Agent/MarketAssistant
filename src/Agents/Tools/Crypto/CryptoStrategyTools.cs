using System.ComponentModel;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.AI;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 策略管理工具实现，供 TradingAgent 查询和更新策略
/// </summary>
public class CryptoStrategyTools : IStrategyTools
{
    private readonly TradingDataService _dataService;

    public CryptoStrategyTools(TradingDataService dataService)
    {
        _dataService = dataService;
    }

    [Description("获取所有活跃状态的交易策略列表")]
    public async Task<List<TradingStrategy>> GetActiveStrategiesAsync()
    {
        return await _dataService.GetStrategiesByStatusAsync(StrategyStatus.Active);
    }

    [Description("根据策略ID获取策略详情")]
    public async Task<TradingStrategy?> GetStrategyAsync(
        [Description("策略ID")] string strategyId)
    {
        return await _dataService.GetStrategyAsync(strategyId);
    }

    [Description("更新策略状态（如暂停、完成、失败）")]
    public async Task UpdateStrategyStatusAsync(
        [Description("策略ID")] string strategyId,
        [Description("新状态")] StrategyStatus status)
    {
        await _dataService.UpdateStrategyStatusAsync(strategyId, status);
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetActiveStrategiesAsync);
        yield return AIFunctionFactory.Create(GetStrategyAsync);
        yield return AIFunctionFactory.Create(UpdateStrategyStatusAsync);
    }
}
