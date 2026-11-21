using System.ComponentModel;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 策略只读查询工具实现，供 TradingAgent 参考策略规则做决策。
/// 策略状态与护栏的写入只由系统（执行器、策略引擎、界面）完成，不暴露给模型。
/// </summary>
public class CryptoStrategyTools(
    TradingStrategyService strategyService,
    TradingDataService dataService,
    ILogger<CryptoStrategyTools> logger) : IStrategyTools
{
    private readonly TradingStrategyService _strategyService = strategyService;
    private readonly TradingDataService _dataService = dataService;
    private readonly ILogger<CryptoStrategyTools> _logger = logger;

    [Description("获取所有活跃状态的交易策略列表")]
    public Task<List<TradingStrategy>> GetActiveStrategiesAsync(CancellationToken cancellationToken = default)
    {
        return ToolExecutor.ExecuteAsync("获取活跃策略列表", "-", _logger,
            ct => _strategyService.GetStrategiesByStatusAsync(StrategyStatus.Active, ct), cancellationToken);
    }

    [Description("根据策略ID获取策略详情")]
    public Task<TradingStrategy?> GetStrategyAsync(
        [Description("策略ID")] string strategyId,
        CancellationToken cancellationToken = default)
    {
        return ToolExecutor.ExecuteAsync("获取策略详情", strategyId, _logger,
            ct => _dataService.GetStrategyAsync(strategyId, ct), cancellationToken);
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetActiveStrategiesAsync);
        yield return AIFunctionFactory.Create(GetStrategyAsync);
    }
}
