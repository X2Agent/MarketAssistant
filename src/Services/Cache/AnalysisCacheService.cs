using MarketAssistant.Agents.MarketAnalysis.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Services.Cache;

/// <summary>
/// 分析结果缓存服务（彻底重构版）
/// 缓存完整的 MarketAnalysisReport，更符合业务逻辑
/// </summary>
public class AnalysisCacheService : IAnalysisCacheService
{
    private readonly ILogger<AnalysisCacheService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromHours(2);

    public AnalysisCacheService(ILogger<AnalysisCacheService> logger, IMemoryCache memoryCache)
    {
        _logger = logger;
        _memoryCache = memoryCache;
    }

    /// <summary>
    /// 获取缓存的市场分析报告
    /// </summary>
    public Task<MarketAnalysisReport?> GetCachedAnalysisAsync(string stockSymbol)
    {
        if (string.IsNullOrWhiteSpace(stockSymbol))
        {
            throw new ArgumentNullException(nameof(stockSymbol));
        }

        var cacheKey = GenerateCacheKey(stockSymbol);

        if (_memoryCache.TryGetValue(cacheKey, out MarketAnalysisReport? cachedReport))
        {
            _logger.LogInformation("从缓存获取分析报告: {StockSymbol}, 分析师数量: {Count}",
                stockSymbol, cachedReport?.AnalystMessages.Count ?? 0);
            return Task.FromResult(cachedReport);
        }

        _logger.LogInformation("缓存未命中: {StockSymbol}", stockSymbol);
        return Task.FromResult<MarketAnalysisReport?>(null);
    }

    /// <summary>
    /// 缓存市场分析报告
    /// </summary>
    public Task CacheAnalysisAsync(string stockSymbol, MarketAnalysisReport report)
    {
        if (string.IsNullOrWhiteSpace(stockSymbol))
        {
            throw new ArgumentNullException(nameof(stockSymbol));
        }

        ArgumentNullException.ThrowIfNull(report);

        var cacheKey = GenerateCacheKey(stockSymbol);

        _memoryCache.Set(cacheKey, report, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheExpiration,
            SlidingExpiration = TimeSpan.FromMinutes(30),
            Priority = CacheItemPriority.Normal
        });

        _logger.LogInformation(
            "已缓存分析报告: {StockSymbol}, 分析师数量: {Count}, 过期时间: {Expiration}",
            stockSymbol,
            report.AnalystMessages.Count,
            _cacheExpiration);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 清除指定股票的缓存
    /// </summary>
    public Task ClearCacheAsync(string stockSymbol)
    {
        if (string.IsNullOrWhiteSpace(stockSymbol))
        {
            throw new ArgumentNullException(nameof(stockSymbol));
        }

        var cacheKey = GenerateCacheKey(stockSymbol);
        _memoryCache.Remove(cacheKey);
        _logger.LogInformation("已清除缓存: {StockSymbol}", stockSymbol);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 生成缓存键
    /// </summary>
    private string GenerateCacheKey(string stockSymbol)
    {
        return $"MarketAnalysisReport_{stockSymbol}";
    }

    public void Dispose()
    {
        // IMemoryCache 由 DI 容器管理，无需手动释放
    }
}
