using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.ViewModels.Home;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

public partial class HomePageViewModel : ViewModelBase, IDisposable
{
    public HomeSearchViewModel Search { get; }

    public HotAssetsViewModel HotAssets { get; }

    public RecentAssetsViewModel RecentAssets { get; }

    public TelegraphNewsViewModel News { get; }

    public HomePageViewModel(
        HomeSearchViewModel searchViewModel,
        HotAssetsViewModel hotAssetsViewModel,
        RecentAssetsViewModel recentAssetsViewModel,
        TelegraphNewsViewModel newsViewModel,
        ILogger<HomePageViewModel> logger) : base(logger)
    {
        Search = searchViewModel;
        HotAssets = hotAssetsViewModel;
        RecentAssets = recentAssetsViewModel;
        News = newsViewModel;

        Search.AssetSelected += OnAssetSelected;
        HotAssets.HotAssetSelected += OnHotAssetSelected;
        RecentAssets.RecentAssetSelected += OnRecentAssetSelected;
    }

    private void OnAssetSelected(object? sender, AssetItem asset)
    {
        NavigateToAsset(asset);
    }

    private void OnHotAssetSelected(object? sender, HotAsset asset)
    {
        var assetCode = asset.MarketType == Infrastructure.Core.MarketType.Crypto
            ? asset.Code.ToLower()
            : $"{asset.Market}{asset.Code}".ToLower();

        decimal? currentPrice = decimal.TryParse(asset.CurrentPrice, out var price) ? price : null;
        decimal? changePercent = decimal.TryParse(asset.ChangePercentage?.TrimEnd('%'), out var percent) ? percent : null;

        // 传递完整的基本信息，避免详情页等待
        var parameter = new AssetNavigationParameter(
            assetCode,
            asset.Name,
            currentPrice,
            changePercent
        );

        WeakReferenceMessenger.Default.Send(new NavigationMessage("Asset", parameter));
        Logger?.LogInformation($"导航到资产详情页: {assetCode}");

        var assetItem = new AssetItem { Name = asset.Name, Code = assetCode };
        _ = RecentAssets.AddToRecentAssetsAsync(assetItem);
    }

    private void OnRecentAssetSelected(object? sender, AssetItem asset)
    {
        NavigateToAsset(asset);
    }

    private void NavigateToAsset(AssetItem assetItem, decimal? currentPrice = null, decimal? changePercent = null)
    {
        WeakReferenceMessenger.Default.Send(
            new NavigationMessage("Asset", new AssetNavigationParameter(
                assetItem.Code,
                assetItem.Name,
                currentPrice,
                changePercent)));

        Logger?.LogInformation($"导航到资产详情页: {assetItem.Code}");

        _ = RecentAssets.AddToRecentAssetsAsync(assetItem);
    }

    public void Dispose()
    {
        Search.AssetSelected -= OnAssetSelected;
        HotAssets.HotAssetSelected -= OnHotAssetSelected;
        RecentAssets.RecentAssetSelected -= OnRecentAssetSelected;

        Search.Dispose();
        HotAssets.Dispose();
        RecentAssets.Dispose();
        News.Dispose();

        GC.SuppressFinalize(this);
    }
}