using System.ComponentModel;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币交易账户只读查询工具（余额、持仓、订单状态），供 TradingAgent 使用。
/// 下单与撤单一律不暴露给模型：由 AISignalStrategyExecutor 解析决策后，经 TradeExecutor 统一风控执行。
/// </summary>
public class CryptoTradingExecutionTools(
    CryptoPortfolioService portfolioService,
    [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
    ILogger<CryptoTradingExecutionTools> logger) : ITradingExecutionTools
{
    private readonly CryptoPortfolioService _portfolioService = portfolioService;
    private readonly IExchangeClient _exchangeClient = exchangeClient;
    private readonly ILogger<CryptoTradingExecutionTools> _logger = logger;

    [Description("查询Binance账户余额，返回总资产价值(USDT)和各币种余额明细")]
    public Task<AccountBalanceSummary> GetAccountBalanceAsync(CancellationToken cancellationToken = default)
    {
        return ToolExecutor.ExecuteAsync("查询账户余额", "-", _logger,
            ct => _portfolioService.GetAccountBalanceSummaryAsync(ct), cancellationToken);
    }

    [Description("查询当前持仓列表，显示每个币种的数量、入场均价、当前价和未实现盈亏")]
    public Task<List<PositionInfo>> GetCurrentPositionsAsync(CancellationToken cancellationToken = default)
    {
        return ToolExecutor.ExecuteAsync("查询当前持仓", "-", _logger,
            ct => _portfolioService.GetCurrentPositionsAsync(ct), cancellationToken);
    }

    [Description("查询指定订单的状态")]
    public Task<OrderStatusInfo> GetOrderStatusAsync(
        [Description("交易对")] string symbol,
        [Description("Binance订单ID")] long orderId,
        CancellationToken cancellationToken = default)
    {
        return ToolExecutor.ExecuteAsync("查询订单状态", symbol, _logger, async ct =>
        {
            symbol = CryptoSymbolConverter.ToBinanceFormat(symbol);
            var order = await _exchangeClient.GetOrderAsync(symbol, orderId.ToString(), ct);
            var executedPrice = order.AveragePrice > 0
                ? order.AveragePrice
                : (order.CumulativeQuoteQty > 0 && order.ExecutedQty > 0
                    ? order.CumulativeQuoteQty / order.ExecutedQty
                    : order.Price);
            return new OrderStatusInfo
            {
                OrderId = long.TryParse(order.OrderId, out var parsedOrderId) ? parsedOrderId : 0,
                Symbol = order.Symbol,
                Status = order.Status,
                ExecutedQty = order.ExecutedQty,
                ExecutedPrice = executedPrice
            };
        }, cancellationToken);
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetAccountBalanceAsync);
        yield return AIFunctionFactory.Create(GetCurrentPositionsAsync);
        yield return AIFunctionFactory.Create(GetOrderStatusAsync);
    }
}
