namespace MarketAssistant.Trading.Models;

#region 枚举类型

public enum StrategyType
{
    StopLoss,
    TakeProfit,
    TrailingStop,
    GridTrading,
    DCA,
    AISignal
}

public enum StrategyStatus { Active, Paused, Completed, Failed }

public enum OrderSide { Buy, Sell }

public enum OrderType { Market, Limit }

public enum TradeRecordStatus { Pending, Filled, PartiallyFilled, Cancelled, Failed }

/// <summary>
/// 持仓方向（与 OrderSide 区分，用于 positions 表）
/// </summary>
public enum PositionSide { Long, Short }

#endregion

#region 交易策略

public class TradingStrategy
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Symbol { get; set; } = string.Empty;
    public StrategyType Type { get; set; }
    public StrategyStatus Status { get; set; }
    public OrderSide Side { get; set; }
    public decimal TriggerPrice { get; set; }
    public decimal? StopLossPrice { get; set; }
    public decimal? TakeProfitPrice { get; set; }
    public decimal Quantity { get; set; }

    /// <summary>
    /// 下单类型（默认市价）。设为 Limit 时按滑点保护价下限价单。
    /// </summary>
    public OrderType OrderType { get; set; } = OrderType.Market;

    /// <summary>
    /// 滑点容忍度（0-1，默认 0.3%）。仅 OrderType=Limit 时生效。
    /// </summary>
    public decimal SlippageTolerance { get; set; } = 0.003m;

    /// <summary>
    /// 显示用标签：DCA 策略以 USDT 定投金额计，其他策略为代币数量。
    /// </summary>
    public string QuantityLabel => Type == StrategyType.DCA
        ? $"定投: {Quantity:F2} USDT"
        : $"数量: {Quantity:F6}";
    public decimal? MaxPositionPercent { get; set; }
    public string? CustomParams { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTriggeredAt { get; set; }
    public int ExecutionCount { get; set; }
    public int? MaxExecutions { get; set; }

    /// <summary>
    /// 追踪止损的峰值/谷值价格（持久化，重启不丢失）。
    /// Sell 侧记录最高价，Buy 侧记录最低价。
    /// </summary>
    public decimal? TrailingPeakPrice { get; set; }
}

#endregion

#region 交易记录

public class TradeRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string StrategyId { get; set; } = string.Empty;
    public string Symbol { get; set; } = string.Empty;
    public OrderSide Side { get; set; }
    public OrderType OrderType { get; set; }
    public decimal RequestedQty { get; set; }
    public decimal ExecutedQty { get; set; }
    public decimal? RequestedPrice { get; set; }
    public decimal ExecutedPrice { get; set; }
    public decimal Commission { get; set; }
    public string CommissionAsset { get; set; } = string.Empty;
    public TradeRecordStatus Status { get; set; }
    public long BinanceOrderId { get; set; }
    public string? AIReasoning { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

#endregion

#region 风控配置

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

#endregion

#region 日统计

public class DailyStats
{
    public string Date { get; set; } = string.Empty;
    public int TradeCount { get; set; }
    public decimal TotalPnl { get; set; }
    public decimal TotalCommission { get; set; }
}

#endregion

#region Agent 辅助模型

public class AccountBalanceSummary
{
    public decimal TotalValueUSDT { get; set; }
    public List<AssetBalance> Assets { get; set; } = [];
}

public class AssetBalance
{
    public string Asset { get; set; } = string.Empty;
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal ValueUSDT { get; set; }
}

public class PositionInfo
{
    public string Symbol { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal UnrealizedPnl { get; set; }
    public decimal UnrealizedPnlPercent { get; set; }
}

public class TradeResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public TradeRecord? Record { get; set; }
}

/// <summary>
/// 持仓记录（FIFO 匹配追踪）。每笔开仓对应一行，平仓时按时间顺序消耗。
/// </summary>
public class Position
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Symbol { get; set; } = string.Empty;
    public PositionSide Side { get; set; }
    /// <summary>
    /// 原始开仓数量
    /// </summary>
    public decimal Quantity { get; set; }
    /// <summary>
    /// 开仓价
    /// </summary>
    public decimal EntryPrice { get; set; }
    /// <summary>
    /// 已平仓数量
    /// </summary>
    public decimal ClosedQuantity { get; set; }
    public string? StrategyId { get; set; }
    public DateTime OpenedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 剩余未平仓数量
    /// </summary>
    public decimal RemainingQuantity => Quantity - ClosedQuantity;
}

