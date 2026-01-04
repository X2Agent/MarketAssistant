using MarketAssistant.Applications.Assets.Models;

namespace MarketAssistant.Applications.Cache;

/// <summary>
/// 资产缓存服务接口
/// </summary>
public interface IAssetCacheService
{
    /// <summary>
    /// 获取缓存的资产信息
    /// </summary>
    Task<AssetInfo?> GetCachedAssetInfoAsync(string code);

    /// <summary>
    /// 缓存资产信息
    /// </summary>
    void CacheAssetInfo(string code, AssetInfo info);

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    void Clear();
}






