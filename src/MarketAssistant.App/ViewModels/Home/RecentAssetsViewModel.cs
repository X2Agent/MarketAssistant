using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.History;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels.Home;

public partial class RecentAssetsViewModel : ViewModelBase, IDisposable
{
    private readonly MarketContext _marketContext;

    private IAssetHistoryService HistoryService =>
        _marketContext.GetService<IAssetHistoryService>();

    private IAssetInfoService AssetInfoService =>
        _marketContext.GetService<IAssetInfoService>();

    public ObservableCollection<AssetItem> RecentAssets { get; } = [];

    public event EventHandler<AssetItem>? RecentAssetSelected;

    public RecentAssetsViewModel(
        MarketContext marketContext,
        ILogger<RecentAssetsViewModel> logger)
        : base(logger)
    {
        _marketContext = marketContext;

        EmptyMessage = "暂无最近查看记录";
        SetRetryCommand(() => LoadRecentAssetsAsync());

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
        await LoadAsync(async _ =>
        {
            var recentAssets = await HistoryService.GetHistoryAsync();

            // 单次批量补全实时价格/涨跌幅（虚拟币为一次批量行情请求；行情仅为附加信息，失败项为 null 时留空）
            var infos = await AssetInfoService.GetAssetInfosAsync(
                [.. recentAssets.Select(asset => new FavoriteAsset { Code = asset.Code })]);
            for (var i = 0; i < recentAssets.Count; i++)
            {
                if (infos[i] is not { } info)
                    continue;

                recentAssets[i].CurrentPrice = info.CurrentPrice;
                recentAssets[i].ChangePercentage = info.ChangePercentage;
            }

            RecentAssets.Clear();
            foreach (var asset in recentAssets)
            {
                RecentAssets.Add(asset);
            }

            return RecentAssets.Count > 0;
        }, "加载最近查看资产");
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

    public void Dispose()
    {
        UnsubscribeFromMarketChanges(_marketContext);
        GC.SuppressFinalize(this);
    }
}






