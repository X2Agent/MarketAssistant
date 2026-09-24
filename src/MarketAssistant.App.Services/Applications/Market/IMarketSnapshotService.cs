using MarketAssistant.Applications.Market.Models;

namespace MarketAssistant.Applications.Market;

/// <summary>
/// 行情速览服务（按市场 Keyed 注册）。返回 null 表示数据源明确无该标的。
/// 数据源不可用时必须抛出异常，由调用方映射为「失败」而非「无数据」。
/// </summary>
public interface IMarketSnapshotService
{
    Task<MarketSnapshot?> GetSnapshotAsync(string code, CancellationToken cancellationToken = default);
}
