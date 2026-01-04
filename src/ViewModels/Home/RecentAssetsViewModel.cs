using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.History;
using MarketAssistant.Applications.Home;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 最近查看资产ViewModel
/// </summary>
public partial class RecentAssetsViewModel : ViewModelBase
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
    /// 选择最近资产命令
    /// </summary>
    public IRelayCommand<AssetItem> SelectRecentAssetCommand { get; }

    /// <summary>
    /// 添加到收藏命令
    /// </summary>
    public IAsyncRelayCommand<AssetItem> AddToFavoriteCommand { get; }

    /// <summary>
    /// 刷新最近资产命令
    /// </summary>
    public IRelayCommand RefreshCommand { get; }

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

        SelectRecentAssetCommand = new RelayCommand<AssetItem>(OnSelectRecentAsset);
        AddToFavoriteCommand = new AsyncRelayCommand<AssetItem>(OnAddToFavoriteAsync);
        RefreshCommand = new RelayCommand(LoadRecentAssets);

        // 自动加载最近资产
        LoadRecentAssets();
    }

    /// <summary>
    /// 加载最近查看资产
    /// </summary>
    public void LoadRecentAssets()
    {
        SafeExecute(() =>
        {
            var recentAssets = HistoryService.GetHistory();

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
    public void AddToRecentAssets(AssetItem asset)
    {
        SafeExecute(() =>
        {
            HistoryService.AddHistory(asset);
            LoadRecentAssets(); // 刷新列表
        }, "添加到最近查看");
    }

    /// <summary>
    /// 选择最近资产
    /// </summary>
    private void OnSelectRecentAsset(AssetItem? asset)
    {
        if (asset == null) return;

        // 通知父ViewModel
        RecentAssetSelected?.Invoke(this, asset);
    }

    /// <summary>
    /// 添加到收藏
    /// </summary>
    private async Task OnAddToFavoriteAsync(AssetItem? asset)
    {
        if (asset == null) return;

        await SafeExecuteAsync(async () =>
        {
            await HomeAssetService.AddToFavoriteAsync(asset);
        }, "添加收藏");
    }
}






