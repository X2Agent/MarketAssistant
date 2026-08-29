using MarketAssistant.Applications.Assets.Models;

namespace MarketAssistant.Applications.Home;

public interface IHomeAssetService
{
    Task<List<AssetItem>> SearchAssetAsync(string query, CancellationToken cancellationToken = default);

    Task<List<HotAsset>> GetHotAssetsAsync();

    Task<List<AssetItem>> GetRecentAssetsAsync(CancellationToken cancellationToken = default);

    Task AddToRecentAssetsAsync(AssetItem asset, CancellationToken cancellationToken = default);

    Task<bool> AddToFavoriteAsync(object assetParameter);
}






