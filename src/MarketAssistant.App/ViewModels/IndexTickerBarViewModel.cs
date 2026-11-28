using MarketAssistant.Services;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 首页行情条：市场级聚合指标（A 股三大指数 / Crypto 总市值·主导率·恐惧贪婪）。
/// v3 设计系统移除顶部 Header 后，行情条归属首页头部区域；
/// 生命周期由宿主页面（HomePageViewModel）持有，Dispose 时停表并退订。
/// </summary>
public partial class IndexTickerBarViewModel : ViewModelBase, IDisposable
{
    /// <summary>行情条定时刷新器（30s），构造时创建并启动。</summary>
    private Avalonia.Threading.DispatcherTimer? _tickerTimer;

    private readonly MarketContext _marketContext;

    /// <summary>
    /// 行情条刷新请求代数：每次发起刷新自增，仅最新一代结果落地。
    /// 替代 bool 防重入——切市场时若旧请求在飞，新请求不会被丢弃，旧结果落地前被淘汰，
    /// 消除"切市场后行情条残留上一个市场数据"的竞态。
    /// </summary>
    private int _tickerRequestId;

    /// <summary>行情条数据项（无数据时视图整段隐藏）。</summary>
    public ObservableCollection<IndexTickerItemViewModel> IndexTickers { get; }

    /// <summary>行情条是否有数据（无数据时整段隐藏，对齐设计系统裁决 #6）。</summary>
    public bool HasIndexTickers => IndexTickers.Count > 0;

    private bool _disposed;

    public IndexTickerBarViewModel(MarketContext marketContext, ILogger<IndexTickerBarViewModel>? logger = null)
        : base(logger)
    {
        _marketContext = marketContext;
        IndexTickers = [];
        SubscribeToMarketChanges(marketContext);
        _ = RefreshIndexTickersAsync();
        StartTickerTimer();
    }

    protected override void OnMarketChanged(MarketType newMarket)
    {
        if (_disposed)
            return;

        _ = RefreshIndexTickersAsync();
    }

    /// <summary>
    /// 按当前市场拉取市场级聚合指标填充行情条。
    /// 通过 Keyed 解析 <see cref="IIndexQuoteService"/>（A 股=三大指数，Crypto=总市值/主导率/恐惧贪婪）；
    /// 无数据或失败时保持为空，行情条随 <see cref="HasIndexTickers"/> 自动隐藏。
    /// 并发策略：允许并发刷新，落地前校验请求代数，仅最新一代结果生效。
    /// </summary>
    private async Task RefreshIndexTickersAsync()
    {
        // 发起即自增代数：切市场/定时刷新并发时，只有最新一代的结果落地
        var requestId = Interlocked.Increment(ref _tickerRequestId);

        try
        {
            var market = _marketContext.CurrentMarket;
            var items = await _marketContext.GetService<IIndexQuoteService>(market).GetLatestAsync();

            // 拉取期间已发起过更新的刷新、已切市场或页面已释放，丢弃过期结果
            if (_disposed ||
                requestId != Volatile.Read(ref _tickerRequestId) ||
                market != _marketContext.CurrentMarket)
                return;

            IndexTickers.Clear();
            foreach (var item in items)
            {
                IndexTickers.Add(IndexTickerItemViewModel.FromTrend(item.Name, item.Value, item.ChangeText, item.Trend));
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "刷新首页行情条失败");
        }
        finally
        {
            OnPropertyChanged(nameof(HasIndexTickers));
        }
    }

    /// <summary>启动行情条 30s 定时刷新（惰性创建于 UI 线程）。</summary>
    private void StartTickerTimer()
    {
        _tickerTimer ??= new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        _tickerTimer.Tick -= OnTickerTimerTick;
        _tickerTimer.Tick += OnTickerTimerTick;
        _tickerTimer.Start();
    }

    private void OnTickerTimerTick(object? sender, EventArgs e)
        => _ = RefreshIndexTickersAsync();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _tickerTimer?.Stop();
        _tickerTimer = null;
        // 必须退订：行情条随首页存活，离开首页后若不退订，
        // MarketContext.PropertyChanged 会继续持有本实例并在切市场时向已废弃实例写集合。
        UnsubscribeFromMarketChanges(_marketContext);
        GC.SuppressFinalize(this);
    }
}
