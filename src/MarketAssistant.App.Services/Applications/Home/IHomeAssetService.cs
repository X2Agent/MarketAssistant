using MarketAssistant.Applications.Assets.Models;

namespace MarketAssistant.Applications.Home;

/// <summary>
/// 首页资产服务接口
/// </summary>
public interface IHomeAssetService
{
    /// <summary>
    /// 搜索资产
    /// </summary>
    Task<List<AssetItem>> SearchAssetAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取热门资产
    /// </summary>
    Task<List<HotAsset>> GetHotAssetsAsync();

    /// <summary>
    /// 获取最近查看的资产
    /// </summary>
    Task<List<AssetItem>> GetRecentAssetsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加到最近查看
    /// </summary>
    Task AddToRecentAssetsAsync(AssetItem asset, CancellationToken cancellationToken = default);

    /// <summary>
    /// 添加到收藏
    /// </summary>
    Task<bool> AddToFavoriteAsync(object assetParameter);
}






