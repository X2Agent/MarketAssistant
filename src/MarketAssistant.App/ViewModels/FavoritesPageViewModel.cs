using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Cache;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace MarketAssistant.ViewModels;

public partial class FavoritesPageViewModel : ViewModelBase, IRecipient<AssetFavoritesChanged>, IDisposable
{
    private readonly MarketContext _marketContext;
    private readonly IDialogService _dialogService;

    /// <summary>当前绑定事件的市场实时行情服务。市场切换后解析到不同实现时重新绑定。</summary>
    private IRealtimeQuoteService? _quoteService;

    /// <summary>
    /// 用于取消上一次加载任务的 CTS，防止并发加载导致列表闪烁或重复项。
    /// 构造函数、市场切换、收藏变更消息都可能触发加载，需串行化。
    /// </summary>
    private CancellationTokenSource? _loadCts;

    /// <summary>
    /// 实时行情标的（应用层资产代码）→ 展示对象的索引。
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
        _marketContext.GetService<IFavoriteService>();

    private IAssetInfoService AssetInfoService =>
        _marketContext.GetService<IAssetInfoService>();

    private IAssetCacheService CacheService =>
        _marketContext.GetService<IAssetCacheService>();

    public ObservableCollection<AssetInfo> Assets { get; set; } = new ObservableCollection<AssetInfo>();

    public FavoritesPageViewModel(
        MarketContext marketContext,
        IDialogService dialogService,
        ILogger<FavoritesPageViewModel> logger)
        : base(logger)
    {
        _marketContext = marketContext;
        _dialogService = dialogService;
        BindRealtimeQuoteService();
        SubscribeToMarketChanges(_marketContext);
        _ = LoadFavoriteAssetsAsync();
        WeakReferenceMessenger.Default.Register(this);
    }

    /// <summary>
    /// 绑定当前市场的实时行情服务事件。市场切换后 keyed 解析到不同实现时，
    /// 先解除旧服务的事件与订阅再绑定新服务，避免旧市场行情继续推送。
    /// </summary>
    private void BindRealtimeQuoteService()
    {
        var service = _marketContext.GetService<IRealtimeQuoteService>();
        if (ReferenceEquals(service, _quoteService))
            return;

        if (_quoteService != null)
        {
            _quoteService.PriceUpdated -= OnRealtimePriceUpdated;
            _ = _quoteService.UnsubscribeAllAsync(RealtimeQuoteSubscriberKeys.Favorites);
        }
        _quoteService = service;
        _quoteService.PriceUpdated += OnRealtimePriceUpdated;
    }

    /// <summary>
    /// 市场切换时重新加载收藏列表。
    /// 收藏页的订阅以完整集合替换，无需在此手动退订，加载时会自动更新订阅集。
    /// </summary>
    protected override void OnMarketChanged(MarketType newMarket)
    {
        // 事件来自单例 MarketContext，Dispose 后不得再触发（重启加载/实时行情订阅）
        if (_disposed)
            return;

        BindRealtimeQuoteService();
        _ = LoadFavoriteAssetsAsync();
    }

    private bool _disposed;

    private async Task LoadFavoriteAssetsAsync()
    {
        // 取消上一次加载任务，避免并发加载导致列表闪烁或重复项；
        // 只取消不 Dispose：在飞加载仍持有旧令牌，立即 Dispose 会偶发 ObjectDisposedException
        _loadCts?.Cancel();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        await SafeExecuteAsync(async () =>
        {
            var favoritesCodes = await FavoriteService.GetFavoritesCodesAsync();

            // 集合修改须在 UI 线程执行（加载可能由后台事件触发进入），避免跨线程操作 ObservableCollection
            await Dispatcher.UIThread.InvokeAsync(Assets.Clear);

            await UpdateAssetDataProgressivelyAsync(favoritesCodes, ct);

            RebuildAssetIndex();

            // 以完整集合替换收藏页订阅：支持实时推送的市场订阅自选资产；
            // 其他市场传空集合，确保切换市场后不残留上一市场的订阅
            var codes = _marketContext.CurrentCapability.SupportsRealtime
                ? Assets.Select(a => a.Code).ToList()
                : [];
            _ = _quoteService!.SubscribeAsync(RealtimeQuoteSubscriberKeys.Favorites, codes);
        }, "加载收藏列表");
    }

