using System.ComponentModel;

namespace MarketAssistant.Trading.Models;

/// <summary>
/// 交易策略配置（持久化模型，由 TradingDataService 读写）
/// </summary>
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

    /// <summary>
    /// 原生条件单的交易所订单 ID。非空时表示该策略已提交为交易所原生条件单，
    /// 客户端无需再轮询评估；策略完成/删除时应调用 TryCancelNativeConditionalOrderAsync 取消。
    /// </summary>
    [Description("原生条件单订单ID")]
    public string? NativeOrderId { get; set; }
}
