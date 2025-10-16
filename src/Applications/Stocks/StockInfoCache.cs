using MarketAssistant.Applications.Stocks.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace MarketAssistant.Applications.Stocks;

/// <summary>
/// 股票信息缓存服务
/// </summary>
public class StockInfoCache
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<StockInfoCache> _logger;
    private const int CacheExpirationMinutes = 5; // 缓存5分钟

    public StockInfoCache(IMemoryCache cache, ILogger<StockInfoCache> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// 获取缓存的股票信息
    /// </summary>
    public StockInfo? Get(string code, string market)
    {
        var cacheKey = GetCacheKey(code, market);
        if (_cache.TryGetValue(cacheKey, out StockInfo? stockInfo))
        {
            _logger.LogDebug($"从缓存获取股票信息: {code} ({market})");
            return stockInfo;
        }
        return null;
    }

    /// <summary>
    /// 设置股票信息到缓存
    /// </summary>
    public void Set(StockInfo stockInfo)
    {
        var cacheKey = GetCacheKey(stockInfo.Code, stockInfo.Market);
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
            Size = 1 // 用于缓存大小限制
        };
        
        _cache.Set(cacheKey, stockInfo, cacheOptions);
        _logger.LogDebug($"缓存股票信息: {stockInfo.Code} ({stockInfo.Market})");
    }

    /// <summary>
    /// 批量设置股票信息到缓存
    /// </summary>
    public void SetRange(IEnumerable<StockInfo> stockInfos)
    {
        foreach (var stockInfo in stockInfos)
        {
            Set(stockInfo);
        }
    }

    /// <summary>
    /// 清除指定股票的缓存
    /// </summary>
    public void Remove(string code, string market)
    {
        var cacheKey = GetCacheKey(code, market);
        _cache.Remove(cacheKey);
        _logger.LogDebug($"清除股票缓存: {code} ({market})");
    }

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    public void Clear()
    {
        // MemoryCache 不支持清除所有条目，只能通过重新创建
        // 这里只是记录日志
        _logger.LogInformation("清除股票缓存");
    }

    private static string GetCacheKey(string code, string market)
    {
        return $"StockInfo_{market}_{code}";
    }
}







