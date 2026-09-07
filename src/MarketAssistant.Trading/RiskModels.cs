using System.ComponentModel;

namespace MarketAssistant.Trading.Models;

/// <summary>
/// 风控配置
/// </summary>
public class RiskConfig
{
    public decimal MaxSingleOrderPercent { get; set; } = 3;
    public decimal MaxDailyLossPercent { get; set; } = 5;
    public decimal MaxTotalPositionPercent { get; set; } = 70;
    public int MaxDailyTrades { get; set; } = 15;
    public decimal MinOrderAmount { get; set; } = 10;
    public bool RequireConfirmation { get; set; } = true;
    public decimal ConfirmationThreshold { get; set; } = 500;

    /// <summary>
    /// 最大回撤熔断。累计回撤超过此百分比时停止所有交易。
    /// </summary>
    public decimal MaxDrawdownPercent { get; set; } = 20;

    /// <summary>
    /// 单 symbol 最大仓位占比（0 表示不限制）。
    /// </summary>
    public decimal MaxSinglePositionPercent { get; set; } = 30;
}

/// <summary>
/// 风控检查结果
/// </summary>
public class RiskCheckResult
{
    public bool Passed { get; set; }
    public bool NeedsConfirmation { get; set; }
    public string? Reason { get; set; }

    public static RiskCheckResult Pass() => new() { Passed = true };
    public static RiskCheckResult Reject(string reason) => new() { Passed = false, Reason = reason };
    public static RiskCheckResult RequireConfirmation(string reason) =>
        new() { Passed = false, NeedsConfirmation = true, Reason = reason };
}

/// <summary>
/// 日统计
/// </summary>
public class DailyStats
{
    public string Date { get; set; } = string.Empty;
    public int TradeCount { get; set; }
    public decimal TotalPnl { get; set; }
    public decimal TotalCommission { get; set; }
}
