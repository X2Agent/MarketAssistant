using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;

namespace MarketAssistant.Services.Trading.Exchanges;

/// <summary>
/// 根据当前交易模式在实盘/模拟盘/Demo/合约客户端之间路由。
/// 通过字典查找活跃客户端，新增模式只需在字典中注册，无需修改路由逻辑。
/// </summary>
/// <remarks>
/// 本类同时是交易所调用的统一埋点：所有 IExchangeClient 调用都经过这里，
/// 因此 API 健康度（失败计数）在此记录，风控据此做 API 异常熔断。
/// </remarks>
public sealed class RoutingExchangeClient(
    TradingEnvironmentService environmentService,
    IReadOnlyDictionary<TradingMode, IExchangeClient> clients,
    ApiHealthMonitor apiHealthMonitor) : IExchangeClient
{
    private readonly TradingEnvironmentService _environmentService = environmentService;
    private readonly IReadOnlyDictionary<TradingMode, IExchangeClient> _clients = clients;
    private readonly ApiHealthMonitor _apiHealthMonitor = apiHealthMonitor;

    public string ExchangeName => GetActiveClient().ExchangeName;

    /// <summary>
    /// 当前是否为合约模式，由底层活跃客户端决定。
    /// </summary>
    public bool IsFutures => GetActiveClient().IsFutures;

    public Task<ExchangeAccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
        => TrackAsync(client => client.GetAccountInfoAsync(ct));

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
        => TrackAsync(client => client.PlaceOrderAsync(
            instrumentSymbol, side, type, quantity, price, clientOrderId, reduceOnly,
            positionSide, stopPrice, trailingDelta, ct));

    public Task<ExchangeOrderResult> GetOrderAsync(
        string instrumentSymbol,
        string orderId,
        CancellationToken ct = default)
        => TrackAsync(client => client.GetOrderAsync(instrumentSymbol, orderId, ct));

    public Task<ExchangeOrderResult> CancelOrderAsync(
        string instrumentSymbol,
        string orderId,
        CancellationToken ct = default)
        => TrackAsync(client => client.CancelOrderAsync(instrumentSymbol, orderId, ct));

    public Task<List<ExchangeOrderResult>> GetOpenOrdersAsync(
        string? instrumentSymbol = null,
        CancellationToken ct = default)
        => TrackAsync(client => client.GetOpenOrdersAsync(instrumentSymbol, ct));

    public Task<List<ExchangePosition>> GetPositionsAsync(
        string? instrumentSymbol = null,
        CancellationToken ct = default)
        => TrackAsync(client => client.GetPositionsAsync(instrumentSymbol, ct));

    public Task SetLeverageAsync(string instrumentSymbol, int leverage, CancellationToken ct = default)
        => TrackAsync(client => TrackVoidAsync(client.SetLeverageAsync(instrumentSymbol, leverage, ct)));

    /// <summary>把无返回值的调用包装为可埋点的 Task&lt;T&gt;。</summary>
    private static async Task<byte> TrackVoidAsync(Task task)
    {
        await task.ConfigureAwait(false);
        return 0;
    }

    /// <summary>
    /// 获取当前活跃客户端的快照。下单链路（风控→杠杆→下单）必须对同一客户端实例完成，
    /// 若各步骤独立重新解析，用户在等待确认期间切换模拟盘/实盘会让订单落到错误环境。
    /// </summary>
    public IExchangeClient GetActiveClientSnapshot() => GetActiveClient();

    /// <summary>
    /// 记录一次交易所调用的成败。用户主动取消（OperationCanceledException）不算 API 故障；
    /// 业务错误（余额不足、参数非法）同样是"API 正常工作"的证据，
    /// 只有传输层故障（网络/超时/5xx）计入失败——把业务拒绝也算故障会让风控误熔断。
    /// </summary>
    private async Task<T> TrackAsync<T>(Func<IExchangeClient, Task<T>> call)
    {
        try
        {
            var result = await call(GetActiveClient()).ConfigureAwait(false);
            _apiHealthMonitor.RecordSuccess();
            return result;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsTransportFailure(ex))
        {
            _apiHealthMonitor.RecordFailure();
            throw;
        }
        catch (Exception)
        {
            _apiHealthMonitor.RecordSuccess();
            throw;
        }
    }

    /// <summary>
    /// 判定异常是否为传输层故障。交易所客户端会把 HttpRequestException 包装成
    /// FriendlyException，必须沿 InnerException 链递归判定。
    /// </summary>
    private static bool IsTransportFailure(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is System.Net.Http.HttpRequestException or TimeoutException or TaskCanceledException)
                return true;
        }

        return false;
    }

    private IExchangeClient GetActiveClient()
    {
        if (_clients.TryGetValue(_environmentService.CurrentMode, out var client))
            return client;

        throw new InvalidOperationException(
            $"交易模式 {_environmentService.CurrentMode} 未配置对应的交易所客户端");
    }
}
