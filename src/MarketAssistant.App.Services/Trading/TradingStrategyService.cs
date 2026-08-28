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

    public Task<TradingStrategy?> GetStrategyAsync(string strategyId, CancellationToken ct = default)
        => _dataService.GetStrategyAsync(strategyId, ct);

    /// <remarks>virtual 供单元测试替换（AISignal 硬性边界行为测试）。</remarks>
    public virtual Task<List<TradingStrategy>> GetStrategiesByStatusAsync(
        StrategyStatus status,
        CancellationToken ct = default)
        => _dataService.GetStrategiesByStatusAsync(status, ct);

    public Task<List<TradingStrategy>> GetAllStrategiesAsync(CancellationToken ct = default)
        => _dataService.GetAllStrategiesAsync(ct);

    public Task<RiskConfig> LoadRiskConfigAsync(CancellationToken ct = default)
        => _dataService.LoadRiskConfigAsync(ct);

    public Task SaveRiskConfigAsync(RiskConfig config, CancellationToken ct = default)
        => _dataService.SaveRiskConfigAsync(config, ct);

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