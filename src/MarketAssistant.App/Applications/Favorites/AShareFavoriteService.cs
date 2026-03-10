using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Infrastructure.Core;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Favorites;

/// <summary>
/// A股收藏服务实现
/// </summary>
public class AShareFavoriteService : FavoriteServiceBase
{
    protected override string PreferenceKey => "FavoriteAssets_AShare";

    public AShareFavoriteService(
        IServiceProvider serviceProvider,
        ILogger<AShareFavoriteService> logger)
        : base(serviceProvider, logger)
    {
    }

    protected override AssetInfo CreateFallbackAssetInfo(FavoriteAsset favorite)
    {
        var displayName = string.IsNullOrWhiteSpace(favorite.Market)
            ? favorite.Code
            : $"{favorite.Market}.{favorite.Code}";

        return new AssetInfo
        {
            Code = favorite.Code,
            Market = favorite.Market,
            Name = displayName,
            MarketType = MarketType.AShare
        };
    }
}

