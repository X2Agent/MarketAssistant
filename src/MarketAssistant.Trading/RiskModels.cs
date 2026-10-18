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

    /// <summary>
    /// 合约持仓强平距离预警阈值（%）：标记价距强平价小于该值时发警告。
    /// </summary>
    public decimal LiquidationWarnPercent { get; set; } = 25;

    /// <summary>
    /// 合约持仓强平距离紧急阈值（%）：小于该值时发紧急告警，
    /// 并触发该标的的交易联动门（新开仓信号强制人工确认）。
    /// </summary>
    public decimal LiquidationCriticalPercent { get; set; } = 10;

    /// <summary>
    /// 合约开仓杠杆倍数（1-125）。硬编码会让用户无法按自身风险承受能力调整。
    /// </summary>
    public int FuturesLeverage { get; set; } = 10;

    /// <summary>
    /// 市价单成交滑点告警阈值（%）：成交价相对信号价偏离超过该值时告警。
    /// 市价单无法事前限制滑点，只能事后发现并提示。
    /// </summary>
    public decimal MaxSlippagePercent { get; set; } = 1;

    /// <summary>
    /// 策略限价单超时时间（秒）：挂起超过该时长未成交则撤单并告警，0 表示不自动撤单。
    /// </summary>
    public int LimitOrderTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// 连续亏损熔断次数：当日实现亏损笔数连续达到该值时停止新开仓，0 表示不限制。
    /// </summary>
    public int MaxConsecutiveLosses { get; set; } = 5;

    /// <summary>
    /// 合约名义总敞口上限（占权益百分比）：Σ|持仓名义| + 本单名义不得超过该比例，0 表示不限制。
    /// 现货仓位占比无法反映杠杆放大后的真实风险，这是合约的等效约束。
    /// </summary>
    public decimal MaxFuturesNotionalPercent { get; set; } = 300;

    /// <summary>
    /// 交易所 API 失败熔断阈值（次）：滑动窗口内失败达到该值时拒单并告警，0 表示不限制。
    /// </summary>
    public int ApiFailureThreshold { get; set; } = 5;
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

    /// <summary>
    /// 当日连续亏损笔数（实现盈亏为负累计，盈亏后归零）。用于连续亏损熔断。
    /// 开仓成交（无已实现盈亏）不参与计数，避免每笔开仓把连亏计数清零。
    /// </summary>
    public int ConsecutiveLosses { get; set; }
}
