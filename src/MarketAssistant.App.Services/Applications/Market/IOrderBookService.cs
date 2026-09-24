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
/// 不支持盘口的市场（A 股无公开逐笔委托/五档数据源）使用空实现。
/// </summary>
public sealed class NoopOrderBookService : IOrderBookService
{
    public Task<OrderBookSnapshot?> GetOrderBookAsync(
        string code, int depth = 5, CancellationToken cancellationToken = default) =>
        Task.FromResult<OrderBookSnapshot?>(null);
}

/// <summary>
/// 不支持逐笔成交的市场使用空实现。
/// </summary>
public sealed class NoopTickTradeService : ITickTradeService
{
    public Task<IReadOnlyList<TickTrade>> GetRecentTradesAsync(
        string code, int limit = 20, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TickTrade>>([]);
}
