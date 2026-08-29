using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels.Home;

public partial class RecentAssetsViewModel : ViewModelBase, IDisposable
{
    private readonly IMarketServiceRegistry _marketServiceRegistry;
    private readonly MarketContext _marketContext;

    private IAssetHistoryService HistoryService =>
        _marketServiceRegistry.GetAssetHistoryService(_marketContext.CurrentMarket);

    private IHomeAssetService HomeAssetService =>
        _marketServiceRegistry.GetHomeAssetService(_marketContext.CurrentMarket);

    private IAssetInfoService AssetInfoService =>
        _marketServiceRegistry.GetAssetInfoService(_marketContext.CurrentMarket);

    public ObservableCollection<AssetItem> RecentAssets { get; } = new();

    public event EventHandler<AssetItem>? RecentAssetSelected;

    public RecentAssetsViewModel(
        IMarketServiceRegistry marketServiceRegistry,
        MarketContext marketContext,
        ILogger<RecentAssetsViewModel> logger)
        : base(logger)
    {
        _marketServiceRegistry = marketServiceRegistry;
        _marketContext = marketContext;

        SubscribeToMarketChanges(_marketContext);

        _ = LoadRecentAssetsAsync();
    }

    /// <summary>
    /// 市场切换时重新加载最近查看资产
    /// </summary>
    protected override void OnMarketChanged(MarketType newMarket)
    {
        _ = LoadRecentAssetsAsync();
    }

    [RelayCommand]
    private async Task LoadRecentAssetsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var recentAssets = await HistoryService.GetHistoryAsync();

            // 并行补全实时价格/涨跌幅（单个失败仅留空，不影响整体加载）
            await Task.WhenAll(recentAssets.Select(EnrichWithQuoteAsync));

            RecentAssets.Clear();
            foreach (var asset in recentAssets)
            {
                RecentAssets.Add(asset);
            }
        }, "加载最近查看资产");
    }

    private async Task EnrichWithQuoteAsync(AssetItem asset)
    {
        try
        {
            var info = await AssetInfoService.GetAssetInfoAsync(asset.Code);
            asset.CurrentPrice = info.CurrentPrice;
            asset.ChangePercentage = info.ChangePercentage;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "补全最近查看资产行情失败: {Code}", asset.Code);
        }
    }

    public async Task AddToRecentAssetsAsync(AssetItem asset)
    {
        await SafeExecuteAsync(async () =>
        {
            await HistoryService.AddHistoryAsync(asset);
            await LoadRecentAssetsAsync();
        }, "添加到最近查看");
    }

    [RelayCommand]
    private void SelectRecentAsset(AssetItem? asset)
    {
        if (asset == null) return;

        RecentAssetSelected?.Invoke(this, asset);
    }

    [RelayCommand]
    private async Task AddToFavoriteAsync(AssetItem? asset)
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






