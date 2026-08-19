using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
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

    /// <summary>
    /// 用于取消上一次加载任务的 CTS，防止并发加载导致列表闪烁或重复项。
    /// 构造函数、市场切换、收藏变更消息都可能触发加载，需串行化。
    /// </summary>
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// WebSocket 标的（Binance 格式）→ 展示对象的索引。
    /// tick 回调在后台线程先查索引：非本页标的直接返回，避免无谓的 UI 线程派发与逐项扫描。
    /// </summary>
    private readonly ConcurrentDictionary<string, AssetInfo> _assetIndex = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 待刷新的价格更新（按标的去重，只保留最新值），由 <see cref="_priceFlushTimer"/> 节流批量刷 UI。
    /// </summary>
    private readonly ConcurrentDictionary<string, (decimal Price, decimal Change)> _pendingPriceUpdates =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 价格刷新节流定时器（250ms）。将每 tick 一次的 UI 派发合并为每秒 4 次批量更新。
    /// 惰性创建于 UI 线程，Dispose 时停止。
    /// </summary>
    private DispatcherTimer? _priceFlushTimer;

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
        SubscribeToMarketChanges(_marketContext);
        _ = LoadFavoriteAssetsAsync();
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 市场切换时重新加载收藏列表。
    /// 收藏页的订阅以完整集合替换，无需在此手动退订，加载时会自动更新订阅集。
    /// </summary>
    protected override void OnMarketChanged(MarketType newMarket)
    {
        _ = LoadFavoriteAssetsAsync();
    }

    /// <summary>
    /// 加载收藏资产列表
    /// </summary>
    private async Task LoadFavoriteAssetsAsync()
    {
        // 取消上一次加载任务，避免并发加载导致列表闪烁或重复项
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        await SafeExecuteAsync(async () =>
        {
            var favoritesCodes = await FavoriteService.GetFavoritesCodesAsync();
            Assets.Clear();
            await UpdateAssetDataProgressivelyAsync(favoritesCodes, ct);

            RebuildAssetIndex();

            // 以完整集合替换收藏页订阅：虚拟币市场订阅自选交易对；
            // 其他市场传空集合，确保切换市场后不残留上一市场的订阅
            var symbols = _marketContext.CurrentMarket == MarketType.Crypto
                ? Assets.Select(a => ToBinanceFormat(a.Code)).ToList()
                : [];
            _ = _wsService.SubscribeAsync(WebSocketSubscriberKeys.Favorites, symbols);
        }, "加载收藏列表");
    }

    /// <summary>
    /// 渐进式加载资产实时数据（限制并发数，避免同时打开过多浏览器页面）
    /// </summary>
    private async Task UpdateAssetDataProgressivelyAsync(List<FavoriteAsset> favorites, CancellationToken ct)
    {
        const int maxConcurrency = 3; // 最多同时请求3个资产数据
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = favorites.Select(async favorite =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct);
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
            catch (OperationCanceledException)
            {
                throw;
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
                    _assetIndex.TryRemove(ToBinanceFormat(assetToRemove.Code), out _);
                    _pendingPriceUpdates.TryRemove(ToBinanceFormat(assetToRemove.Code), out _);
                }

                // 再从持久化存储中移除
                await FavoriteService.RemoveFavoriteAsync(asset.Code, asset.Market);

                Logger?.LogInformation($"已取消收藏资产: {asset.Name}({asset.Code})");
                await Task.CompletedTask;
            }, "取消收藏");
        }
    }

    /// <summary>
    /// WebSocket 实时价格更新回调（后台线程）。
    /// 索引未命中（非本页标的）直接返回，不产生任何 UI 线程派发；
    /// 命中则暂存最新值，由 250ms 节流定时器批量刷新，避免高频 tick 逐条打 UI。
    /// </summary>
    private void OnWebSocketPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        if (!_assetIndex.ContainsKey(symbol))
            return;

        _pendingPriceUpdates[symbol] = (lastPrice, changePercent);
        EnsurePriceFlushTimer();
    }

    /// <summary>
    /// 重建标的索引（列表加载完成后调用）。
    /// </summary>
    private void RebuildAssetIndex()
    {
        _assetIndex.Clear();
        foreach (var asset in Assets)
        {
            _assetIndex[ToBinanceFormat(asset.Code)] = asset;
        }
    }

    /// <summary>
    /// 惰性创建节流定时器。DispatcherTimer 必须在 UI 线程构造，
    /// 故先快检再 Post；Post 内二次判空防重复创建。
    /// </summary>
    private void EnsurePriceFlushTimer()
    {
        if (_priceFlushTimer != null)
            return;

        Dispatcher.UIThread.Post(() =>
        {
            if (_priceFlushTimer != null)
                return;

            _priceFlushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _priceFlushTimer.Tick += (_, _) => FlushPendingPriceUpdates();
            _priceFlushTimer.Start();
        });
    }

    /// <summary>
    /// 批量应用暂存的价格更新到展示对象（UI 线程，每 250ms 至多一次）。
    /// </summary>
    private void FlushPendingPriceUpdates()
    {
        foreach (var symbol in _pendingPriceUpdates.Keys.ToList())
        {
            if (!_pendingPriceUpdates.TryRemove(symbol, out var update))
                continue;

            if (_assetIndex.TryGetValue(symbol, out var asset))
            {
                asset.CurrentPrice = update.Price.ToString("G");
                asset.ChangePercentage = $"{update.Change:F2}%";
            }
        }
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
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _priceFlushTimer?.Stop();
        _priceFlushTimer = null;
        UnsubscribeFromMarketChanges(_marketContext);
        _wsService.PriceUpdated -= OnWebSocketPriceUpdated;
        _ = _wsService.UnsubscribeAllAsync(WebSocketSubscriberKeys.Favorites);
        WeakReferenceMessenger.Default.UnregisterAll(this);
        GC.SuppressFinalize(this);
    }
}
