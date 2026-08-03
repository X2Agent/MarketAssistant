using System.ComponentModel;

namespace MarketAssistant.Trading.Models;

#region Agent 视图模型

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

#endregion

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
