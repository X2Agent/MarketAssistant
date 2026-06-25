using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 最近查看资产ViewModel
/// </summary>
public partial class RecentAssetsViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MarketContext _marketContext;

    private IAssetHistoryService HistoryService =>
        _serviceProvider.GetRequiredKeyedService<IAssetHistoryService>(_marketContext.CurrentMarket);

    private IHomeAssetService HomeAssetService =>
        _serviceProvider.GetRequiredKeyedService<IHomeAssetService>(_marketContext.CurrentMarket);

    /// <summary>
    /// 最近查看资产集合
    /// </summary>
    public ObservableCollection<AssetItem> RecentAssets { get; } = new();

    /// <summary>
    /// 最近资产选择事件
    /// </summary>
    public event EventHandler<AssetItem>? RecentAssetSelected;

    public RecentAssetsViewModel(
        IServiceProvider serviceProvider,
        MarketContext marketContext,
        ILogger<RecentAssetsViewModel> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
        _marketContext = marketContext;

        // 订阅市场切换事件
        SubscribeToMarketChanges(_marketContext);

        // 自动加载最近资产
        _ = LoadRecentAssetsAsync();
    }

    /// <summary>
    /// 市场切换时重新加载最近查看资产
    /// </summary>
    protected override void OnMarketChanged(MarketType newMarket)
    {
        _ = LoadRecentAssetsAsync();
    }

    /// <summary>
    /// 加载最近查看资产
    /// </summary>
    [RelayCommand]
    private async Task LoadRecentAssetsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var recentAssets = await HistoryService.GetHistoryAsync();

            RecentAssets.Clear();
            foreach (var asset in recentAssets)
            {
                RecentAssets.Add(asset);
            }
        }, "加载最近查看资产");
    }

    /// <summary>
    /// 添加资产到最近查看
    /// </summary>
    public async Task AddToRecentAssetsAsync(AssetItem asset)
    {
        await SafeExecuteAsync(async () =>
        {
            await HistoryService.AddHistoryAsync(asset);
            await LoadRecentAssetsAsync();
        }, "添加到最近查看");
    }

    /// <summary>
    /// 选择最近资产
    /// </summary>
    [RelayCommand]
    private void SelectRecentAsset(AssetItem? asset)
    {
        if (asset == null) return;

        // 通知父ViewModel
        RecentAssetSelected?.Invoke(this, asset);
    }

    /// <summary>
    /// 添加到收藏
    /// </summary>
    [RelayCommand]
    private async Task AddToFavoriteAsync(AssetItem? asset)
    {
        if (asset == null) return;

        await SafeExecuteAsync(async () =>
        {
            await HomeAssetService.AddToFavoriteAsync(asset);
        }, "添加收藏");
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        UnsubscribeFromMarketChanges(_marketContext);
        GC.SuppressFinalize(this);
    }
}






