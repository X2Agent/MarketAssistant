using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 策略编排服务：统一封装策略增删改，并广播策略集合变化。
/// </summary>
public class TradingStrategyService
{
    private readonly TradingDataService _dataService;

    public TradingStrategyService(TradingDataService dataService)
    {
        _dataService = dataService;
    }

    public event EventHandler? StrategiesChanged;

    /// <summary>
    /// 按状态查询策略。该转发保留：调用方（StrategyEngine/MarketMonitor 等）依赖策略变更广播语义，
    /// 且单元测试通过 virtual 覆写替换实现（AISignal 硬性边界行为测试）。
    /// </summary>
    /// <remarks>virtual 供单元测试替换（AISignal 硬性边界行为测试）。</remarks>
    public virtual Task<List<TradingStrategy>> GetStrategiesByStatusAsync(
        StrategyStatus status,
        CancellationToken ct = default)
        => _dataService.GetStrategiesByStatusAsync(status, ct);

    public async Task SaveStrategyAsync(TradingStrategy strategy, CancellationToken ct = default)
    {
        await _dataService.SaveStrategyAsync(strategy, ct).ConfigureAwait(false);
        RaiseStrategiesChanged();
    }

    /// <remarks>virtual 供单元测试替换（AISignal 硬性边界行为测试）。</remarks>
    public virtual async Task UpdateStrategyStatusAsync(
        string strategyId,
        StrategyStatus status,
        CancellationToken ct = default)
    {
        await _dataService.UpdateStrategyStatusAsync(strategyId, status, ct).ConfigureAwait(false);
        RaiseStrategiesChanged();
    }

    public async Task DeleteStrategyAsync(string strategyId, CancellationToken ct = default)
    {
        await _dataService.DeleteStrategyAsync(strategyId, ct).ConfigureAwait(false);
        RaiseStrategiesChanged();
    }

    private void RaiseStrategiesChanged() => StrategiesChanged?.Invoke(this, EventArgs.Empty);
}