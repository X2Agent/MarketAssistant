using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Services.Trading;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

/// <summary>
/// 交易监控页 - 策略绩效部分（实盘统计 + 影子模式对照）。
/// </summary>
public partial class TradeMonitorViewModel
{
    private const string EmptyPerformanceHint = "暂无平仓成交，成交后此处显示胜率 / 总盈亏 / 最大回撤";

    /// <summary>是否已有实盘平仓成交（决定展示指标还是引导文案）。</summary>
    [ObservableProperty]
    private bool _hasPerformance;

    /// <summary>没有指标可展示时的说明：区分“还没有平仓成交”与“加载失败”。</summary>
    [ObservableProperty]
    private string _performanceHintText = EmptyPerformanceHint;

    [ObservableProperty]
    private int _performanceStrategyCount;

    [ObservableProperty]
    private int _performanceClosedTrades;

    /// <summary>总体胜率（0-100）。</summary>
    [ObservableProperty]
    private decimal _performanceWinRatePercent;

    /// <summary>各策略累计已实现盈亏之和（USDT）。</summary>
    [ObservableProperty]
    private decimal _performanceTotalPnl;

    /// <summary>各策略最大回撤中的最大值（USDT）；不同策略的回撤发生在不同时段，不能相加。</summary>
    [ObservableProperty]
    private decimal _performanceMaxDrawdown;

    /// <summary>影子模式绩效摘要。</summary>
    [ObservableProperty]
    private string _shadowPerformanceText = "暂无影子成交";

    /// <summary>加载策略绩效（实盘总体 + 影子）。</summary>
    private async Task LoadPerformanceAsync()
    {
        try
        {
            var performances = await _performanceService.GetAllAsync();
            HasPerformance = performances.Count > 0;
            if (HasPerformance)
            {
                var closedTrades = performances.Sum(p => p.ClosedTrades);
                PerformanceStrategyCount = performances.Count;
                PerformanceClosedTrades = closedTrades;
                PerformanceWinRatePercent = performances.Sum(p => p.WinningTrades) / (decimal)closedTrades * 100;
                PerformanceTotalPnl = performances.Sum(p => p.TotalPnl);
                PerformanceMaxDrawdown = performances.Max(p => p.MaxDrawdown);
            }
            else
            {
                PerformanceHintText = EmptyPerformanceHint;
            }

            var shadow = await _performanceService.GetShadowAsync();
            ShadowPerformanceText = shadow.ClosedTrades == 0
                ? "暂无影子成交"
                : $"影子平仓 {shadow.ClosedTrades} 笔 · 胜率 {shadow.WinRate:F1}% · "
                  + $"总盈亏 {shadow.TotalPnl:+0.00;-0.00;0.00} USDT";
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "加载策略绩效失败");
            HasPerformance = false;
            PerformanceHintText = "绩效加载失败，下次刷新时重试";
        }
    }
}
