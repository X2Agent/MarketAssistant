using MarketAssistant.Applications.Crypto;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using System.Globalization;

namespace MarketAssistant.Services.Trading.Exchanges;

/// <summary>
/// Binance 交易所适配器，将 BinanceAccountServiceBase 包装为统一的 IExchangeClient 接口。
/// 通过构造参数注入不同的账户服务（现货实盘/Testnet/合约实盘/Testnet）与名称，消除重复实现。
/// </summary>
public class BinanceExchangeClient : IExchangeClient
{
    private readonly BinanceAccountServiceBase _accountService;
    private readonly string _exchangeName;
    private readonly string? _positionSide;

    /// <param name="accountService">账户服务（现货或合约）</param>
    /// <param name="exchangeName">交易所显示名（"Binance" / "Binance Spot Testnet" / "Binance Futures" / "Binance Futures Testnet"）</param>
    /// <param name="positionSide">合约单向模式下默认持仓方向（"BOTH"），现货传 null</param>
    public BinanceExchangeClient(BinanceAccountServiceBase accountService, string exchangeName, string? positionSide = null)
    {
        _accountService = accountService;
        _exchangeName = exchangeName;
        _positionSide = positionSide;
    }

    public string ExchangeName => _exchangeName;

    /// <summary>
    /// 合约模式由 positionSide 是否为 null 决定（合约传 "BOTH"，现货传 null）
    /// </summary>
    public bool IsFutures => !string.IsNullOrEmpty(_positionSide);

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
        bool reduceOnly = false,
        string? positionSide = null,
        decimal? stopPrice = null,
        int? trailingDelta = null,
        CancellationToken ct = default)
    {
        var response = await _accountService.PlaceOrderAsync(
            symbol, side.ToString().ToUpper(), type.ToString().ToUpper(),
            quantity, price, clientOrderId, _positionSide ?? positionSide, reduceOnly,
            stopPrice, trailingDelta, ct);

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

    /// <summary>
    /// 现货无持仓概念，返回空列表。合约持仓由 BinanceFuturesExchangeClient 覆写。
    /// </summary>
    public virtual Task<List<ExchangePosition>> GetPositionsAsync(
        string? instrumentSymbol = null, CancellationToken ct = default)
        => Task.FromResult<List<ExchangePosition>>([]);

    /// <summary>
    /// 现货无需设置杠杆，为空操作。合约由子类覆写。
    /// </summary>
    public virtual Task SetLeverageAsync(string instrumentSymbol, int leverage, CancellationToken ct = default)
        => Task.CompletedTask;

    /// <summary>
    /// 将币安订单响应映射为统一的 ExchangeOrderResult。
    /// </summary>
    private protected static ExchangeOrderResult MapOrderResponse(BinanceOrderResponse response)
    {
        // 汇总所有 fills 的手续费（仅现货下单响应包含 fills，合约/查询/撤单响应不包含）
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
            AveragePrice = decimal.TryParse(response.AvgPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var ap) ? ap : 0,
            CumulativeQuoteQty = decimal.TryParse(response.CummulativeQuoteQty, NumberStyles.Number, CultureInfo.InvariantCulture, out var cq) ? cq : 0,
            FillCommission = totalCommission,
            CommissionAsset = commissionAsset
        };
    }
}
