using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Services.Dialog;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.Home;

/// <summary>
/// A股首页服务实现
/// </summary>
public class AShareHomeService : HomeAssetServiceBase
{
    public AShareHomeService(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILogger<AShareHomeService> logger)
        : base(serviceProvider, dialogService, logger)
    {
    }

    protected override bool TryMapFavoriteAsset(
        object assetParameter,
        out FavoriteAssetRequest favoriteRequest,
        out string? errorMessage)
    {
        errorMessage = null;
        if (assetParameter is HotAsset hotAsset)
        {
            favoriteRequest = new FavoriteAssetRequest(hotAsset.Name, hotAsset.Code, hotAsset.Market);
            return true;
        }

        if (assetParameter is AssetItem assetItem)
        {
            var assetName = assetItem.Name;
            var code = assetItem.Code;
            var market = string.Empty;
            if (code.StartsWith("sh") || code.StartsWith("sz"))
            {
                market = code.Substring(0, 2).ToUpper();
                code = code.Substring(2);
            }

            favoriteRequest = new FavoriteAssetRequest(assetName, code, market);
            return true;
        }

        favoriteRequest = new FavoriteAssetRequest(string.Empty, string.Empty, string.Empty);
        return false;
    }
}

