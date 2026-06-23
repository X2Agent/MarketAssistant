using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Home;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 热门资产ViewModel
/// </summary>
public partial class HotAssetsViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MarketContext _marketContext;

    private IHomeAssetService HomeAssetService =>
        _serviceProvider.GetRequiredKeyedService<IHomeAssetService>(_marketContext.CurrentMarket);

    /// <summary>
    /// 热门资产集合
    /// </summary>
    public ObservableCollection<HotAsset> HotAssets { get; } = new();

    /// <summary>
    /// 热门资产选择事件
    /// </summary>
    public event EventHandler<HotAsset>? HotAssetSelected;

    public HotAssetsViewModel(
        IServiceProvider serviceProvider,
        MarketContext marketContext,
        ILogger<HotAssetsViewModel> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
        _marketContext = marketContext;

        // 订阅市场切换事件
        SubscribeToMarketChanges(_marketContext);

        // 自动加载热门资产
        _ = LoadHotAssetsAsync();
    }

    /// <summary>
    /// 市场切换时重新加载热门资产
    /// </summary>
    protected override void OnMarketChanged(MarketType newMarket)
    {
        _ = LoadHotAssetsAsync();
    }

    /// <summary>
    /// 加载热门资产
    /// </summary>
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

    /// <summary>
    /// 选择热门资产
    /// </summary>
    [RelayCommand]
    private void SelectHotAsset(HotAsset? asset)
    {
        if (asset == null) return;

        // 通知父ViewModel
        HotAssetSelected?.Invoke(this, asset);
    }

    /// <summary>
    /// 添加到收藏
    /// </summary>
    [RelayCommand]
    private async Task AddToFavoriteAsync(HotAsset? asset)
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






