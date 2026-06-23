using MarketAssistant.Trading.Models;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 交易执行工具 —— 仅 Crypto 市场
/// </summary>
public interface ITradingExecutionTools : IToolsProvider
{
    Task<AccountBalanceSummary> GetAccountBalanceAsync(CancellationToken cancellationToken = default);
    Task<List<PositionInfo>> GetCurrentPositionsAsync(CancellationToken cancellationToken = default);
    Task<TradeResult> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null, CancellationToken cancellationToken = default);
    Task<OrderStatusInfo> GetOrderStatusAsync(string symbol, long orderId, CancellationToken cancellationToken = default);
    Task<bool> CancelOrderAsync(string symbol, long orderId, CancellationToken cancellationToken = default);
}
