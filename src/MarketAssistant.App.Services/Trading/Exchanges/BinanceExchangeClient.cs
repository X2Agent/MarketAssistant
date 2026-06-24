using MarketAssistant.Applications.Crypto;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using System.Globalization;

namespace MarketAssistant.Trading.Exchanges;

/// <summary>
/// Binance 交易所适配器，将 BinanceAccountService 包装为统一的 IExchangeClient 接口
/// </summary>
public class BinanceExchangeClient : IExchangeClient
{
    private readonly BinanceAccountService _accountService;

    public string ExchangeName => "Binance";

    public BinanceExchangeClient(BinanceAccountService accountService)
    {
        _accountService = accountService;
    }

    public async Task<ExchangeAccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
    {
        var info = await _accountService.GetAccountInfoAsync(ct);
        return new ExchangeAccountInfo
        {
            CanTrade = info.CanTrade,
            Balances = info.Balances.Select(b => new ExchangeBalance
            {
                Asset = b.Asset,
                Free = decimal.TryParse(b.Free, NumberStyles.Number, CultureInfo.InvariantCulture, out var f) ? f : 0,
                Locked = decimal.TryParse(b.Locked, NumberStyles.Number, CultureInfo.InvariantCulture, out var l) ? l : 0
            }).ToList()
        };
    }

    public async Task<ExchangeOrderResult> PlaceOrderAsync(
        string symbol, OrderSide side, OrderType type,
        decimal quantity, decimal? price = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
    {
        var response = await _accountService.PlaceOrderAsync(
            symbol, side.ToString().ToUpper(), type.ToString().ToUpper(),
            quantity, price, clientOrderId, ct);

        return MapOrderResponse(response);
    }

    public async Task<ExchangeOrderResult> GetOrderAsync(
        string symbol, string orderId, CancellationToken ct = default)
    {
        var response = await _accountService.GetOrderAsync(symbol, long.Parse(orderId), ct);
        return MapOrderResponse(response);
    }

    public async Task<ExchangeOrderResult> CancelOrderAsync(
        string symbol, string orderId, CancellationToken ct = default)
    {
        var response = await _accountService.CancelOrderAsync(symbol, long.Parse(orderId), ct);
        return MapOrderResponse(response);
    }

    public async Task<List<ExchangeOrderResult>> GetOpenOrdersAsync(
        string? symbol = null, CancellationToken ct = default)
    {
        var orders = await _accountService.GetOpenOrdersAsync(symbol, ct);
        return orders.Select(MapOrderResponse).ToList();
    }

    private static ExchangeOrderResult MapOrderResponse(BinanceOrderResponse response)
    {
        // 汇总所有 fills 的手续费（仅下单响应包含 fills，查询/撤单响应不包含）
        decimal totalCommission = 0;
        string? commissionAsset = null;
        if (response.Fills.Count > 0)
        {
            foreach (var fill in response.Fills)
            {
                if (decimal.TryParse(fill.Commission, NumberStyles.Number, CultureInfo.InvariantCulture, out var c))
                    totalCommission += c;
                commissionAsset ??= fill.CommissionAsset;
            }
        }

        return new ExchangeOrderResult
        {
            Symbol = response.Symbol,
            OrderId = response.OrderId.ToString(),
            Status = response.Status,
            Side = response.Side,
            Type = response.Type,
            RequestedQty = decimal.TryParse(response.OrigQty, NumberStyles.Number, CultureInfo.InvariantCulture, out var rq) ? rq : 0,
            ExecutedQty = decimal.TryParse(response.ExecutedQty, NumberStyles.Number, CultureInfo.InvariantCulture, out var eq) ? eq : 0,
            Price = decimal.TryParse(response.Price, NumberStyles.Number, CultureInfo.InvariantCulture, out var p) ? p : 0,
            FillCommission = totalCommission,
            CommissionAsset = commissionAsset
        };
    }
}
