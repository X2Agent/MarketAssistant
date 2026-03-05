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
    public decimal? MaxPositionPercent { get; set; }
    public string? CustomParams { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTriggeredAt { get; set; }
    public int ExecutionCount { get; set; }
    public int? MaxExecutions { get; set; }
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
    public decimal MaxSingleOrderPercent { get; set; } = 5;
    public decimal MaxDailyLossPercent { get; set; } = 10;
    public decimal MaxTotalPositionPercent { get; set; } = 80;
    public int MaxDailyTrades { get; set; } = 20;
    public decimal MinOrderAmount { get; set; } = 10;
    public bool RequireConfirmation { get; set; }
    public decimal ConfirmationThreshold { get; set; } = 1000;
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
