using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 收藏页ViewModel
/// </summary>
public partial class FavoritesPageViewModel : ViewModelBase, IRecipient<AssetFavoritesChanged>, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MarketContext _marketContext;
    private readonly IDialogService _dialogService;
    private readonly BinanceWebSocketService _wsService;

    private IFavoriteService FavoriteService => 
        _serviceProvider.GetRequiredKeyedService<IFavoriteService>(_marketContext.CurrentMarket);

    private IAssetInfoService AssetInfoService => 
        _serviceProvider.GetRequiredKeyedService<IAssetInfoService>(_marketContext.CurrentMarket);

    private IAssetCacheService CacheService => 
        _serviceProvider.GetRequiredKeyedService<IAssetCacheService>(_marketContext.CurrentMarket);

    public ObservableCollection<AssetInfo> Assets { get; set; } = new ObservableCollection<AssetInfo>();

    /// <summary>
    /// 构造函数
    /// </summary>
    public FavoritesPageViewModel(
        IServiceProvider serviceProvider,
        MarketContext marketContext,
        IDialogService dialogService,
        BinanceWebSocketService wsService,
        ILogger<FavoritesPageViewModel> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
        _marketContext = marketContext;
        _dialogService = dialogService;
        _wsService = wsService;
        _wsService.PriceUpdated += OnWebSocketPriceUpdated;
        _ = LoadFavoriteAssetsAsync();
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 加载收藏资产列表
    /// </summary>
    private async Task LoadFavoriteAssetsAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var favoritesCodes = FavoriteService.GetFavoritesCodes();
            Assets.Clear();
            await UpdateAssetDataProgressivelyAsync(favoritesCodes);

            // 虚拟币市场启用 WebSocket 实时推送
            if (_marketContext.CurrentMarket == MarketType.Crypto && Assets.Count > 0)
            {
                var symbols = Assets.Select(a => ToBinanceFormat(a.Code)).ToList();
                _ = _wsService.SubscribeAsync(symbols);
            }
        }, "加载收藏列表");
    }

    /// <summary>
    /// 渐进式加载资产实时数据（限制并发数，避免同时打开过多浏览器页面）
    /// </summary>
    private async Task UpdateAssetDataProgressivelyAsync(List<FavoriteAsset> favorites)
    {
        const int maxConcurrency = 3; // 最多同时请求3个资产数据
        var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = favorites.Select(async favorite =>
        {
            await semaphore.WaitAsync();
            try
            {
                // 先尝试从缓存获取
                var assetInfo = await CacheService.GetCachedAssetInfoAsync(favorite.Code);

                // 如果缓存中没有,则从网络获取
                if (assetInfo == null)
                {
                    assetInfo = await AssetInfoService.GetAssetInfoAsync(favorite.Code, favorite.Market);
                    // 缓存获取到的数据
                    if (assetInfo != null)
                    {
                        CacheService.CacheAssetInfo(favorite.Code, assetInfo);
                    }
                }

                return assetInfo;
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, $"加载资产 {favorite.Code} 数据时出错");
                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);

        // 在UI线程上批量添加结果
        foreach (var assetInfo in results)
        {
            if (assetInfo != null)
            {
                Assets.Add(assetInfo);
            }
        }
    }

    /// <summary>
    /// 选择收藏资产
    /// </summary>
    [RelayCommand]
    private void SelectFavoriteAsset(AssetInfo? asset)
    {
        if (asset == null) return;

        // 解析价格信息
        decimal? currentPrice = decimal.TryParse(asset.CurrentPrice, out var price) ? price : null;
        decimal? changePercent = decimal.TryParse(asset.ChangePercentage?.TrimEnd('%'), out var percent) ? percent : null;

        // 传递完整的基本信息，加速详情页显示
        WeakReferenceMessenger.Default.Send(
            new NavigationMessage("Asset", new AssetNavigationParameter(
                asset.Code, 
                asset.Name,
                currentPrice,
                changePercent)));
    }

    /// <summary>
    /// 移除收藏资产
    /// </summary>
    [RelayCommand]
    private async Task RemoveFavorite(AssetInfo? asset)
    {
        if (asset == null) return;

        // 显示确认对话框
        var confirmed = await _dialogService.ShowConfirmationAsync(
            "取消收藏",
            $"确定要取消收藏 {asset.Name}({asset.Code}) 吗？",
            "确定",
            "取消"
        );

        // 用户确认后才执行删除
        if (confirmed)
        {
            await SafeExecuteAsync(async () =>
            {
                // 先从UI集合中移除（避免因消息触发重新加载导致的竞态条件）
                var assetToRemove = Assets.FirstOrDefault(s => s.Code == asset.Code && s.Market == asset.Market);
                if (assetToRemove != null)
                {
                    Assets.Remove(assetToRemove);
                }

                // 再从持久化存储中移除
                FavoriteService.RemoveFavorite(asset.Code, asset.Market);

                Logger?.LogInformation($"已取消收藏资产: {asset.Name}({asset.Code})");
                await Task.CompletedTask;
            }, "取消收藏");
        }
    }

    /// <summary>
    /// WebSocket 实时价格更新回调
    /// </summary>
    private void OnWebSocketPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            var asset = Assets.FirstOrDefault(a =>
                ToBinanceFormat(a.Code).Equals(symbol, StringComparison.OrdinalIgnoreCase));

            if (asset == null) return;

            asset.CurrentPrice = lastPrice.ToString("G");
            asset.ChangePercentage = $"{changePercent:F2}%";
        });
    }

    /// <summary>
    /// 接收收藏变更消息
    /// </summary>
    public void Receive(AssetFavoritesChanged message)
    {
        _ = LoadFavoriteAssetsAsync();
    }

    public void Dispose()
    {
        _wsService.PriceUpdated -= OnWebSocketPriceUpdated;
        WeakReferenceMessenger.Default.UnregisterAll(this);
        GC.SuppressFinalize(this);
    }
}
