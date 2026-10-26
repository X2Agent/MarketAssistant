using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Services.Trading;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

/// <summary>
/// 交易监控页 - 策略绩效部分（实盘统计 + 影子模式对照）。
/// </summary>
public partial class TradeMonitorViewModel
{
    /// <summary>总体实盘绩效摘要（无平仓成交时显示引导文案）。</summary>
    [ObservableProperty]
    private string _overallPerformanceText = "暂无平仓成交，成交后此处显示胜率 / 盈亏比 / 最大回撤";

    /// <summary>影子模式绩效摘要。</summary>
    [ObservableProperty]
    private string _shadowPerformanceText = "暂无影子成交";

    /// <summary>加载策略绩效（实盘总体 + 影子）。</summary>
    private async Task LoadPerformanceAsync()
    {
        try
        {
            var performances = await _performanceService.GetAllAsync();
            if (performances.Count == 0)
            {
                OverallPerformanceText = "暂无平仓成交，成交后此处显示胜率 / 盈亏比 / 最大回撤";
            }
            else
            {
                var closed = performances.Sum(p => p.ClosedTrades);
                var won = performances.Sum(p => p.WinningTrades);
                var pnl = performances.Sum(p => p.TotalPnl);
                var drawdown = performances.Max(p => p.MaxDrawdown);
                OverallPerformanceText =
                    $"{performances.Count} 个策略 · 平仓 {closed} 笔 · 胜率 {won / (decimal)closed * 100:F1}% · "
                    + $"总盈亏 {pnl:+0.00;-0.00;0.00} USDT · 最大回撤 {drawdown:F2} USDT";
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
        }
    }
}
