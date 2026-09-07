using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Applications.Assets;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;


namespace MarketAssistant.ViewModels;

public partial class AssetPageViewModel : ViewModelBase, INavigationAware<AssetNavigationParameter>, IDisposable
{
    public override string Title => "资产详情";

    private readonly MarketContext _marketContext;
    private CancellationTokenSource? _loadingCancellationTokenSource;

    /// <summary>当前绑定事件的实时行情服务。导航参数携带的市场与当前绑定不一致时重新绑定。</summary>
    private IRealtimeQuoteService? _quoteService;

    [ObservableProperty]
    private KLineType _currentKLineType = KLineType.Daily;

    [ObservableProperty]
    private string _assetCode = "";

    [ObservableProperty]
    private string _assetName = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private ObservableCollection<KLineData> _kLineData = new();

    [ObservableProperty]
    private decimal _currentPrice;

    [ObservableProperty]
    private decimal _priceChangePercent;

    [ObservableProperty]
    private decimal _priceChange;

    /// <summary>当前价展示文本（按量级格式化，适配低价币）</summary>
    public string CurrentPriceText => PriceFormatter.Format(CurrentPrice);

    /// <summary>涨跌额展示文本（按量级格式化，适配低价币）</summary>
    public string PriceChangeText => PriceFormatter.Format(PriceChange);

    partial void OnCurrentPriceChanged(decimal value) => OnPropertyChanged(nameof(CurrentPriceText));

    partial void OnPriceChangeChanged(decimal value) => OnPropertyChanged(nameof(PriceChangeText));

    public bool IsMinuteSelected => CurrentKLineType == KLineType.Minute15;
    public bool IsDailySelected => CurrentKLineType == KLineType.Daily;
    public bool IsWeeklySelected => CurrentKLineType == KLineType.Weekly;
    public bool IsMonthlySelected => CurrentKLineType == KLineType.Monthly;

    public IRelayCommand<string> ChangeKLineTypeCommand { get; private set; }
    public IRelayCommand NavigateToAnalysisCommand { get; private set; }

    public AssetPageViewModel(
        ILogger<AssetPageViewModel> logger,
        MarketContext marketContext) : base(logger)
    {
        _marketContext = marketContext;

        ChangeKLineTypeCommand = new RelayCommand<string>(ChangeKLineTypeAsync);
        NavigateToAnalysisCommand = new RelayCommand(NavigateToAnalysisAsync);
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

    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        if (!string.IsNullOrEmpty(AssetCode))
        {
            await SafeExecuteAsync(async () => await LoadAssetDataAsync(AssetCode), "刷新数据");
        }
    }

    private void NavigateToAnalysisAsync()
    {
        if (string.IsNullOrEmpty(AssetCode))
            return;

        WeakReferenceMessenger.Default.Send(new NavigationMessage("Analysis", new AssetNavigationParameter(AssetCode, AssetName)));
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

    private async Task LoadAssetDataAsync(string assetCode)
    {
        if (string.IsNullOrEmpty(assetCode))
            return;

        // 取消上一次加载，避免并发加载；只取消不 Dispose——
        // 在飞操作仍持有旧令牌，立即 Dispose 会偶发 ObjectDisposedException
        _loadingCancellationTokenSource?.Cancel();
        _loadingCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _loadingCancellationTokenSource.Token;

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var klineService = _marketContext.GetService<IKLineService>();
            // IKLineService.GetKLineDataAsync 暂不支持 CancellationToken，
            // 仅能通过取消令牌在返回后丢弃过期结果
            var kLineDataList = await klineService.GetKLineDataAsync(assetCode, CurrentKLineType);

            cancellationToken.ThrowIfCancellationRequested();

            // 方法从 UI 线程启动且未脱离同步上下文，await 之后天然回到 UI 线程，
            // 无需再手动 InvokeAsync
            KLineData = new ObservableCollection<KLineData>(kLineDataList);
            CalculatePriceInfo(kLineDataList);
        }
        catch (OperationCanceledException)
        {
            Logger?.LogInformation("资产 {AssetCode} 的K线数据加载已取消", assetCode);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, "加载K线数据");
            Logger?.LogError(ex, "加载资产 {AssetCode} 的K线数据时发生错误", assetCode);
        }
        finally
        {
            IsBusy = false;
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
        if (!string.IsNullOrEmpty(parameter.Code))
        {
            // 1. 立即设置加载状态，确保骨架屏显示
            IsBusy = true;
            HasError = false;

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

            // 2. 异步加载完整数据（不阻塞导航）。
            // 不要包 Task.Run：OnNavigatedTo 已在 UI 线程，LoadAssetDataAsync 第一个 await
            // 之后自动回到 UI 线程，包 Task.Run 会把 IsBusy/HasError 等绑定属性丢到线程池线程写入，
            // 触发 Avalonia 跨线程异常
            if (!isReactivation)
            {
                _ = LoadAssetDataAsync(parameter.Code);

                // 3. 支持实时推送的市场订阅实时价格
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
        }
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

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentPrice = lastPrice;
            PriceChangePercent = changePercent;
            PriceChange = CurrentPrice * PriceChangePercent / 100;
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



