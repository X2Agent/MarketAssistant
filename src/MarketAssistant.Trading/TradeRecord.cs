using System.ComponentModel;

namespace MarketAssistant.Trading.Models;

/// <summary>
/// 交易执行记录（持久化模型）
/// </summary>
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
    public long ExchangeOrderId { get; set; }
    [Description("AI下单理由")]
    public string? AIReasoning { get; set; }
    [Description("创建时间")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [Description("完成时间")]
    public DateTime? CompletedAt { get; set; }
}
