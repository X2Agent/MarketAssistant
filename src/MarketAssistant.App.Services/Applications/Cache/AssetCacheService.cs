using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// 通用资产缓存服务，通过 <see cref="ServiceKeyAttribute"/> 从 Keyed DI 注册键自动获取市场类型，
/// 内部计算缓存前缀。合并了原 AShareAssetCacheService / CryptoAssetCacheService 两个重复实现。
/// </summary>
public sealed class AssetCacheService : IAssetCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<AssetCacheService> _logger;
    private readonly string _cacheKeyPrefix;
    private const int CacheExpirationMinutes = 5;

    /// <summary>
    /// 跟踪当前实例已缓存的键，用于按前缀清除，避免依赖 MemoryCache 内部反射。
    /// </summary>
    private readonly ConcurrentDictionary<string, byte> _trackedKeys = new();

    public AssetCacheService(
        [ServiceKey] MarketType marketType,
        IMemoryCache cache,
        ILogger<AssetCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
        _cacheKeyPrefix = CacheKeys.GetAssetInfoPrefix(marketType);
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
            Size = 1
        };

        _cache.Set(cacheKey, info, cacheOptions);
        _trackedKeys[cacheKey] = 0;
        _logger.LogDebug("缓存资产信息: {Code}", code);
    }

    public void Clear()
    {
        foreach (var key in _trackedKeys.Keys)
        {
            _cache.Remove(key);
        }
        _trackedKeys.Clear();
        _logger.LogInformation("清除资产缓存: {Prefix}", _cacheKeyPrefix);
    }

    private string GetCacheKey(string code) => $"{_cacheKeyPrefix}{code}";
}
