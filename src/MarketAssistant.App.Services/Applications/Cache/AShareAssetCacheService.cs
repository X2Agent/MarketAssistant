using MarketAssistant.Applications.Assets.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// A股资产缓存服务实现
/// </summary>
public class AShareAssetCacheService : IAssetCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<AShareAssetCacheService> _logger;
    private const int CacheExpirationMinutes = 5; // 缓存5分钟

    public AShareAssetCacheService(IMemoryCache cache, ILogger<AShareAssetCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<AssetInfo?> GetCachedAssetInfoAsync(string code)
    {
        var cacheKey = GetCacheKey(code);
        if (_cache.TryGetValue(cacheKey, out AssetInfo? assetInfo))
        {
            _logger.LogDebug("从缓存获取资产信息: {Code}", code);
            return Task.FromResult<AssetInfo?>(assetInfo);
        }
        return Task.FromResult<AssetInfo?>(null);
    }

    public void CacheAssetInfo(string code, AssetInfo info)
    {
        var cacheKey = GetCacheKey(code);
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CacheExpirationMinutes),
            Size = 1 // 用于缓存大小限制
        };
        
        _cache.Set(cacheKey, info, cacheOptions);
        _logger.LogDebug("缓存资产信息: {Code}", code);
    }

    public void Clear()
    {
        // MemoryCache 不支持清除所有条目
        // 这里只是记录日志
        _logger.LogInformation("清除A股资产缓存");
    }

    private static string GetCacheKey(string code)
    {
        return $"AssetInfo_AShare_{code}";
    }
}






