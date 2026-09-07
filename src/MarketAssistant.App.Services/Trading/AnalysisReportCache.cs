using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Caching.Memory;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 分析报告内存缓存：线程安全地存储最近一次市场分析结果，
/// 供交易模块在 AI 信号策略决策时读取，打通分析-交易链路。
/// 缓存键包含市场类型前缀，避免 A 股与虚拟币同代码（理论上）相互覆盖。
/// 内部实现由手搓 ConcurrentDictionary+LRU 收敛为 <see cref="IMemoryCache"/>：
/// 过期语义沿用原实现（绝对 TTL 24 小时，写入后固定时长失效）；
/// 容量上限（原 50 条）随 LRU 一并移除——报告按 symbol 存储、条目数天然有限，无需淘汰。
/// </summary>
public sealed class AnalysisReportCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly IMemoryCache _cache;
    private readonly MarketContext _marketContext;

    public AnalysisReportCache(MarketContext marketContext, IMemoryCache? cache = null)
    {
        _marketContext = marketContext;
        // 测试/手工构造可缺省；DI 注入应用级共享缓存实例
        _cache = cache ?? new MemoryCache(new MemoryCacheOptions());
    }

    /// <summary>
    /// 写入缓存（显式市场类型）。后台交易链路应显式传入市场，
    /// 避免依赖随时可能被 UI 切换的全局 MarketContext 状态。
    /// </summary>
    public void Set(string symbol, MarketType market, MarketAnalysisReport report)
    {
        _cache.Set(
            BuildKey(market, symbol),
            new CachedReport(report, DateTime.UtcNow),
            new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = Ttl });
    }

    /// <summary>写入缓存（沿用全局 MarketContext 当前市场，供市场分析工作流使用）。</summary>
    public void Set(string symbol, MarketAnalysisReport report)
        => Set(symbol, _marketContext.CurrentMarket, report);

    /// <summary>
    /// 读取缓存（显式市场类型）。过期条目由 IMemoryCache 自动移除。
    /// </summary>
    public CachedReport? Get(string symbol, MarketType market)
        => _cache.TryGetValue<CachedReport>(BuildKey(market, symbol), out var cached) ? cached : null;

    /// <summary>读取缓存（沿用全局 MarketContext 当前市场）。</summary>
    public CachedReport? Get(string symbol)
        => Get(symbol, _marketContext.CurrentMarket);

    /// <summary>
    /// 构建包含市场类型的缓存键，避免跨市场冲突。
    /// 键格式由 <see cref="CacheKeys.GetTradingAnalysisReportKey"/> 统一管理。
    /// </summary>
    private static string BuildKey(MarketType market, string symbol)
        => CacheKeys.GetTradingAnalysisReportKey(market, symbol);

    public sealed record CachedReport(MarketAnalysisReport Report, DateTime CachedAt);
}
