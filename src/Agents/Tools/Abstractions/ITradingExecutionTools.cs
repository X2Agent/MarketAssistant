using Microsoft.Extensions.AI;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Agents.Tools.Abstractions;

/// <summary>
/// 交易执行工具 —— 仅 Crypto 市场
/// </summary>
public interface ITradingExecutionTools
{
    Task<AccountBalanceSummary> GetAccountBalanceAsync();
    Task<List<PositionInfo>> GetCurrentPositionsAsync();
    Task<TradeResult> PlaceOrderAsync(string symbol, OrderSide side, OrderType type, decimal quantity, decimal? price = null);
    Task<OrderStatusInfo> GetOrderStatusAsync(string symbol, long orderId);
    Task<bool> CancelOrderAsync(string symbol, long orderId);
    IEnumerable<AIFunction> GetFunctions();
}
