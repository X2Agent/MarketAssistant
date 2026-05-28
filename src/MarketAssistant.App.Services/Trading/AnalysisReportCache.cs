using System.Collections.Concurrent;
using MarketAssistant.Agents.MarketAnalysis.Models;

namespace MarketAssistant.Trading;

/// <summary>
/// 分析报告内存缓存：线程安全地存储最近一次市场分析结果，
/// 供交易模块在 AI 信号策略决策时读取，打通分析-交易链路。
/// </summary>
public sealed class AnalysisReportCache
{
    private readonly ConcurrentDictionary<string, CachedReport> _reports =
        new(StringComparer.OrdinalIgnoreCase);

    public void Set(string symbol, MarketAnalysisReport report) =>
        _reports[symbol] = new CachedReport(report, DateTime.UtcNow);

    public CachedReport? Get(string symbol) =>
        _reports.TryGetValue(symbol, out var cached) ? cached : null;

    public sealed record CachedReport(MarketAnalysisReport Report, DateTime CachedAt);
}
