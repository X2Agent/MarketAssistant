using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Dialog;

/// <summary>
/// 自动交易人审确认的展示数据。
/// 超时本身由调用方的取消令牌驱动，<see cref="Deadline"/> 只用于窗口内的倒计时展示。
/// </summary>
/// <param name="EnvironmentText">交易环境名称（如“Binance 实盘合约”），让审批人知道订单落到哪里</param>
/// <param name="IsTestnet">测试网 / Demo 环境（虚拟资金）</param>
public sealed record TradeApprovalRequest(
    string Symbol,
    OrderSide Side,
    decimal Price,
    decimal Quantity,
    string Reason,
    string EnvironmentText,
    bool IsTestnet,
    DateTimeOffset Deadline)
{
    /// <summary>名义金额（参考价 × 数量），风控的确认阈值按它判定。</summary>
    public decimal Notional => Price * Quantity;
}
