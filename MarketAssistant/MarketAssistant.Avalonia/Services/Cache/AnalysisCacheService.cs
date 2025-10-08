using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MarketAssistant.Avalonia.Views.Models;

namespace MarketAssistant.Services.Cache;

/// <summary>
/// 分析结果缓存服务
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
    /// 获取缓存的分析结果
    /// </summary>
    public Task<AnalystResult?> GetCachedAnalysisAsync(string stockSymbol)
    {
        var cacheKey = GenerateCacheKey(stockSymbol);

        if (_memoryCache.TryGetValue(cacheKey, out AnalystResult? cachedResult))
        {
            _logger.LogInformation("从缓存获取分析结果: {StockSymbol}", stockSymbol);
            return Task.FromResult<AnalystResult?>(cachedResult);
        }

        return Task.FromResult<AnalystResult?>(null);
    }

    /// <summary>
    /// 缓存分析结果
    /// </summary>
    public Task CacheAnalysisAsync(string stockSymbol, AnalystResult analysisResult)
    {
        var cacheKey = GenerateCacheKey(stockSymbol);

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheExpiration,
            Priority = CacheItemPriority.Normal,
            Size = EstimateAnalysisResultSize(analysisResult)
        };

        _memoryCache.Set(cacheKey, analysisResult, cacheOptions);

        _logger.LogInformation("缓存分析结果: {StockSymbol}, 过期时间: {ExpiresAt}",
            stockSymbol, DateTime.UtcNow.Add(_cacheExpiration));

        return Task.CompletedTask;
    }

    /// <summary>
    /// 清除指定股票的缓存
    /// </summary>
    public Task ClearCacheAsync(string stockSymbol)
    {
        var cacheKey = GenerateCacheKey(stockSymbol);
        _memoryCache.Remove(cacheKey);

        _logger.LogInformation("清除缓存: {StockSymbol}", stockSymbol);

        return Task.CompletedTask;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        // IMemoryCache 由DI容器管理，不需要手动释放
    }

    #region 私有方法

    /// <summary>
    /// 生成缓存键
    /// </summary>
    private string GenerateCacheKey(string stockSymbol)
    {
        return $"{stockSymbol.ToUpperInvariant()}_{DateTime.UtcNow:yyyyMMdd}";
    }

    /// <summary>
    /// 估算 AnalystResult 的内存大小
    /// </summary>
    private long EstimateAnalysisResultSize(AnalystResult analysisResult)
    {
        long size = 0;
        
        // 基本字符串属性
        size += (analysisResult.StockSymbol?.Length ?? 0) * 2;
        size += (analysisResult.TargetPrice?.Length ?? 0) * 2;
        size += (analysisResult.PriceChange?.Length ?? 0) * 2;
        size += (analysisResult.Rating?.Length ?? 0) * 2;
        size += (analysisResult.InvestmentRating?.Length ?? 0) * 2;
        size += (analysisResult.RiskLevel?.Length ?? 0) * 2;
        size += (analysisResult.ConsensusInfo?.Length ?? 0) * 2;
        size += (analysisResult.DisagreementInfo?.Length ?? 0) * 2;
        
        // 数值类型
        size += sizeof(float) * 2; // OverallScore + ConfidencePercentage
        
        // 字典和列表
        size += analysisResult.DimensionScores.Count * 32; // 估算键值对大小
        size += analysisResult.InvestmentHighlights.Sum(x => x?.Length ?? 0) * 2;
        size += analysisResult.RiskFactors.Sum(x => x?.Length ?? 0) * 2;
        size += analysisResult.OperationSuggestions.Sum(x => x?.Length ?? 0) * 2;
        
        // 分析数据项
        foreach (var item in analysisResult.AnalysisData)
        {
            size += (item.DataType?.Length ?? 0) * 2;
            size += (item.Name?.Length ?? 0) * 2;
            size += (item.Value?.Length ?? 0) * 2;
            size += (item.Unit?.Length ?? 0) * 2;
            size += (item.Signal?.Length ?? 0) * 2;
            size += (item.Impact?.Length ?? 0) * 2;
            size += (item.Strategy?.Length ?? 0) * 2;
            size += 64; // 对象开销
        }
        
        return Math.Max(size, 1); // 至少1字节，避免0大小
    }

    #endregion
}

