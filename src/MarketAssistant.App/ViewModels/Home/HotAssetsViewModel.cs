using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.Home;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels.Home;

public partial class HotAssetsViewModel : ViewModelBase, IRecipient<AssetFavoritesChanged>, IDisposable
{
    private readonly MarketContext _marketContext;

    private IHomeAssetService HomeAssetService =>
        _marketContext.GetService<IHomeAssetService>();

    private IFavoriteService FavoriteService =>
        _marketContext.GetService<IFavoriteService>();

    public ObservableCollection<HotAssetItem> HotAssets { get; } = [];

    public event EventHandler<HotAsset>? HotAssetSelected;

    public HotAssetsViewModel(
        MarketContext marketContext,
        ILogger<HotAssetsViewModel> logger)
        : base(logger)
    {
        _marketContext = marketContext;

        EmptyMessage = "暂无热门资产数据";
        SetRetryCommand(() => LoadHotAssetsAsync());

        SubscribeToMarketChanges(_marketContext);
        WeakReferenceMessenger.Default.Register(this);

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
        await LoadAsync(async _ =>
        {
            var hotAssets = await HomeAssetService.GetHotAssetsAsync();

            // 先取收藏再填列表：星标随卡片一起出现，不会先空心再变实心
            var favoriteCodes = await GetFavoriteCodesAsync();

            HotAssets.Clear();
            var rank = 1;
            foreach (var asset in hotAssets)
            {
                var item = new HotAssetItem(asset, rank++);
                item.IsFavorite = favoriteCodes.Contains(item.FavoriteCode);
                HotAssets.Add(item);
            }

            return HotAssets.Count > 0;
        }, "加载热门资产");
    }

    [RelayCommand]
    private void SelectHotAsset(HotAssetItem? item)
    {
        if (item == null) return;

        HotAssetSelected?.Invoke(this, item.Asset);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(HotAssetItem? item)
    {
        if (item == null) return;

        // 星标不在这里改：收藏服务写库后会广播 AssetFavoritesChanged，由 Receive 统一回显
        await SafeExecuteAsync(async () =>
        {
            // 按卡片自己的市场取收藏服务：切换市场后旧列表重载前仍可见，不能写进新市场的收藏库
            var favoriteService = _marketContext.GetService<IFavoriteService>(item.Asset.MarketType);
            if (item.IsFavorite)
            {
                await favoriteService.RemoveFavoriteAsync(item.FavoriteCode, item.FavoriteMarket);
            }
            else
            {
                await favoriteService.AddFavoriteAsync(item.FavoriteCode, item.FavoriteMarket);
            }
        }, "更新自选");
    }

    /// <summary>按收藏库的真实状态回显所有热门卡的星标；切换与跨页同步都经 <see cref="Receive"/> 走到这里。</summary>
    private async Task RefreshFavoriteFlagsAsync()
    {
        var favoriteCodes = await GetFavoriteCodesAsync();
        foreach (var item in HotAssets)
        {
            item.IsFavorite = favoriteCodes.Contains(item.FavoriteCode);
        }
    }

    private async Task<HashSet<string>> GetFavoriteCodesAsync()
    {
        var favorites = await FavoriteService.GetFavoritesCodesAsync();
        return favorites.Select(favorite => favorite.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public void Receive(AssetFavoritesChanged message) =>
        Dispatcher.UIThread.Post(() => _ = SafeExecuteAsync(RefreshFavoriteFlagsAsync, "刷新自选状态"));

    public void Dispose()
    {
        UnsubscribeFromMarketChanges(_marketContext);
        WeakReferenceMessenger.Default.UnregisterAll(this);
        GC.SuppressFinalize(this);
    }
}
