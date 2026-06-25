using System.ComponentModel;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 策略管理工具实现，供 TradingAgent 查询和更新策略
/// </summary>
public class CryptoStrategyTools : IStrategyTools
{
    private readonly TradingDataService _dataService;
    private readonly ILogger<CryptoStrategyTools> _logger;

    public CryptoStrategyTools(TradingDataService dataService, ILogger<CryptoStrategyTools> logger)
    {
        _dataService = dataService;
        _logger = logger;
    }

    [Description("获取所有活跃状态的交易策略列表")]
    public async Task<List<TradingStrategy>> GetActiveStrategiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dataService.GetStrategiesByStatusAsync(StrategyStatus.Active);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取活跃策略列表失败");
            throw new FriendlyException($"获取策略列表失败: {ex.Message}", ex);
        }
    }

    [Description("根据策略ID获取策略详情")]
    public async Task<TradingStrategy?> GetStrategyAsync(
        [Description("策略ID")] string strategyId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _dataService.GetStrategyAsync(strategyId);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "获取策略详情失败: {StrategyId}", strategyId);
            throw new FriendlyException($"获取策略详情失败: {ex.Message}", ex);
        }
    }

    [Description("更新策略状态（如暂停、完成、失败）")]
    public async Task UpdateStrategyStatusAsync(
        [Description("策略ID")] string strategyId,
        [Description("新状态")] StrategyStatus status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _dataService.UpdateStrategyStatusAsync(strategyId, status);
        }
        catch (Exception ex) when (ex is not FriendlyException)
        {
            _logger.LogError(ex, "更新策略状态失败: {StrategyId} -> {Status}", strategyId, status);
            throw new FriendlyException($"更新策略状态失败: {ex.Message}", ex);
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetActiveStrategiesAsync);
        yield return AIFunctionFactory.Create(GetStrategyAsync);
        yield return AIFunctionFactory.Create(UpdateStrategyStatusAsync);
    }
}