    /// <summary>
    /// 渐进式加载资产实时数据（限制并发数，避免同时打开过多浏览器页面）
    /// </summary>
    private async Task UpdateAssetDataProgressivelyAsync(List<FavoriteAsset> favorites, CancellationToken ct)
    {
        const int maxConcurrency = 3;
        using var semaphore = new SemaphoreSlim(maxConcurrency);

        var tasks = favorites.Select(async favorite =>
        {
            ct.ThrowIfCancellationRequested();
            await semaphore.WaitAsync(ct);
            try
            {
                var assetInfo = await CacheService.GetCachedAssetInfoAsync(favorite.Code);

                if (assetInfo == null)
                {
                    assetInfo = await AssetInfoService.GetAssetInfoAsync(favorite.Code, favorite.Market);
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

        // 落 UI 前复查取消：旧加载在新的 Clear 之后到达时不得再追加（防残留/重复条目）
        ct.ThrowIfCancellationRequested();

        // 集合修改须在 UI 线程执行（加载可能由后台事件触发进入），避免跨线程操作 ObservableCollection
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            foreach (var assetInfo in results)
            {
                if (assetInfo != null)
                {
                    Assets.Add(assetInfo);
                }
            }
        });
    }

    [RelayCommand]
    private void SelectFavoriteAsset(AssetInfo? asset)
    {
        if (asset == null) return;

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

    [RelayCommand]
    private async Task RemoveFavorite(AssetInfo? asset)
    {
        if (asset == null) return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "取消收藏",
            $"确定要取消收藏 {asset.Name}({asset.Code}) 吗？",
            "确定",
            "取消"
        );

        if (confirmed)
        {
            await SafeExecuteAsync(async () =>
            {
                // 先从UI集合中移除（避免因消息触发重新加载导致的竞态条件）
                var assetToRemove = Assets.FirstOrDefault(s => s.Code == asset.Code && s.Market == asset.Market);
                if (assetToRemove != null)
                {
                    Assets.Remove(assetToRemove);
                    _assetIndex.TryRemove(assetToRemove.Code, out _);
                    _pendingPriceUpdates.TryRemove(assetToRemove.Code, out _);
                }

                await FavoriteService.RemoveFavoriteAsync(asset.Code, asset.Market);

                Logger?.LogInformation($"已取消收藏资产: {asset.Name}({asset.Code})");
                await Task.CompletedTask;
            }, "取消收藏");
        }
    }

    /// <summary>
    /// 实时价格更新回调（后台线程），参数为应用层资产代码。
    /// 索引未命中（非本页标的）直接返回，不产生任何 UI 线程派发；
    /// 命中则暂存最新值，由 250ms 节流定时器批量刷新，避免高频 tick 逐条打 UI。
    /// </summary>
    private void OnRealtimePriceUpdated(string code, decimal lastPrice, decimal changePercent)
    {
        if (!_assetIndex.ContainsKey(code))
            return;

        _pendingPriceUpdates[code] = (lastPrice, changePercent);
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
            _assetIndex[asset.Code] = asset;
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
            if (_disposed || _priceFlushTimer != null)
                return;

            _priceFlushTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(250)
            };
            _priceFlushTimer.Tick += FlushPendingPriceUpdates;
            _priceFlushTimer.Start();
        });
    }

    /// <summary>
    /// 批量应用暂存的价格更新到展示对象（UI 线程，每 250ms 至多一次）。
    /// </summary>
    private void FlushPendingPriceUpdates(object? sender, EventArgs e)
    {
        foreach (var symbol in _pendingPriceUpdates.Keys.ToList())
        {
            if (!_pendingPriceUpdates.TryRemove(symbol, out var update))
                continue;

            if (_assetIndex.TryGetValue(symbol, out var asset))
            {
                asset.CurrentPrice = PriceFormatter.Format(update.Price);
                // InvariantCulture 与 PriceChangeColorConverter 的解析端保持同一 culture，否则逗号小数区域下解析失败标签变灰
                asset.ChangePercentage = update.Change.ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + "%";
            }
        }
    }

    public void Receive(AssetFavoritesChanged message)
    {
        _ = LoadFavoriteAssetsAsync();
    }

    public void Dispose()
    {
        _disposed = true;
        _loadCts?.Cancel();
        if (_priceFlushTimer != null)
        {
            _priceFlushTimer.Tick -= FlushPendingPriceUpdates;
            _priceFlushTimer.Stop();
            _priceFlushTimer = null;
        }
        UnsubscribeFromMarketChanges(_marketContext);
        if (_quoteService != null)
        {
            _quoteService.PriceUpdated -= OnRealtimePriceUpdated;
            _ = _quoteService.UnsubscribeAllAsync(RealtimeQuoteSubscriberKeys.Favorites);
        }
        WeakReferenceMessenger.Default.UnregisterAll(this);
        GC.SuppressFinalize(this);
    }
}
