using MarketAssistant.Applications.Market.Models;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// 盘口深度服务（按市场 Keyed 注册）。不支持盘口的市场注册空实现并返回 null。
/// </summary>
public interface IOrderBookService
{
    Task<OrderBookSnapshot?> GetOrderBookAsync(
        string code,
        int depth = 5,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 逐笔成交服务（按市场 Keyed 注册）。不支持逐笔的市场注册空实现并返回空列表。
/// 返回结果按成交时间倒序（最新在前）。
/// </summary>
public interface ITickTradeService
{
    Task<IReadOnlyList<TickTrade>> GetRecentTradesAsync(
        string code,
        int limit = 20,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 不支持盘口的市场使用空实现。
/// </summary>
/// <remarks>
/// 当前 A 股（<c>AShareOrderBookService</c>，新浪个股行情接口）与虚拟币
/// （<c>CryptoOrderBookService</c>，币安深度接口）均已注册真实实现，
/// 本类仅作为新增市场缺少盘口数据源时的兜底契约位。
/// </remarks>
public sealed class NoopOrderBookService : IOrderBookService
{
    public Task<OrderBookSnapshot?> GetOrderBookAsync(
        string code, int depth = 5, CancellationToken cancellationToken = default) =>
        Task.FromResult<OrderBookSnapshot?>(null);
}

/// <summary>
/// 不支持逐笔成交的市场使用空实现。
/// </summary>
/// <remarks>
/// 当前 A 股（新浪个股行情接口不返回逐笔明细）与虚拟币
/// （<c>CryptoTickTradeService</c>，币安成交流）分别注册空实现与真实实现，
/// 本类仅作为新增市场缺少逐笔数据源时的兜底契约位。
/// </remarks>
public sealed class NoopTickTradeService : ITickTradeService
{
    public Task<IReadOnlyList<TickTrade>> GetRecentTradesAsync(
        string code, int limit = 20, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TickTrade>>([]);
}
