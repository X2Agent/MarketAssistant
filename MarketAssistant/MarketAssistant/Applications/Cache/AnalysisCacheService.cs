using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// 分析结果缓存服务
/// </summary>
public class AnalysisCacheService : IAnalysisCacheService
{
    private readonly ILogger<AnalysisCacheService> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly TimeSpan _cacheExpiration;

    public AnalysisCacheService(ILogger<AnalysisCacheService> logger, IMemoryCache memoryCache, TimeSpan? cacheExpiration = null)
    {
        _logger = logger;
        _memoryCache = memoryCache;
        _cacheExpiration = cacheExpiration ?? TimeSpan.FromHours(2);
    }

    /// <summary>
    /// 获取缓存的分析结果
    /// </summary>
    public Task<ChatHistory?> GetCachedAnalysisAsync(string stockSymbol)
    {
        var cacheKey = GenerateCacheKey(stockSymbol);
        
        if (_memoryCache.TryGetValue(cacheKey, out ChatHistory? cachedHistory))
        {
            _logger.LogInformation("从缓存获取分析结果: {StockSymbol}", stockSymbol);
            return Task.FromResult<ChatHistory?>(cachedHistory);
        }

        return Task.FromResult<ChatHistory?>(null);
    }

    /// <summary>
    /// 缓存分析结果
    /// </summary>
    public Task CacheAnalysisAsync(string stockSymbol, ChatHistory chatHistory)
    {
        var cacheKey = GenerateCacheKey(stockSymbol);
        
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheExpiration,
            Priority = CacheItemPriority.Normal,
            Size = EstimateChatHistorySize(chatHistory)
        };

        _memoryCache.Set(cacheKey, chatHistory, cacheOptions);

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
    /// 估算 ChatHistory 的内存大小
    /// </summary>
    private long EstimateChatHistorySize(ChatHistory chatHistory)
    {
        long size = 0;
        foreach (var message in chatHistory)
        {
            size += (message.Content?.Length ?? 0) * 2; // 每个字符大约2字节（UTF-16）
            size += (message.AuthorName?.Length ?? 0) * 2;
            size += 32; // 其他属性的大致开销
        }
        return Math.Max(size, 1); // 至少1字节，避免0大小
    }

    #endregion
}

