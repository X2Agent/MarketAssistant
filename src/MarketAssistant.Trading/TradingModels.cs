using System.ComponentModel;

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

[Description("交易策略配置")]
public class TradingStrategy
{
    [Description("策略唯一ID")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Description("交易对符号（如BTCUSDT）")]
    public string Symbol { get; set; } = string.Empty;
    [Description("策略类型：StopLoss/TakeProfit/TrailingStop/GridTrading/DCA/AISignal")]
    public StrategyType Type { get; set; }
    [Description("策略状态：Active/Paused/Completed/Failed")]
    public StrategyStatus Status { get; set; }
    [Description("交易方向：Buy或Sell")]
    public OrderSide Side { get; set; }
    [Description("触发价格")]
    public decimal TriggerPrice { get; set; }
    [Description("止损价格")]
    public decimal? StopLossPrice { get; set; }
    [Description("止盈价格")]
    public decimal? TakeProfitPrice { get; set; }
    [Description("交易数量（DCA策略为USDT金额）")]
    public decimal Quantity { get; set; }

    [Description("下单类型：Market（市价）或Limit（限价）")]
    public OrderType OrderType { get; set; } = OrderType.Market;

    [Description("滑点容忍度（0-1），仅限价单生效")]
    public decimal SlippageTolerance { get; set; } = 0.003m;

    public string QuantityLabel => Type == StrategyType.DCA
        ? $"定投: {Quantity:F2} USDT"
        : $"数量: {Quantity:F6}";
    [Description("最大仓位占比限制（%）")]
    public decimal? MaxPositionPercent { get; set; }
    [Description("自定义策略参数（JSON）")]
    public string? CustomParams { get; set; }
    [Description("策略创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Description("上次触发时间")]
    public DateTime? LastTriggeredAt { get; set; }
    [Description("已执行次数")]
    public int ExecutionCount { get; set; }
    [Description("最大执行次数限制")]
    public int? MaxExecutions { get; set; }

    [Description("追踪止损的峰值/谷值价格")]
    public decimal? TrailingPeakPrice { get; set; }
}

#endregion

#region 交易记录

[Description("交易执行记录")]
public class TradeRecord
{
    [Description("记录唯一ID")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [Description("关联策略ID")]
    public string StrategyId { get; set; } = string.Empty;
    [Description("交易对符号")]
    public string Symbol { get; set; } = string.Empty;
    [Description("交易方向")]
    public OrderSide Side { get; set; }
    [Description("订单类型")]
    public OrderType OrderType { get; set; }
    [Description("请求交易数量")]
    public decimal RequestedQty { get; set; }
    [Description("实际成交数量")]
    public decimal ExecutedQty { get; set; }
    [Description("请求价格（限价单）")]
    public decimal? RequestedPrice { get; set; }
    [Description("实际成交价格")]
    public decimal ExecutedPrice { get; set; }
    [Description("手续费")]
    public decimal Commission { get; set; }
    [Description("手续费币种")]
    public string CommissionAsset { get; set; } = string.Empty;
    [Description("订单状态：Pending/Filled/PartiallyFilled/Cancelled/Failed")]
    public TradeRecordStatus Status { get; set; }
    [Description("交易所订单ID")]
    public long BinanceOrderId { get; set; }
    [Description("AI下单理由")]
    public string? AIReasoning { get; set; }
    [Description("创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Description("完成时间")]
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

[Description("账户余额汇总")]
public class AccountBalanceSummary
{
    [Description("账户总资产价值（USDT）")]
    public decimal TotalValueUSDT { get; set; }
    [Description("各币种余额明细")]
    public List<AssetBalance> Assets { get; set; } = [];
}

[Description("单币种资产余额")]
public class AssetBalance
{
    [Description("币种名称（如BTC、ETH、USDT）")]
    public string Asset { get; set; } = string.Empty;
    [Description("可用余额")]
    public decimal Free { get; set; }
    [Description("冻结余额（挂单中）")]
    public decimal Locked { get; set; }
    [Description("折合USDT价值")]
    public decimal ValueUSDT { get; set; }
}

[Description("持仓信息")]
public class PositionInfo
{
    [Description("交易对符号")]
    public string Symbol { get; set; } = string.Empty;
    [Description("持仓数量")]
    public decimal Quantity { get; set; }
    [Description("开仓均价")]
    public decimal EntryPrice { get; set; }
    [Description("当前价格")]
    public decimal CurrentPrice { get; set; }
    [Description("未实现盈亏（USDT）")]
    public decimal UnrealizedPnl { get; set; }
    [Description("未实现盈亏百分比（%）")]
    public decimal UnrealizedPnlPercent { get; set; }
}

[Description("交易执行结果")]
public class TradeResult
{
    [Description("是否成功")]
    public bool Success { get; set; }
    [Description("失败时的错误信息")]
    public string? ErrorMessage { get; set; }
    [Description("成功时的交易记录")]
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

[Description("订单状态查询结果")]
public class OrderStatusInfo
{
    [Description("订单ID")]
    public long OrderId { get; set; }
    [Description("交易对符号")]
    public string Symbol { get; set; } = string.Empty;
    [Description("订单状态")]
    public string Status { get; set; } = string.Empty;
    [Description("已成交数量")]
    public decimal ExecutedQty { get; set; }
    [Description("成交价格")]
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
