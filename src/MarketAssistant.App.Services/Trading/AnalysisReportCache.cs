using System.Collections.Concurrent;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;

namespace MarketAssistant.Services.Trading;

/// <summary>
/// 分析报告内存缓存：线程安全地存储最近一次市场分析结果，
/// 供交易模块在 AI 信号策略决策时读取，打通分析-交易链路。
/// 缓存键包含市场类型前缀，避免 A 股与虚拟币同代码（理论上）相互覆盖。
/// </summary>
public sealed class AnalysisReportCache
{
    private const int MaxEntries = 50;
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<string, CachedReport> _reports =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly MarketContext _marketContext;

    public AnalysisReportCache(MarketContext marketContext)
    {
        _marketContext = marketContext;
    }

    /// <summary>
    /// 写入缓存（显式市场类型）。后台交易链路应显式传入市场，
    /// 避免依赖随时可能被 UI 切换的全局 MarketContext 状态。
    /// </summary>
    public void Set(string symbol, MarketType market, MarketAnalysisReport report)
    {
        // 惰性清理：写入前移除已过期条目，并在超限时淘汰最旧的条目
        EvictExpired();
        EnsureCapacity();

        _reports[BuildKey(market, symbol)] = new CachedReport(report, DateTime.UtcNow);
    }

    /// <summary>写入缓存（沿用全局 MarketContext 当前市场，供市场分析工作流使用）。</summary>
    public void Set(string symbol, MarketAnalysisReport report)
        => Set(symbol, _marketContext.CurrentMarket, report);

    /// <summary>
    /// 读取缓存（显式市场类型）。
    /// </summary>
    public CachedReport? Get(string symbol, MarketType market)
    {
        var key = BuildKey(market, symbol);
        if (!_reports.TryGetValue(key, out var cached))
            return null;

        if (DateTime.UtcNow - cached.CachedAt > Ttl)
        {
            _reports.TryRemove(key, out _);
            return null;
        }

        return cached;
    }

    /// <summary>读取缓存（沿用全局 MarketContext 当前市场）。</summary>
    public CachedReport? Get(string symbol)
        => Get(symbol, _marketContext.CurrentMarket);

    /// <summary>
    /// 构建包含市场类型的缓存键，避免跨市场冲突。
    /// 键格式由 <see cref="CacheKeys.GetTradingAnalysisReportKey"/> 统一管理。
    /// </summary>
    private static string BuildKey(MarketType market, string symbol)
        => CacheKeys.GetTradingAnalysisReportKey(market, symbol);

    /// <summary>
    /// 移除所有已过期条目
    /// </summary>
    private void EvictExpired()
    {
        var threshold = DateTime.UtcNow - Ttl;
        foreach (var kv in _reports)
        {
            if (kv.Value.CachedAt < threshold)
            {
                _reports.TryRemove(kv.Key, out _);
            }
        }
    }

    /// <summary>
    /// 容量超限时淘汰最旧的条目
    /// </summary>
    private void EnsureCapacity()
    {
        if (_reports.Count < MaxEntries) return;

        // 按 CachedAt 升序，淘汰最旧的若干条目，留出少量余量避免频繁触发
        var overflow = _reports.Count - MaxEntries + 1;
        var oldest = _reports
            .OrderBy(kv => kv.Value.CachedAt)
            .Take(overflow)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in oldest)
        {
            _reports.TryRemove(key, out _);
        }
    }

    public sealed record CachedReport(MarketAnalysisReport Report, DateTime CachedAt);
}
