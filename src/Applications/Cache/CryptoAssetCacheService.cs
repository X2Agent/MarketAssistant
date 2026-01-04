using MarketAssistant.Applications.Assets.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// 虚拟币资产缓存服务实现
/// </summary>
public class CryptoAssetCacheService : IAssetCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<CryptoAssetCacheService> _logger;
    private const int CacheExpirationMinutes = 5; // 缓存5分钟

    public CryptoAssetCacheService(IMemoryCache cache, ILogger<CryptoAssetCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public Task<AssetInfo?> GetCachedAssetInfoAsync(string code)
    {
        var cacheKey = GetCacheKey(code);
        if (_cache.TryGetValue(cacheKey, out AssetInfo? assetInfo))
        {
            _logger.LogDebug("从缓存获取虚拟币资产信息: {Code}", code);
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
            Size = 1
        };
        
        _cache.Set(cacheKey, info, cacheOptions);
        _logger.LogDebug("缓存虚拟币资产信息: {Code}", code);
    }

    public void Clear()
    {
        _logger.LogInformation("清除虚拟币资产缓存");
    }

    private static string GetCacheKey(string code)
    {
        return $"AssetInfo_Crypto_{code}";
    }
}






