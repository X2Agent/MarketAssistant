using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.Market;
using MarketAssistant.Applications.Market.Models;
using MarketAssistant.Applications.Technical;
using MarketAssistant.Applications.Technical.Models;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

public partial class AssetPageViewModel : ViewModelBase, INavigationAware<AssetNavigationParameter>, IDisposable
{
    /// <summary>盘口展示档位数（币安 depth 仅接受 5/10/20/50/100/500/1000/5000）。</summary>
    private const int OrderBookDepth = 5;

    /// <summary>逐笔成交展示条数。</summary>
    private const int TickTradeLimit = 20;

    /// <summary>技术指标计算所需 K 线根数，需覆盖最长均线周期 MA250。</summary>
    private const int IndicatorBarCount = 250;

    /// <summary>技术指标面板展示的均线周期（完整周期集合见 <see cref="TechnicalIndicatorSnapshot"/>）。</summary>
    private static readonly int[] DisplayMovingAveragePeriods = [5, 10, 20, 30, 60];

    /// <summary>盘口自动刷新最小间隔。价格推送频率远高于盘口变化频率，必须节流。</summary>
    private static readonly TimeSpan OrderBookRefreshInterval = TimeSpan.FromSeconds(3);

    public override string Title => "资产详情";

    private readonly MarketContext _marketContext;
    private CancellationTokenSource? _loadingCancellationTokenSource;
    private DateTime _lastOrderBookRefreshAt = DateTime.MinValue;
    private bool _isOrderBookRefreshing;

    // 各加载路径可重入（切换资产/周期、右栏盘口补拉、单区块重试都会取消旧加载后重新进入），
    // 而 K 线等数据源的请求不支持取消、只能在其自然返回后丢弃结果。
    // 通过代次计数识别过期调用：仅最新一次尝试允许写入状态，避免延迟返回的旧结果覆写新加载的状态。
    private int _pageLoadGeneration;
    private int _kLineAttempt;
    private int _snapshotAttempt;
    private int _indicatorAttempt;
    private int _orderBookAttempt;
    private int _tickTradeAttempt;

    /// <summary>当前绑定事件的实时行情服务。导航参数携带的市场与当前绑定不一致时重新绑定。</summary>
    private IRealtimeQuoteService? _quoteService;

    [ObservableProperty]
    private KLineType _currentKLineType = KLineType.Daily;

    [ObservableProperty]
    private string _assetCode = "";

    [ObservableProperty]
    private string _assetName = string.Empty;

    [ObservableProperty]
    private ObservableCollection<KLineData> _kLineData = [];

    [ObservableProperty]
    private decimal _currentPrice;

    [ObservableProperty]
    private decimal _priceChangePercent;

    [ObservableProperty]
    private decimal _priceChange;

    /// <summary>当前标的是否已加入自选（按详情页所属市场判定）。</summary>
    [ObservableProperty]
    private bool _isFavorite;

    /// <summary>行情速览指标（今开 / 最高 / 最低 / 昨收 / 成交量 / 成交额 / 换手 / 振幅 / 量比 / 市盈率 / 市净率）。</summary>
    [ObservableProperty]
    private ObservableCollection<MarketMetricItem> _marketMetrics = [];

    /// <summary>技术指标分组（均线 / MACD / BOLL / KDJ），统一按日线口径计算。</summary>
    [ObservableProperty]
    private ObservableCollection<IndicatorGroupItem> _indicatorGroups = [];

    /// <summary>技术指标对应的 K 线日期。</summary>
    [ObservableProperty]
    private string _indicatorDateText = string.Empty;

    /// <summary>卖盘五档，按「卖五 → 卖一」展示，使卖一紧邻中间价。</summary>
    [ObservableProperty]
    private ObservableCollection<OrderBookLevelItem> _asks = [];

    /// <summary>买盘五档，按「买一 → 买五」展示。</summary>
    [ObservableProperty]
    private ObservableCollection<OrderBookLevelItem> _bids = [];

    [ObservableProperty]
    private string _orderBookSummaryText = string.Empty;

    /// <summary>买盘挂单占比（%），用于盘口深度概览条。</summary>
    [ObservableProperty]
    private double _bidQuantityPercent;

    [ObservableProperty]
    private string _bidQuantityPercentText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<TickTradeItem> _tickTrades = [];

    [ObservableProperty]
    private SectionState _snapshotState = new();

