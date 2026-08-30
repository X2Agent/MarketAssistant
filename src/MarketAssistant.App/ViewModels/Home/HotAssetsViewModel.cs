using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Home;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels.Home;

public partial class HotAssetsViewModel : ViewModelBase, IDisposable
{
    private readonly MarketContext _marketContext;

    private IHomeAssetService HomeAssetService =>
        _marketContext.GetService<IHomeAssetService>();

    public ObservableCollection<HotAsset> HotAssets { get; } = new();

    public event EventHandler<HotAsset>? HotAssetSelected;

    public HotAssetsViewModel(
        MarketContext marketContext,
        ILogger<HotAssetsViewModel> logger)
        : base(logger)
    {
        _marketContext = marketContext;

        SubscribeToMarketChanges(_marketContext);

        _ = LoadHotAssetsAsync();
    }

    /// <summary>
    /// 市场切换时重新加载热门资产
    /// </summary>
    protected override void OnMarketChanged(MarketType newMarket)
    {
        _ = LoadHotAssetsAsync();
    }

    [RelayCommand]
    private async Task LoadHotAssetsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var hotAssets = await HomeAssetService.GetHotAssetsAsync();

            HotAssets.Clear();
            foreach (var asset in hotAssets)
            {
                HotAssets.Add(asset);
            }
        }, "加载热门资产");
    }

    [RelayCommand]
    private void SelectHotAsset(HotAsset? asset)
    {
        if (asset == null) return;

        HotAssetSelected?.Invoke(this, asset);
    }

    [RelayCommand]
    private async Task AddToFavoriteAsync(HotAsset? asset)
    {
        if (asset == null) return;

        await SafeExecuteAsync(async () =>
        {
            await HomeAssetService.AddToFavoriteAsync(asset);
        }, "添加收藏");
    }

    public void Dispose()
    {
        UnsubscribeFromMarketChanges(_marketContext);
        GC.SuppressFinalize(this);
    }
}






