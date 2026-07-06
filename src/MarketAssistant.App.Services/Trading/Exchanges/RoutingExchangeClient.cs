using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Trading.Exchanges;

/// <summary>
/// 根据当前交易模式在实盘/模拟盘/合约客户端之间路由。
/// 支持 4 种模式：现货实盘、现货 Testnet、合约实盘、合约 Testnet。
/// </summary>
public sealed class RoutingExchangeClient : IExchangeClient
{
    private readonly TradingEnvironmentService _environmentService;
    private readonly BinanceExchangeClient _spotLiveClient;
    private readonly BinanceExchangeClient _spotTestnetClient;
    private readonly BinanceFuturesExchangeClient _futuresLiveClient;
    private readonly BinanceFuturesExchangeClient _futuresTestnetClient;

    public RoutingExchangeClient(
        TradingEnvironmentService environmentService,
        BinanceExchangeClient spotLiveClient,
        BinanceExchangeClient spotTestnetClient,
        BinanceFuturesExchangeClient futuresLiveClient,
        BinanceFuturesExchangeClient futuresTestnetClient)
    {
        _environmentService = environmentService;
        _spotLiveClient = spotLiveClient;
        _spotTestnetClient = spotTestnetClient;
        _futuresLiveClient = futuresLiveClient;
        _futuresTestnetClient = futuresTestnetClient;
    }

    public string ExchangeName => GetActiveClient().ExchangeName;

    public Task<ExchangeAccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
        => GetActiveClient().GetAccountInfoAsync(ct);

    public Task<ExchangeOrderResult> PlaceOrderAsync(
        string instrumentSymbol,
        OrderSide side,
        OrderType type,
        decimal quantity,
        decimal? price = null,
        string? clientOrderId = null,
        CancellationToken ct = default)
        => GetActiveClient().PlaceOrderAsync(instrumentSymbol, side, type, quantity, price, clientOrderId, ct);

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

    private IExchangeClient GetActiveClient() => _environmentService.CurrentMode switch
    {
        CryptoTradingMode.BinanceTestnet => _spotTestnetClient,
        CryptoTradingMode.LiveFutures => _futuresLiveClient,
        CryptoTradingMode.BinanceFuturesTestnet => _futuresTestnetClient,
        _ => _spotLiveClient
    };
}
