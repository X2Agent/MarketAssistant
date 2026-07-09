using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Trading.Exchanges;

/// <summary>
/// 根据当前交易模式在实盘/模拟盘/Demo/合约客户端之间路由。
/// 通过字典查找活跃客户端，新增模式只需在字典中注册，无需修改路由逻辑。
/// </summary>
public sealed class RoutingExchangeClient : IExchangeClient
{
    private readonly TradingEnvironmentService _environmentService;
    private readonly IReadOnlyDictionary<CryptoTradingMode, IExchangeClient> _clients;

    public RoutingExchangeClient(
        TradingEnvironmentService environmentService,
        IReadOnlyDictionary<CryptoTradingMode, IExchangeClient> clients)
    {
        _environmentService = environmentService;
        _clients = clients;
    }

    public string ExchangeName => GetActiveClient().ExchangeName;

    /// <summary>
    /// 当前是否为合约模式，由底层活跃客户端决定。
    /// </summary>
    public bool IsFutures => GetActiveClient().IsFutures;

    public Task<ExchangeAccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
        => GetActiveClient().GetAccountInfoAsync(ct);

    public Task<ExchangeOrderResult> PlaceOrderAsync(
        string instrumentSymbol,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? price = null,
        string? clientOrderId = null,
        bool reduceOnly = false,
        string? positionSide = null,
        decimal? stopPrice = null,
        int? trailingDelta = null,
        CancellationToken ct = default)
        => GetActiveClient().PlaceOrderAsync(instrumentSymbol, side, type, quantity, price, clientOrderId, reduceOnly, positionSide, stopPrice, trailingDelta, ct);

    public Task<ExchangeOrderResult> GetOrderAsync(
        string instrumentSymbol,
        string orderId,
        CancellationToken ct = default)
        => GetActiveClient().GetOrderAsync(instrumentSymbol, orderId, ct);

    public Task<ExchangeOrderResult> CancelOrderAsync(
        string instrumentSymbol,
        string orderId,
        CancellationToken ct = default)
        => GetActiveClient().CancelOrderAsync(instrumentSymbol, orderId, ct);

    public Task<List<ExchangeOrderResult>> GetOpenOrdersAsync(
        string? instrumentSymbol = null,
        CancellationToken ct = default)
        => GetActiveClient().GetOpenOrdersAsync(instrumentSymbol, ct);

    public Task<List<ExchangePosition>> GetPositionsAsync(
        string? instrumentSymbol = null,
        CancellationToken ct = default)
        => GetActiveClient().GetPositionsAsync(instrumentSymbol, ct);

    public Task SetLeverageAsync(string instrumentSymbol, int leverage, CancellationToken ct = default)
        => GetActiveClient().SetLeverageAsync(instrumentSymbol, leverage, ct);

    public Task SetMarginTypeAsync(string instrumentSymbol, string marginType, CancellationToken ct = default)
        => GetActiveClient().SetMarginTypeAsync(instrumentSymbol, marginType, ct);

    public Task<List<ExchangeTradeDetail>> GetUserTradesAsync(string instrumentSymbol, CancellationToken ct = default)
        => GetActiveClient().GetUserTradesAsync(instrumentSymbol, ct);

    private IExchangeClient GetActiveClient()
    {
        if (_clients.TryGetValue(_environmentService.CurrentMode, out var client))
            return client;

        throw new InvalidOperationException(
            $"交易模式 {_environmentService.CurrentMode} 未配置对应的交易所客户端");
    }
}
