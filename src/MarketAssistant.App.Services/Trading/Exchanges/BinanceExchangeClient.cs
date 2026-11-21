using MarketAssistant.Applications.Crypto;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using System.Globalization;

namespace MarketAssistant.Services.Trading.Exchanges;

/// <summary>
/// Binance 交易所适配器，将 BinanceAccountServiceBase 包装为统一的 IExchangeClient 接口。
/// 通过构造参数注入不同的账户服务（现货实盘/Testnet/合约实盘/Testnet）与名称，消除重复实现。
/// </summary>
/// <param name="accountService">账户服务（现货或合约）</param>
/// <param name="exchangeName">交易所显示名（"Binance" / "Binance Spot Testnet" / "Binance Futures" / "Binance Futures Testnet"）</param>
/// <param name="positionSide">合约单向模式下默认持仓方向（"BOTH"），现货传 null</param>
public class BinanceExchangeClient(BinanceAccountServiceBase accountService, string exchangeName, string? positionSide = null) : IExchangeClient
{
    private readonly BinanceAccountServiceBase _accountService = accountService;
    private readonly string _exchangeName = exchangeName;
    private readonly string? _positionSide = positionSide;

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
            Balances = [.. info.Balances.Select(b => new ExchangeBalance
            {
                Asset = b.Asset,
                Free = decimal.TryParse(b.Free, NumberStyles.Number, CultureInfo.InvariantCulture, out var f) ? f : 0,
                Locked = decimal.TryParse(b.Locked, NumberStyles.Number, CultureInfo.InvariantCulture, out var l) ? l : 0
            })]
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
            symbol, side.ToString().ToUpperInvariant(), ToExchangeOrderType(type),
            quantity, price, clientOrderId, _positionSide ?? positionSide, reduceOnly,
            stopPrice, trailingDelta, postOnly: type == OrderType.PostOnlyLimit, ct);

        return MapOrderResponse(response);
    }

    /// <summary>
    /// 枚举名 → 币安订单类型字符串。
    /// 必须显式映射而不是 <c>type.ToString().ToUpperInvariant()</c>：
    /// 后者会把 <see cref="OrderType.StopMarket"/> 变成 "STOPMARKET"（缺下划线），
    /// 被交易所按参数错误拒绝，条件单因此永远挂不上、保护从未真正上链。
    /// </summary>
    internal static string ToExchangeOrderType(OrderType type) => type switch
    {
        OrderType.Market => "MARKET",
        OrderType.Limit => "LIMIT",
        OrderType.StopMarket => "STOP_MARKET",
        OrderType.TakeProfitMarket => "TAKE_PROFIT_MARKET",
        OrderType.TrailingStopMarket => "TRAILING_STOP_MARKET",
        // 现货条件单：触发即市价的 STOP_LOSS / TAKE_PROFIT（与合约 STOP_MARKET 语义一致）
        OrderType.StopLoss => "STOP_LOSS",
        OrderType.TakeProfit => "TAKE_PROFIT",
        // post-only 类型串仍是 LIMIT：合约由账户服务加 timeInForce=GTX，现货由账户服务改写为 LIMIT_MAKER
        OrderType.PostOnlyLimit => "LIMIT",
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "未知的订单类型")
    };

    public async Task<ExchangeOrderResult> GetOrderAsync(
        string symbol, string orderId, CancellationToken ct = default)
    {
        var response = await _accountService.GetOrderAsync(symbol, long.Parse(orderId), ct);
        return MapOrderResponse(response);
    }

    public async Task<ExchangeOrderResult> GetOrderByClientOrderIdAsync(
        string symbol, string clientOrderId, CancellationToken ct = default)
    {
        var response = await _accountService.GetOrderByClientOrderIdAsync(symbol, clientOrderId, ct);
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
        return [.. orders.Select(MapOrderResponse)];
    }

    /// <summary>
    /// 现货保护性 OCO：转交账户服务（仅现货实现，合约客户端调用会抛 NotSupportedException，
    /// 调用方只在 <see cref="IsFutures"/> 为 false 时走此路径）。返回顺序：止损腿、止盈腿。
    /// </summary>
    public async Task<IReadOnlyList<ExchangeOrderResult>> PlaceSpotOcoAsync(
        string symbol, OrderSide side, decimal quantity,
        string? stopClientOrderId, decimal stopPrice,
        string? takeProfitClientOrderId, decimal takeProfitPrice,
        CancellationToken ct = default)
    {
        var ocoResponse = await _accountService.PlaceSpotOcoAsync(
            symbol, side.ToString().ToUpperInvariant(), quantity,
            stopClientOrderId, stopPrice,
            takeProfitClientOrderId, takeProfitPrice, ct);

        // OCO 成员单的 clientOrderId 由交易所按请求回显，按 MC 前缀腿别识别归属
        var stopOrder = ocoResponse.Orders.FirstOrDefault(o =>
            string.Equals(o.ClientOrderId, stopClientOrderId, StringComparison.Ordinal))
            ?? ocoResponse.Orders.FirstOrDefault(o => o.Type == "STOP_LOSS");
        var takeProfitOrder = ocoResponse.Orders.FirstOrDefault(o =>
            string.Equals(o.ClientOrderId, takeProfitClientOrderId, StringComparison.Ordinal))
            ?? ocoResponse.Orders.FirstOrDefault(o => o.Type == "TAKE_PROFIT");

        if (stopOrder == null || takeProfitOrder == null)
            throw new InvalidOperationException(
                $"OCO 响应缺少腿订单: orderListId={ocoResponse.OrderListId}，收到 {ocoResponse.Orders.Count} 腿");

        return [MapOrderResponse(stopOrder), MapOrderResponse(takeProfitOrder)];
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
            ClientOrderId = response.ClientOrderId,
            Status = response.Status,
            Side = response.Side,
            Type = response.Type,
            StopPrice = decimal.TryParse(response.StopPrice, NumberStyles.Number, CultureInfo.InvariantCulture, out var sp) ? sp : 0,
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
