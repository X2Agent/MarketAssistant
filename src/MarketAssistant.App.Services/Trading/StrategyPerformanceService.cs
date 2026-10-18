using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 策略绩效统计（实盘口径）。
/// </summary>
/// <param name="StrategyId">策略 ID。</param>
/// <param name="ClosedTrades">平仓成交笔数（有已实现盈亏的成交）。</param>
/// <param name="WinningTrades">盈利笔数。</param>
/// <param name="TotalPnl">累计已实现盈亏（USDT）。</param>
/// <param name="WinRate">胜率（0-100）。</param>
/// <param name="ProfitFactor">盈亏比（总盈利 / 总亏损绝对值）；无亏损交易时为 null。</param>
/// <param name="MaxDrawdown">累计盈亏曲线的最大回撤（USDT）。</param>
public sealed record StrategyPerformance(
    string StrategyId,
    string Symbol,
    int ClosedTrades,
    int WinningTrades,
    decimal TotalPnl,
    decimal WinRate,
    decimal? ProfitFactor,
    decimal MaxDrawdown);

/// <summary>
/// 影子模式绩效统计（与 <see cref="StrategyPerformance"/> 同口径，便于直接对比）。
/// </summary>
public sealed record ShadowPerformance(
    string? StrategyId,
    int ClosedTrades,
    int WinningTrades,
    decimal TotalPnl,
    decimal WinRate,
    decimal? ProfitFactor,
    decimal MaxDrawdown);

/// <summary>
/// 策略绩效统计服务：把成交记录折算为胜率、盈亏比、最大回撤等可决策指标。
/// </summary>
/// <remarks>
/// 为什么需要它：只有成交列表时，用户无法回答"这个策略到底赚不赚"。
/// 统计口径与实盘一致（只计有已实现盈亏的平仓成交），影子与实盘可逐项对比，
/// 这是"先影子验证再上实盘"闭环的最后一环。
/// </remarks>
public sealed class StrategyPerformanceService(
    TradingDataService dataService,
    ShadowTradingService shadowTradingService,
    ILogger<StrategyPerformanceService> logger)
{
    /// <summary>统计单条策略的实盘绩效。无平仓成交时返回 null。</summary>
    public async Task<StrategyPerformance?> GetAsync(string strategyId, CancellationToken ct = default)
    {
        var records = await dataService.GetRecordsByStrategyAsync(strategyId, ct).ConfigureAwait(false);
        return Build(strategyId, records);
    }

    /// <summary>统计全部策略的实盘绩效（无平仓成交的策略不出现）。</summary>
    public async Task<IReadOnlyList<StrategyPerformance>> GetAllAsync(CancellationToken ct = default)
    {
        var strategies = await dataService.GetAllStrategiesAsync(ct).ConfigureAwait(false);
        var results = new List<StrategyPerformance>();

        foreach (var strategy in strategies)
        {
            var performance = await GetAsync(strategy.Id, ct).ConfigureAwait(false);
            if (performance != null)
                results.Add(performance);
        }

        return results;
    }

    /// <summary>统计影子模式绩效（可按策略过滤）。</summary>
    public async Task<ShadowPerformance> GetShadowAsync(string? strategyId = null, CancellationToken ct = default)
    {
        try
        {
            var trades = await shadowTradingService.LoadAsync(strategyId, ct: ct).ConfigureAwait(false);
            var pnls = trades
                .Where(trade => trade.RealizedPnl != 0)
                .OrderBy(trade => trade.CreatedAt)
                .Select(trade => trade.RealizedPnl)
                .ToList();

            return BuildShadow(strategyId, pnls);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "统计影子绩效失败: {StrategyId}", strategyId);
            return BuildShadow(strategyId, []);
        }
    }

    private static StrategyPerformance? Build(string strategyId, List<TradeRecord> records)
    {
        var pnls = records
            .Where(record => record.RealizedPnl != 0)
            .OrderBy(record => record.CreatedAt)
            .Select(record => record.RealizedPnl)
            .ToList();

        if (pnls.Count == 0)
            return null;

        var symbol = records.FirstOrDefault()?.Symbol ?? string.Empty;
        return new StrategyPerformance(
            strategyId,
            symbol,
            pnls.Count,
            pnls.Count(pnl => pnl > 0),
            pnls.Sum(),
            pnls.Count(pnl => pnl > 0) / (decimal)pnls.Count * 100,
            CalculateProfitFactor(pnls),
            CalculateMaxDrawdown(pnls));
    }

    private static ShadowPerformance BuildShadow(string? strategyId, List<decimal> pnls)
    {
        if (pnls.Count == 0)
            return new ShadowPerformance(strategyId, 0, 0, 0m, 0m, null, 0m);

        return new ShadowPerformance(
            strategyId,
            pnls.Count,
            pnls.Count(pnl => pnl > 0),
            pnls.Sum(),
            pnls.Count(pnl => pnl > 0) / (decimal)pnls.Count * 100,
            CalculateProfitFactor(pnls),
            CalculateMaxDrawdown(pnls));
    }

    /// <summary>盈亏比 = 总盈利 / 总亏损绝对值；无亏损交易时无定义（返回 null）。</summary>
    private static decimal? CalculateProfitFactor(List<decimal> pnls)
    {
        var grossProfit = pnls.Where(pnl => pnl > 0).Sum();
        var grossLoss = Math.Abs(pnls.Where(pnl => pnl < 0).Sum());

        return grossLoss == 0 ? null : grossProfit / grossLoss;
    }

    /// <summary>累计盈亏曲线的最大回撤（峰谷最大跌幅）。</summary>
    private static decimal CalculateMaxDrawdown(List<decimal> pnls)
    {
        var cumulative = 0m;
        var peak = 0m;
        var maxDrawdown = 0m;

        foreach (var pnl in pnls)
        {
            cumulative += pnl;
            peak = Math.Max(peak, cumulative);
            maxDrawdown = Math.Max(maxDrawdown, peak - cumulative);
        }

        return maxDrawdown;
    }
}