    [ObservableProperty]
    private SectionState _indicatorState = new();

    [ObservableProperty]
    private SectionState _orderBookState = new();

    [ObservableProperty]
    private SectionState _tickTradeState = new();

    /// <summary>右栏（技术指标 + 盘口 + 逐笔）展开状态，折叠后主图独占宽度。</summary>
    [ObservableProperty]
    private bool _isRightPanelVisible = true;

    /// <summary>右栏折叠按钮文案。</summary>
    public string RightPanelToggleText => IsRightPanelVisible ? "收起右栏" : "展开右栏";

    partial void OnIsRightPanelVisibleChanged(bool value) => OnPropertyChanged(nameof(RightPanelToggleText));

    /// <summary>收藏按钮文案：已自选显示“已自选”，否则“加入自选”。</summary>
    public string FavoriteLabel => IsFavorite ? "已自选" : "加入自选";

    partial void OnIsFavoriteChanged(bool value) => OnPropertyChanged(nameof(FavoriteLabel));

    /// <summary>当前市场是否支持交易（决定“快捷交易”按钮可见性，A 股隐藏）。</summary>
    public bool CanTrade => _marketContext.CurrentCapability.SupportsTrading;

    /// <summary>当前市场是否提供盘口深度（A 股无公开五档数据源）。</summary>
    public bool SupportsOrderBook => _marketContext.CurrentCapability.SupportsOrderBook;

    /// <summary>当前市场是否提供逐笔成交。</summary>
    public bool SupportsTickTrades => _marketContext.CurrentCapability.SupportsTickTrades;

    /// <summary>当前价展示文本（按量级格式化，适配低价币）</summary>
    public string CurrentPriceText => PriceFormatter.Format(CurrentPrice);

    /// <summary>涨跌额展示文本（按量级格式化，适配低价币）</summary>
    public string PriceChangeText => PriceFormatter.Format(PriceChange);

    partial void OnCurrentPriceChanged(decimal value) => OnPropertyChanged(nameof(CurrentPriceText));

    partial void OnPriceChangeChanged(decimal value) => OnPropertyChanged(nameof(PriceChangeText));

    /// <summary>刷新中但已有内容：主图不闪骨架屏，仅提示正在刷新。</summary>
    public bool IsRefreshing => IsBusy && !IsInitialLoading;

    public bool IsMinuteSelected => CurrentKLineType == KLineType.Minute15;
    public bool IsDailySelected => CurrentKLineType == KLineType.Daily;
    public bool IsWeeklySelected => CurrentKLineType == KLineType.Weekly;
    public bool IsMonthlySelected => CurrentKLineType == KLineType.Monthly;

    public IRelayCommand<string> ChangeKLineTypeCommand { get; private set; }
    public IRelayCommand NavigateToAnalysisCommand { get; private set; }
    public IRelayCommand NavigateToTradingCommand { get; private set; }
    public IRelayCommand NavigateToAlertCommand { get; private set; }
    public IRelayCommand ToggleRightPanelCommand { get; private set; }
    public IAsyncRelayCommand ToggleFavoriteCommand { get; private set; }

    public AssetPageViewModel(
        ILogger<AssetPageViewModel> logger,
        MarketContext marketContext) : base(logger)
    {
        _marketContext = marketContext;

        ChangeKLineTypeCommand = new RelayCommand<string>(ChangeKLineTypeAsync);
        NavigateToAnalysisCommand = new RelayCommand(NavigateToAnalysisAsync);
        NavigateToTradingCommand = new RelayCommand(NavigateToTradingAsync);
        NavigateToAlertCommand = new RelayCommand(NavigateToAlertAsync);
        ToggleRightPanelCommand = new RelayCommand(ToggleRightPanel);
        ToggleFavoriteCommand = new AsyncRelayCommand(ToggleFavoriteAsync);

        SetRetryCommand(() => LoadAssetDataAsync(AssetCode));
    }

    partial void OnCurrentKLineTypeChanged(KLineType value)
    {
        OnPropertyChanged(nameof(IsMinuteSelected));
        OnPropertyChanged(nameof(IsDailySelected));
        OnPropertyChanged(nameof(IsWeeklySelected));
        OnPropertyChanged(nameof(IsMonthlySelected));

        if (!string.IsNullOrEmpty(AssetCode))
        {
            _ = LoadAssetDataAsync(AssetCode);
        }
    }

