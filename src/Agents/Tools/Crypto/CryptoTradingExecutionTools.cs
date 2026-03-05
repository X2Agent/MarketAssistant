using System.ComponentModel;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Applications.Crypto;
using MarketAssistant.Services.Data;
using MarketAssistant.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Agents.Tools.Crypto;

/// <summary>
/// 虚拟币交易执行工具实现，供 TradingAgent 使用。下单委托给 TradeExecutor 统一入口。
/// </summary>
public class CryptoTradingExecutionTools : ITradingExecutionTools
{
    private readonly BinanceAccountService _accountService;
    private readonly BinanceMarketDataService _marketDataService;
    private readonly TradeExecutor _tradeExecutor;
    private readonly ILogger<CryptoTradingExecutionTools> _logger;

    public CryptoTradingExecutionTools(
        BinanceAccountService accountService,
        BinanceMarketDataService marketDataService,
        TradeExecutor tradeExecutor,
        ILogger<CryptoTradingExecutionTools> logger)
    {
        _accountService = accountService;
        _marketDataService = marketDataService;
        _tradeExecutor = tradeExecutor;
        _logger = logger;
    }

    [Description("查询Binance账户余额，返回总资产价值(USDT)和各币种余额明细")]
    public async Task<AccountBalanceSummary> GetAccountBalanceAsync()
    {
        var accountInfo = await _accountService.GetAccountInfoAsync();
        var summary = new AccountBalanceSummary();

        foreach (var balance in accountInfo.Balances)
        {
            if (!decimal.TryParse(balance.Free, out var free) || !decimal.TryParse(balance.Locked, out var locked))
                continue;
            if (free == 0 && locked == 0)
                continue;

            var assetBalance = new AssetBalance
            {
                Asset = balance.Asset,
                Free = free,
                Locked = locked
            };

            if (balance.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
            {
                assetBalance.ValueUSDT = free + locked;
            }
            else
            {
                try
                {
                    var ticker = await _marketDataService.Get24hrTickerAsync($"{balance.Asset}USDT");
                    if (ticker != null)
                        assetBalance.ValueUSDT = (free + locked) * ticker.LastPrice;
                }
                catch { /* non-USDT pairs can be skipped */ }
            }

            summary.TotalValueUSDT += assetBalance.ValueUSDT;
            summary.Assets.Add(assetBalance);
        }

        return summary;
    }

    [Description("查询当前持仓列表，显示每个币种的数量、入场均价、当前价和未实现盈亏")]
    public async Task<List<PositionInfo>> GetCurrentPositionsAsync()
    {
        var accountInfo = await _accountService.GetAccountInfoAsync();
        var positions = new List<PositionInfo>();

        foreach (var balance in accountInfo.Balances)
        {
            if (!decimal.TryParse(balance.Free, out var free) || !decimal.TryParse(balance.Locked, out var locked))
                continue;
            var quantity = free + locked;
            if (quantity <= 0 || balance.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
                continue;

            var position = new PositionInfo
            {
                Symbol = $"{balance.Asset}USDT",
                Quantity = quantity
            };

            try
            {
                var ticker = await _marketDataService.Get24hrTickerAsync(position.Symbol);
                if (ticker != null)
                {
                    position.CurrentPrice = ticker.LastPrice;
                    position.EntryPrice = ticker.LastPrice;
                }
            }
            catch { /* skip */ }

            positions.Add(position);
        }

        return positions;
    }

    [Description("下单交易。所有订单会先经过风控检查。symbol格式如BTCUSDT，side为Buy或Sell，type为Market或Limit")]
    public async Task<TradeResult> PlaceOrderAsync(
        [Description("交易对，如BTCUSDT")] string symbol,
        [Description("买卖方向")] OrderSide side,
        [Description("订单类型")] OrderType type,
        [Description("交易数量")] decimal quantity,
        [Description("限价单价格，市价单可不填")] decimal? price = null)
    {
        var effectivePrice = price ?? 0;
        if (type == OrderType.Market && effectivePrice == 0)
        {
            try
            {
                var ticker = await _marketDataService.Get24hrTickerAsync(symbol);
                if (ticker != null)
                    effectivePrice = ticker.LastPrice;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "无法获取 {Symbol} 当前价格", symbol);
            }
        }

        if (effectivePrice <= 0)
            return new TradeResult { Success = false, ErrorMessage = $"无法确定 {symbol} 的有效价格，拒绝下单" };

        var strategyId = TradingContext.CurrentStrategyId ?? "manual";
        return await _tradeExecutor.ExecuteOrderAsync(
            symbol, side, type, quantity, effectivePrice,
            type == OrderType.Limit ? price : null,
            strategyId: strategyId);
    }

    [Description("查询指定订单的状态")]
    public async Task<OrderStatusInfo> GetOrderStatusAsync(
        [Description("交易对")] string symbol,
        [Description("Binance订单ID")] long orderId)
    {
        var order = await _accountService.GetOrderAsync(symbol, orderId);
        return new OrderStatusInfo
        {
            OrderId = order.OrderId,
            Symbol = order.Symbol,
            Status = order.Status,
            ExecutedQty = decimal.TryParse(order.ExecutedQty, out var eq) ? eq : 0,
            ExecutedPrice = decimal.TryParse(order.Price, out var ep) ? ep : 0
        };
    }

    [Description("取消指定订单")]
    public async Task<bool> CancelOrderAsync(
        [Description("交易对")] string symbol,
        [Description("Binance订单ID")] long orderId)
    {
        try
        {
            await _accountService.CancelOrderAsync(symbol, orderId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "取消订单失败: {Symbol} {OrderId}", symbol, orderId);
            return false;
        }
    }

    public IEnumerable<AIFunction> GetFunctions()
    {
        yield return AIFunctionFactory.Create(GetAccountBalanceAsync);
        yield return AIFunctionFactory.Create(GetCurrentPositionsAsync);
        yield return AIFunctionFactory.Create(PlaceOrderAsync);
        yield return AIFunctionFactory.Create(GetOrderStatusAsync);
        yield return AIFunctionFactory.Create(CancelOrderAsync);
    }
}
