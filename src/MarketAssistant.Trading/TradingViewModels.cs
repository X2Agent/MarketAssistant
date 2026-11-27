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

    /// <summary>
    /// 是否为空头持仓（合约）。现货持仓恒为 false。
    /// 数量为绝对值，方向由本字段表达——紧急平仓等场景必须据此决定买卖方向。
    /// </summary>
    [Description("是否空头持仓")]
    public bool IsShort { get; set; }

    /// <summary>标记价（合约）。现货为 0，展示层不得把 0 当真实标记价。</summary>
    [Description("标记价")]
    public decimal MarkPrice { get; set; }

    /// <summary>
    /// 强平价（合约）。交易所未返回（全仓低风险等）或现货场景为 0，
    /// 展示层必须按 0 隐藏，不得把 0 当真实强平价。
    /// </summary>
    [Description("强平价")]
    public decimal LiquidationPrice { get; set; }

    /// <summary>杠杆倍数（合约）。现货为 0。</summary>
    [Description("杠杆倍数")]
    public decimal Leverage { get; set; }

    /// <summary>所属策略 ID（本地账本关联；手动开仓为空）。</summary>
    [Description("所属策略")]
    public string? StrategyId { get; set; }

    /// <summary>是否有可展示的强平价（0 表示交易所未返回或现货）。</summary>
    public bool HasLiquidationPrice => LiquidationPrice > 0;

    /// <summary>是否为杠杆持仓（合约）。现货为 false，用于隐藏杠杆与强平价字段。</summary>
    public bool IsLeveraged => Leverage > 0;

    /// <summary>当前价的名称：合约的 <see cref="CurrentPrice"/> 取标记价，现货取最新成交价。</summary>
    public string CurrentPriceLabel => IsLeveraged ? "标记" : "现价";
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
    /// <summary>
    /// 失败类别。调用方据此区分重试策略：网络类可快速重试，
    /// 拒绝类（风控/用户）重试无意义，应暂停策略并通知用户。
    /// </summary>
    [Description("失败类别")]
    public TradeFailureCategory FailureCategory { get; set; } = TradeFailureCategory.None;
}

/// <summary>
/// 现货 OCO 保护单挂单结果：成功时携带两腿各自的 Pending 交易记录
/// （对账与成交回写按腿独立处理）。
/// </summary>
public class OcoPlacementResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public TradeRecord? StopRecord { get; set; }
    public TradeRecord? TakeProfitRecord { get; set; }
    public TradeFailureCategory FailureCategory { get; set; } = TradeFailureCategory.None;
}

/// <summary>
/// 交易失败类别
/// </summary>
public enum TradeFailureCategory
{
    [Description("无失败")]
    None,

    /// <summary>
    /// 网络类失败（请求超时、连接异常、重试耗尽），短期重试可能恢复
    /// </summary>
    [Description("网络异常")]
    Network,

    /// <summary>
    /// 拒绝类失败（风控拒绝、人工确认被拒或超时、持仓校验不通过），重试不会改变结果
    /// </summary>
    [Description("交易被拒绝")]
    Rejected,

    /// <summary>
    /// 下单请求可能已落地，但收敛查询失败，订单状态未知；禁止自动重试或改下其他订单。
    /// </summary>
    [Description("订单状态未知")]
    OrderStateUnknown,

    [Description("其他失败")]
    Other,
    PostOnlyWouldTake,
    Deferred
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