public class OrderStatusInfo
{
    public long OrderId { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal ExecutedQty { get; set; }
    public decimal ExecutedPrice { get; set; }
}

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
/// 交易上下文，用于在 Agent 工具调用链中传递当前策略 ID
/// </summary>
public static class TradingContext
{
    private static readonly AsyncLocal<string?> _strategyId = new();

    public static string? CurrentStrategyId
    {
        get => _strategyId.Value;
        set => _strategyId.Value = value;
    }
}

#endregion

#region 策略参数模型

/// <summary>
/// 网格交易策略参数
/// </summary>
public class GridTradingParams
{
    /// <summary>
    /// 网格上界价格
    /// </summary>
    public decimal UpperPrice { get; set; }

    /// <summary>
    /// 网格下界价格
    /// </summary>
    public decimal LowerPrice { get; set; }

    /// <summary>
    /// 网格数量（将在上界与下界之间均匀分布）
    /// </summary>
    public int GridCount { get; set; } = 5;

    /// <summary>
    /// 每格交易数量
    /// </summary>
    public decimal QuantityPerGrid { get; set; }

    /// <summary>
    /// 上次触发的网格价格索引（用于追踪状态）
    /// </summary>
    public int LastTriggeredIndex { get; set; } = -1;

    /// <summary>
    /// 破网止损价（可选）。价格跌破此值时清仓所有网格多头仓位。
    /// </summary>
    public decimal? StopLossPrice { get; set; }

    /// <summary>
    /// 破网止盈价（可选）。价格涨破此值时清仓所有网格空头仓位。
    /// </summary>
    public decimal? TakeProfitPrice { get; set; }

    /// <summary>
    /// 计算网格间距
    /// </summary>
    public decimal GridSpacing => GridCount > 1 ? (UpperPrice - LowerPrice) / GridCount : 0;

    /// <summary>
    /// 获取指定索引处的网格价格
    /// </summary>
    public decimal GetGridPrice(int index) => LowerPrice + GridSpacing * index;
}

/// <summary>
/// 定投（DCA）策略参数
/// </summary>
public class DCAParams
{
    /// <summary>
    /// 定投间隔（秒）
    /// </summary>
    public int IntervalSeconds { get; set; } = 86400; // 默认每天

    /// <summary>
    /// 每次定投数量
    /// </summary>
    public decimal AmountPerInterval { get; set; }

    /// <summary>
    /// 价格上限（高于此价不买入，0 表示无限制）
    /// </summary>
    public decimal MaxBuyPrice { get; set; }

    /// <summary>
    /// 价格下限触发加倍（低于此价双倍买入，0 表示不启用）
    /// </summary>
    public decimal DoubleBuyBelowPrice { get; set; }

    /// <summary>
    /// 加倍冷却期（秒）。两次加倍之间至少间隔此时间，默认 24 小时。
    /// 防止瀑布式下跌中连续加倍耗尽资金。
    /// </summary>
    public int DoubleBuyCooldownSeconds { get; set; } = 86400;

    /// <summary>
    /// 加倍次数上限（0 表示不限制）。防止无限制加倍。
    /// </summary>
    public int MaxDoubleBuyCount { get; set; } = 3;

    /// <summary>
    /// 上次加倍时间（ISO 8601）。用于冷却期判断，持久化在 CustomParams 中。
    /// </summary>
    public string? LastDoubleBuyAt { get; set; }

    /// <summary>
    /// 已加倍次数。用于上限判断，持久化在 CustomParams 中。
    /// </summary>
    public int DoubleBuyCount { get; set; }
}

#endregion
