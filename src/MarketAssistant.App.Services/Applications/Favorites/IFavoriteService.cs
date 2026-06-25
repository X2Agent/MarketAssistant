using MarketAssistant.Applications.Assets.Models;

namespace MarketAssistant.Applications.Favorites;

/// <summary>
/// 收藏服务接口
/// </summary>
public interface IFavoriteService
{
    /// <summary>
    /// 添加资产到收藏
    /// </summary>
    Task AddFavoriteAsync(string code, string market, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从收藏中移除资产
    /// </summary>
    Task RemoveFavoriteAsync(string code, string market, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查资产是否已收藏
    /// </summary>
    Task<bool> IsFavoriteAsync(string code, string market, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有收藏的资产代码
    /// </summary>
    Task<List<FavoriteAsset>> GetFavoritesCodesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有收藏的资产（包含最新数据）
    /// </summary>
    Task<List<AssetInfo>> GetFavoritesWithLatestDataAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 清空所有收藏
    /// </summary>
    Task ClearFavoritesAsync(CancellationToken cancellationToken = default);
}