    protected override void OnBusyChanged() => OnPropertyChanged(nameof(IsRefreshing));


    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        if (!string.IsNullOrEmpty(AssetCode))
        {
            await SafeExecuteAsync(async () => await LoadAssetDataAsync(AssetCode), "刷新数据");
        }
    }

    /// <summary>
    /// 单区块重试。区块之间互不影响：某区块失败时只重载该区块，不重载整页。
    /// </summary>
    [RelayCommand]
    private async Task RetrySectionAsync(string? section)
    {
        if (string.IsNullOrEmpty(AssetCode) || string.IsNullOrEmpty(section))
            return;

        using var cancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = cancellationTokenSource.Token;

        switch (section)
        {
            case "Snapshot":
                await LoadSnapshotSectionAsync(AssetCode, cancellationToken);
                break;
            case "Indicator":
                await LoadIndicatorSectionAsync(AssetCode, cancellationToken);
                break;
            case "OrderBook":
                await LoadOrderBookSectionAsync(AssetCode, cancellationToken);
                break;
            case "TickTrade":
                await LoadTickTradeSectionAsync(AssetCode, cancellationToken);
                break;
            default:
                Logger?.LogWarning("未知的区块重试请求: {Section}", section);
                break;
        }
    }

    private void ToggleRightPanel()
    {
        IsRightPanelVisible = !IsRightPanelVisible;

        // 右栏折叠期间不请求盘口，展开时补一次，避免展示过期深度
        if (IsRightPanelVisible)
        {
            _ = RefreshOrderBookAsync(force: true);
        }
    }

    private void NavigateToAnalysisAsync()
    {
        if (string.IsNullOrEmpty(AssetCode))
            return;

        WeakReferenceMessenger.Default.Send(new NavigationMessage("Analysis", new AssetNavigationParameter(AssetCode, AssetName)));
    }

    private void NavigateToTradingAsync()
    {
        if (string.IsNullOrEmpty(AssetCode))
            return;

        WeakReferenceMessenger.Default.Send(new NavigationMessage("Trading", new AssetNavigationParameter(AssetCode, AssetName)));
    }

    private void NavigateToAlertAsync()
    {
        if (string.IsNullOrEmpty(AssetCode))
            return;

        WeakReferenceMessenger.Default.Send(new NavigationMessage("PriceAlert", new AssetNavigationParameter(AssetCode, AssetName)));
    }

    /// <summary>
    /// 将详情页资产代码归一化为收藏存储所用的 code（统一走 <see cref="FavoriteCodeNormalizer"/>），
    /// 并保留交易所/来源标记用于展示：A 股取 SH/SZ 前缀，虚拟币固定 Binance。
    /// </summary>
    private string NormalizeFavoriteCode(out string market)
    {
        var rawCode = AssetCode.Trim();
        var normalized = FavoriteCodeNormalizer.Normalize(rawCode, _marketContext.CurrentMarket);

        if (_marketContext.CurrentMarket == MarketType.Crypto)
        {
            market = "Binance";
        }
        else
        {
            // A 股：归一化剥离了前缀时还原交易所标记；收藏判定按 code + market_type，标记仅供行情映射
            market = normalized.Length < rawCode.Length
                ? rawCode[..2].ToUpperInvariant()
                : string.Empty;
        }

        return normalized;
    }

    /// <summary>按当前市场刷新收藏状态，失败静默（不影响详情页主流程）。</summary>
    private async Task RefreshFavoriteStateAsync()
    {
        if (string.IsNullOrEmpty(AssetCode))
        {
            IsFavorite = false;
            return;
        }

        try
        {
            var favoriteService = _marketContext.GetService<IFavoriteService>();
            var code = NormalizeFavoriteCode(out var market);
            IsFavorite = await favoriteService.IsFavoriteAsync(code, market);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "刷新资产 {AssetCode} 收藏状态失败", AssetCode);
        }
    }

    private async Task ToggleFavoriteAsync()
    {
        if (string.IsNullOrEmpty(AssetCode))
            return;

        try
        {
            var favoriteService = _marketContext.GetService<IFavoriteService>();
            var code = NormalizeFavoriteCode(out var market);

            if (IsFavorite)
            {
                await favoriteService.RemoveFavoriteAsync(code, market);
            }
            else
            {
                await favoriteService.AddFavoriteAsync(code, market);
            }

            // 以数据库真实状态回显，避免本地取反与持久化状态漂移
            await RefreshFavoriteStateAsync();
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "切换资产 {AssetCode} 收藏状态失败", AssetCode);
        }
    }

    private void ChangeKLineTypeAsync(string? type)
    {
        if (string.IsNullOrEmpty(type))
            return;

        var newKLineType = type.ToLower() switch
        {
            "minute" => KLineType.Minute15,
            "daily" => KLineType.Daily,
            "weekly" => KLineType.Weekly,
            "monthly" => KLineType.Monthly,
            _ => CurrentKLineType
        };

        if (newKLineType != CurrentKLineType)
        {
            CurrentKLineType = newKLineType;
        }
    }

    /// <summary>
    /// 并行加载详情页全部区块。仅 K 线决定页面整体状态，
    /// 行情速览 / 技术指标 / 盘口 / 逐笔各自失败时只降级自身。
    /// </summary>
    private async Task LoadAssetDataAsync(string assetCode)
    {
        if (string.IsNullOrEmpty(assetCode))
            return;

        // 取消上一次加载，避免并发加载；只取消不 Dispose——
        // 在飞操作仍持有旧令牌，立即 Dispose 会偶发 ObjectDisposedException
        _loadingCancellationTokenSource?.Cancel();
        _loadingCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _loadingCancellationTokenSource.Token;
        var generation = ++_pageLoadGeneration;

        IsBusy = true;

        try
        {
            var kLineTask = LoadKLineSectionAsync(assetCode, cancellationToken);
            var snapshotTask = LoadSnapshotSectionAsync(assetCode, cancellationToken);
            var indicatorTask = LoadIndicatorSectionAsync(assetCode, cancellationToken);
            var orderBookTask = LoadOrderBookSectionAsync(assetCode, cancellationToken);
            var tickTradeTask = LoadTickTradeSectionAsync(assetCode, cancellationToken);

            await LoadAsync(_ => kLineTask, "加载K线数据", cancellationToken);

            // 各区块自行捕获异常，此处仅兜底，防止某个区块的异常逃逸成未观察任务
            await Task.WhenAll(snapshotTask, indicatorTask, orderBookTask, tickTradeTask);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "加载资产 {AssetCode} 的行情区块时发生错误", assetCode);
        }
        finally
        {
            // 过期调用不得清除忙碌态，否则会提前关掉新加载的刷新提示
            if (generation == _pageLoadGeneration)
                IsBusy = false;
        }
    }

    private async Task<bool> LoadKLineSectionAsync(string assetCode, CancellationToken cancellationToken)
    {
        var klineService = _marketContext.GetService<IKLineService>();
        var attempt = ++_kLineAttempt;

        // IKLineService.GetKLineDataAsync 暂不支持 CancellationToken，
        // 仅能通过取消令牌在返回后丢弃过期结果
        var kLineDataList = await klineService.GetKLineDataAsync(assetCode, CurrentKLineType);

        cancellationToken.ThrowIfCancellationRequested();

        // 方法从 UI 线程启动且未脱离同步上下文，await 之后天然回到 UI 线程，
        // 无需再手动 InvokeAsync
        if (attempt != _kLineAttempt)
            return false;

        KLineData = new ObservableCollection<KLineData>(kLineDataList);
        CalculatePriceInfo(kLineDataList);

        return kLineDataList.Count > 0;
    }

    private async Task LoadSnapshotSectionAsync(string assetCode, CancellationToken cancellationToken)
    {
        var attempt = ++_snapshotAttempt;
        SnapshotState = SnapshotState with { State = LoadState.Loading };

        try
        {
            var snapshot = await _marketContext.GetService<IMarketSnapshotService>()
                .GetSnapshotAsync(assetCode, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var metrics = BuildMarketMetrics(snapshot);
            if (attempt != _snapshotAttempt)
                return;

            MarketMetrics = metrics;
            SnapshotState = SnapshotState with
            {
                State = snapshot is null ? LoadState.Empty : LoadState.Ready,
                HasData = snapshot is not null
            };
        }
        catch (OperationCanceledException)
        {
            if (attempt != _snapshotAttempt)
                return;

            SnapshotState = SnapshotState with { State = LoadState.Idle };
        }
        catch (Exception ex)
        {
            if (attempt != _snapshotAttempt)
                return;

            MarketMetrics = BuildMarketMetrics(null);
            SnapshotState = SnapshotState with
            {
                State = LoadState.Failed,
                HasData = false,
                FailedText = "行情速览加载失败"
            };
            Logger?.LogWarning(ex, "加载资产 {AssetCode} 的行情速览失败", assetCode);
        }
    }

    private async Task LoadIndicatorSectionAsync(string assetCode, CancellationToken cancellationToken)
    {
        var attempt = ++_indicatorAttempt;
        IndicatorState = IndicatorState with { State = LoadState.Loading };

        try
        {
            // 技术指标固定按日线口径计算：周期切换只影响主图，不重复拉取指标数据
            var snapshot = await _marketContext.GetService<ITechnicalIndicatorService>()
                .GetSnapshotAsync(assetCode, KLineType.Daily, IndicatorBarCount, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var groups = BuildIndicatorGroups(snapshot);
            if (attempt != _indicatorAttempt)
                return;

            IndicatorGroups = groups;
            IndicatorDateText = groups.Count > 0 ? snapshot.Date : string.Empty;
            IndicatorState = IndicatorState with
            {
                State = groups.Count > 0 ? LoadState.Ready : LoadState.Empty,
                HasData = groups.Count > 0
            };
        }
        catch (OperationCanceledException)
        {
            if (attempt != _indicatorAttempt)
                return;

            IndicatorState = IndicatorState with { State = LoadState.Idle };
        }
        catch (Exception ex)
        {
            if (attempt != _indicatorAttempt)
                return;

            IndicatorGroups = [];
            IndicatorDateText = string.Empty;
            IndicatorState = IndicatorState with
            {
                State = LoadState.Failed,
                HasData = false,
                FailedText = "技术指标加载失败"
            };
            Logger?.LogWarning(ex, "计算资产 {AssetCode} 的技术指标失败", assetCode);
        }
    }

    private async Task LoadOrderBookSectionAsync(string assetCode, CancellationToken cancellationToken)
    {
        if (!SupportsOrderBook)
        {
            OrderBookState = new SectionState { State = LoadState.Idle };
            return;
        }

        var attempt = ++_orderBookAttempt;
        OrderBookState = OrderBookState with { State = LoadState.Loading };

        try
        {
            var snapshot = await _marketContext.GetService<IOrderBookService>()
                .GetOrderBookAsync(assetCode, OrderBookDepth, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var hasLevels = snapshot is not null && (snapshot.Bids.Count > 0 || snapshot.Asks.Count > 0);
            if (attempt != _orderBookAttempt)
                return;

            if (hasLevels)
            {
                ApplyOrderBook(snapshot!);
            }
            else
            {
                ClearOrderBook();
            }

            OrderBookState = OrderBookState with
            {
                State = hasLevels ? LoadState.Ready : LoadState.Empty,
                HasData = hasLevels
            };
        }
        catch (OperationCanceledException)
        {
            if (attempt != _orderBookAttempt)
                return;

            OrderBookState = OrderBookState with { State = LoadState.Idle };
        }
        catch (Exception ex)
        {
            if (attempt != _orderBookAttempt)
                return;

            ClearOrderBook();
            OrderBookState = OrderBookState with
            {
                State = LoadState.Failed,
                HasData = false,
                FailedText = "盘口数据加载失败"
            };
            Logger?.LogWarning(ex, "加载资产 {AssetCode} 的盘口数据失败", assetCode);
        }
    }

    private async Task LoadTickTradeSectionAsync(string assetCode, CancellationToken cancellationToken)
    {
        if (!SupportsTickTrades)
        {
            TickTradeState = new SectionState { State = LoadState.Idle };
            return;
        }

        var attempt = ++_tickTradeAttempt;
        TickTradeState = TickTradeState with { State = LoadState.Loading };

        try
        {
            var trades = await _marketContext.GetService<ITickTradeService>()
                .GetRecentTradesAsync(assetCode, TickTradeLimit, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            var items = new ObservableCollection<TickTradeItem>(trades.Select(CreateTickTradeItem));
            if (attempt != _tickTradeAttempt)
                return;

            TickTrades = items;
            TickTradeState = TickTradeState with
            {
                State = items.Count > 0 ? LoadState.Ready : LoadState.Empty,
                HasData = items.Count > 0
            };
        }
        catch (OperationCanceledException)
        {
            if (attempt != _tickTradeAttempt)
                return;

            TickTradeState = TickTradeState with { State = LoadState.Idle };
        }
        catch (Exception ex)
        {
            if (attempt != _tickTradeAttempt)
                return;

            TickTrades = [];
            TickTradeState = TickTradeState with
            {
                State = LoadState.Failed,
                HasData = false,
                FailedText = "逐笔成交加载失败"
            };
            Logger?.LogWarning(ex, "加载资产 {AssetCode} 的逐笔成交失败", assetCode);
        }
    }

    /// <summary>右栏展开且距上次刷新超过节流间隔时才请求盘口；force 用于展开右栏后的补拉。</summary>
    private async Task RefreshOrderBookAsync(bool force)
    {
        if (!force && (DateTime.UtcNow - _lastOrderBookRefreshAt) < OrderBookRefreshInterval)
            return;

        if (_isOrderBookRefreshing)
            return;

        _lastOrderBookRefreshAt = DateTime.UtcNow;

        if (string.IsNullOrEmpty(AssetCode))
            return;

        _isOrderBookRefreshing = true;
        try
        {
            var cancellationToken = _loadingCancellationTokenSource?.Token ?? CancellationToken.None;
            await LoadOrderBookSectionAsync(AssetCode, cancellationToken);
            await LoadTickTradeSectionAsync(AssetCode, cancellationToken);
        }
        finally
        {
            _isOrderBookRefreshing = false;
        }
    }

    private void ApplyOrderBook(OrderBookSnapshot snapshot)
    {
        var maxQuantity = snapshot.Bids
            .Concat(snapshot.Asks)
            .Select(level => level.Quantity)
            .DefaultIfEmpty(0m)
            .Max();

        // 卖盘按价格降序展示（卖五 → 卖一），使卖一紧邻中间价
        Asks = new ObservableCollection<OrderBookLevelItem>(
            snapshot.Asks
                .Select((level, index) => CreateOrderBookLevelItem(level, $"卖{index + 1}", maxQuantity, isBid: false))
                .Reverse());

        Bids = new ObservableCollection<OrderBookLevelItem>(
            snapshot.Bids.Select((level, index) => CreateOrderBookLevelItem(level, $"买{index + 1}", maxQuantity, isBid: true)));

        BidQuantityPercent = (double)(snapshot.BidQuantityPercent ?? 0);
        BidQuantityPercentText = snapshot.BidQuantityPercent.HasValue
            ? $"买盘 {snapshot.BidQuantityPercent.Value:F1}%"
            : string.Empty;

        OrderBookSummaryText = snapshot.Spread.HasValue
            ? $"价差 {PriceFormatter.Format(snapshot.Spread.Value)}"
            : string.Empty;
    }

    private void ClearOrderBook()
    {
        Asks = [];
        Bids = [];
        BidQuantityPercent = 0;
        BidQuantityPercentText = string.Empty;
        OrderBookSummaryText = string.Empty;
    }

    private static OrderBookLevelItem CreateOrderBookLevelItem(
        OrderBookLevel level,
        string levelLabel,
        decimal maxQuantity,
        bool isBid)
    {
        var depthPercent = maxQuantity > 0 ? (double)(level.Quantity / maxQuantity * 100) : 0;

        return new OrderBookLevelItem(
            LevelLabel: levelLabel,
            PriceText: PriceFormatter.Format(level.Price),
            QuantityText: MarketValueFormatter.FormatQuantity(level.Quantity),
            AmountText: MarketValueFormatter.FormatAmount(level.Amount),
            DepthPercent: depthPercent,
            // 正数取涨色（买盘），负数取跌色（卖盘）
            ToneValue: isBid ? 1m : -1m);
    }

    private static TickTradeItem CreateTickTradeItem(TickTrade trade) =>
        new(
            TimeText: trade.Timestamp.ToString("HH:mm:ss"),
            PriceText: PriceFormatter.Format(trade.Price),
            QuantityText: MarketValueFormatter.FormatQuantity(trade.Quantity),
            AmountText: MarketValueFormatter.FormatAmount(trade.Amount),
            ToneValue: trade.IsActiveBuy ? 1m : -1m);

    /// <summary>构建行情速览指标。数据源未提供的项保留占位（Value 为空）由界面隐藏，保证顺序稳定。</summary>
    private static ObservableCollection<MarketMetricItem> BuildMarketMetrics(MarketSnapshot? snapshot) =>
        new(
        [
            new MarketMetricItem("今开", MarketValueFormatter.FormatPrice(snapshot?.Open)),
            new MarketMetricItem("最高", MarketValueFormatter.FormatPrice(snapshot?.High)),
            new MarketMetricItem("最低", MarketValueFormatter.FormatPrice(snapshot?.Low)),
            new MarketMetricItem("昨收", MarketValueFormatter.FormatPrice(snapshot?.PreClose)),
            new MarketMetricItem("成交量", MarketValueFormatter.FormatQuantity(snapshot?.Volume)),
            new MarketMetricItem("成交额", MarketValueFormatter.FormatAmount(snapshot?.Amount)),
            new MarketMetricItem("换手率", MarketValueFormatter.FormatPercent(snapshot?.TurnoverRate)),
            new MarketMetricItem("振幅", MarketValueFormatter.FormatPercent(snapshot?.Amplitude)),
            new MarketMetricItem("量比", MarketValueFormatter.FormatRatio(snapshot?.VolumeRatio)),
            new MarketMetricItem("市盈率", MarketValueFormatter.FormatRatio(snapshot?.Pe)),
            new MarketMetricItem("市净率", MarketValueFormatter.FormatRatio(snapshot?.Pb))
        ]);

    private static ObservableCollection<IndicatorGroupItem> BuildIndicatorGroups(TechnicalIndicatorSnapshot snapshot)
    {
        var groups = new ObservableCollection<IndicatorGroupItem>();

        AddIndicatorGroup(
            groups,
            "均线",
            [.. DisplayMovingAveragePeriods.Select(period => ($"MA{period}", snapshot.GetMovingAverage(period), true))]);

        AddIndicatorGroup(
            groups,
            "MACD",
            ("DIF", snapshot.MacdDiff, false),
            ("DEA", snapshot.MacdDea, false),
            ("MACD", snapshot.MacdBar, false));

        AddIndicatorGroup(
            groups,
            "BOLL",
            ("上轨", snapshot.BollUpper, true),
            ("中轨", snapshot.BollMiddle, true),
            ("下轨", snapshot.BollLower, true));

        AddIndicatorGroup(
            groups,
            "KDJ",
            ("K", snapshot.KdjK, false),
            ("D", snapshot.KdjD, false),
            ("J", snapshot.KdjJ, false));

        return groups;
    }

    /// <summary>追加一个指标分组；全部条目都无值时跳过，避免出现空分组标题。</summary>
    private static void AddIndicatorGroup(
        ObservableCollection<IndicatorGroupItem> groups,
        string title,
        params (string Label, decimal? Value, bool AsPrice)[] entries)
    {
        var items = entries
            .Where(entry => entry.Value.HasValue)
            .Select(entry => new IndicatorItem(
                entry.Label,
                entry.AsPrice
                    ? MarketValueFormatter.FormatPrice(entry.Value)
                    : MarketValueFormatter.FormatRatio(entry.Value)))
            .ToList();

        if (items.Count > 0)
        {
            groups.Add(new IndicatorGroupItem(title, new ObservableCollection<IndicatorItem>(items)));
        }
    }

    private void CalculatePriceInfo(List<KLineData> data)
    {
        if (data.Count == 0)
            return;

        var latestData = data.Last();
        CurrentPrice = latestData.Close;

        if (data.Count > 1)
        {
            var previousData = data[data.Count - 2];
            PriceChange = latestData.Close - previousData.Close;
            PriceChangePercent = previousData.Close != 0 ?
                Math.Round((latestData.Close - previousData.Close) / previousData.Close * 100, 2) : 0;
        }
        else
        {
            PriceChange = 0;
            PriceChangePercent = 0;
        }
    }

    public void OnNavigatedTo(AssetNavigationParameter parameter, bool isReactivation)
    {
        if (string.IsNullOrEmpty(parameter.Code))
            return;

        AssetName = !string.IsNullOrEmpty(parameter.Name) ? parameter.Name : parameter.Code;
        AssetCode = parameter.Code;

        if (parameter.CurrentPrice.HasValue)
        {
            CurrentPrice = parameter.CurrentPrice.Value;

            if (parameter.ChangePercent.HasValue)
            {
                PriceChangePercent = parameter.ChangePercent.Value;
                PriceChange = CurrentPrice * PriceChangePercent / 100;
            }
        }
        else
        {
            CurrentPrice = 0;
            PriceChangePercent = 0;
            PriceChange = 0;
        }

        if (!isReactivation)
        {
            // 先复位再加载，使 LoadAsync 识别为首次加载并展示骨架屏；
            // 重新激活（GoBack）时保留已有内容，不能复位状态，否则骨架屏会一直停留
            ResetLoadState();
            ResetSectionStates();

            // 异步加载完整数据（不阻塞导航）。
            // 不要包 Task.Run：OnNavigatedTo 已在 UI 线程，LoadAssetDataAsync 第一个 await
            // 之后自动回到 UI 线程，包 Task.Run 会把 IsBusy/HasError 等绑定属性丢到线程池线程写入，
            // 触发 Avalonia 跨线程异常
            _ = LoadAssetDataAsync(parameter.Code);

            // 支持实时推送的市场订阅实时价格
            // 优先使用参数携带的 MarketType，避免导航期间切换市场导致的竞态
            var effectiveMarket = parameter.MarketType ?? _marketContext.CurrentMarket;
            if (_marketContext.GetService<IMarketCapability>(effectiveMarket).SupportsRealtime)
            {
                BindRealtimeQuoteService(_marketContext.GetService<IRealtimeQuoteService>(effectiveMarket));
                // 订阅前先整体替换，防止重复
                _ = _quoteService!.SubscribeAsync(RealtimeQuoteSubscriberKeys.AssetDetail, [parameter.Code]);
            }
            else
            {
                DetachRealtimeQuoteService();
            }
        }

        // 刷新收藏状态（首次进入与 GoBack 重新激活均需同步）
        _ = RefreshFavoriteStateAsync();
    }

    /// <summary>切换标的时清空上一标的的区块状态与数据，避免新标的页面闪现旧内容。</summary>
    private void ResetSectionStates()
    {
        SnapshotState = new SectionState();
        IndicatorState = new SectionState();
        OrderBookState = new SectionState();
        TickTradeState = new SectionState();

        MarketMetrics = BuildMarketMetrics(null);
        IndicatorGroups = [];
        IndicatorDateText = string.Empty;
        ClearOrderBook();
        TickTrades = [];

        _lastOrderBookRefreshAt = DateTime.MinValue;
        _isOrderBookRefreshing = false;
    }

    /// <summary>
    /// 绑定实时行情服务事件。导航到不同市场的资产时，先解除旧服务的事件与订阅再绑定新服务。
    /// </summary>
    private void BindRealtimeQuoteService(IRealtimeQuoteService service)
    {
        if (ReferenceEquals(service, _quoteService))
            return;

        DetachRealtimeQuoteService();
        _quoteService = service;
        _quoteService.PriceUpdated += OnDetailPriceUpdated;
    }

    /// <summary>
    /// 解除当前实时行情服务的事件与订阅。用于切到无实时推送的市场或页面离开时清理。
    /// </summary>
    private void DetachRealtimeQuoteService()
    {
        if (_quoteService == null)
            return;

        _quoteService.PriceUpdated -= OnDetailPriceUpdated;
        _ = _quoteService.UnsubscribeAllAsync(RealtimeQuoteSubscriberKeys.AssetDetail);
        _quoteService = null;
    }

    private void OnDetailPriceUpdated(string code, decimal lastPrice, decimal changePercent)
    {
        if (!AssetCode.Equals(code, StringComparison.OrdinalIgnoreCase))
            return;

        // 回调在后台线程，所有绑定属性写入与盘口刷新都必须回到 UI 线程
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentPrice = lastPrice;
            PriceChangePercent = changePercent;
            PriceChange = CurrentPrice * PriceChangePercent / 100;

            // 右栏折叠时不做无谓请求；展开时按节流间隔跟随刷新盘口与逐笔
            if (IsRightPanelVisible && SupportsOrderBook)
            {
                _ = RefreshOrderBookAsync(force: false);
            }
        });
    }

    public void OnNavigatedFrom()
    {
        _loadingCancellationTokenSource?.Cancel();
        _quoteService?.PriceUpdated -= OnDetailPriceUpdated;
    }

    public void Dispose()
    {
        _loadingCancellationTokenSource?.Cancel();
        DetachRealtimeQuoteService();
        GC.SuppressFinalize(this);
    }
}
