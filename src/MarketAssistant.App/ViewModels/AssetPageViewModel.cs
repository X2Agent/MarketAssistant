using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Charts;
using MarketAssistant.Applications.Charts.Models;
using MarketAssistant.Infrastructure;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.DataProviders;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;


namespace MarketAssistant.ViewModels;

/// <summary>
/// 资产详情页ViewModel
/// </summary>
public partial class AssetPageViewModel : ViewModelBase, INavigationAware<AssetNavigationParameter>, IDisposable
{
    public override string Title => "资产详情";

    private readonly Func<MarketType, IKLineService> _klineServiceResolver;
    private readonly MarketContext _marketContext;
    private readonly BinanceWebSocketService _wsService;
    private CancellationTokenSource? _loadingCancellationTokenSource;

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

    /// <summary>
    /// 计算属性用于UI绑定
    /// </summary>
    public bool IsMinuteSelected => CurrentKLineType == KLineType.Minute15;
    public bool IsDailySelected => CurrentKLineType == KLineType.Daily;
    public bool IsWeeklySelected => CurrentKLineType == KLineType.Weekly;
    public bool IsMonthlySelected => CurrentKLineType == KLineType.Monthly;

    public IRelayCommand<string> ChangeKLineTypeCommand { get; private set; }
    public IRelayCommand NavigateToAnalysisCommand { get; private set; }

    public AssetPageViewModel(
        ILogger<AssetPageViewModel> logger,
        Func<MarketType, IKLineService> klineServiceResolver,
        MarketContext marketContext,
        BinanceWebSocketService wsService) : base(logger)
    {
        _klineServiceResolver = klineServiceResolver ?? throw new ArgumentNullException(nameof(klineServiceResolver));
        _marketContext = marketContext;
        _wsService = wsService;

        ChangeKLineTypeCommand = new RelayCommand<string>(ChangeKLineTypeAsync);
        NavigateToAnalysisCommand = new RelayCommand(NavigateToAnalysisAsync);
    }

    /// <summary>
    /// 设置资产代码（异步加载数据，避免阻塞UI）
    /// </summary>
    private void SetAssetCode(string code)
    {
        AssetCode = code;
        if (!string.IsNullOrEmpty(code))
        {
            _ = LoadAssetDataAsync(code);
        }
    }

    /// <summary>
    /// 当K线类型变化时通知相关UI属性
    /// </summary>
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

    /// <summary>
    /// 刷新资产数据
    /// </summary>
    [RelayCommand]
    private async Task RefreshDataAsync()
    {
        if (!string.IsNullOrEmpty(AssetCode))
        {
            await SafeExecuteAsync(async () => await LoadAssetDataAsync(AssetCode), "刷新数据");
        }
    }

    /// <summary>
    /// 导航到资产分析页面
    /// </summary>
    private void NavigateToAnalysisAsync()
    {
        if (string.IsNullOrEmpty(AssetCode))
            return;

        WeakReferenceMessenger.Default.Send(new NavigationMessage("Analysis", new AssetNavigationParameter(AssetCode, AssetName)));
    }

    /// <summary>
    /// 改变K线类型
    /// </summary>
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
    /// 加载资产K线数据
    /// </summary>
    private async Task LoadAssetDataAsync(string assetCode)
    {
        if (string.IsNullOrEmpty(assetCode))
            return;

        _loadingCancellationTokenSource?.Cancel();
        _loadingCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _loadingCancellationTokenSource.Token;

        IsBusy = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            var klineService = _klineServiceResolver(_marketContext.CurrentMarket);
            var kLineDataList = await klineService.GetKLineDataAsync(assetCode, CurrentKLineType);

            cancellationToken.ThrowIfCancellationRequested();

            // 在 UI 线程上更新 ObservableCollection，避免后台线程修改绑定属性
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                KLineData = new ObservableCollection<KLineData>(kLineDataList);
                CalculatePriceInfo(kLineDataList);
            });
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

    /// <summary>
    /// 计算价格相关信息
    /// </summary>
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

            // 2. 设置基本信息（立即显示）
            AssetName = !string.IsNullOrEmpty(parameter.Name) ? parameter.Name : parameter.Code;
            AssetCode = parameter.Code;

            // 3. 如果导航参数中包含价格信息，立即显示
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
                // 清空旧数据
                CurrentPrice = 0;
                PriceChangePercent = 0;
                PriceChange = 0;
            }

            // 4. 在后台线程加载完整数据（不阻塞导航）
            // GoBack 重新激活时不重复加载，避免重复订阅 WebSocket 和重复请求
            if (!isReactivation)
            {
                _ = Task.Run(async () => await LoadAssetDataAsync(parameter.Code));

                // 5. 虚拟币市场订阅 WebSocket 实时价格
                // 优先使用参数携带的 MarketType，避免导航期间切换市场导致的竞态
                var effectiveMarket = parameter.MarketType ?? _marketContext.CurrentMarket;
                if (effectiveMarket == MarketType.Crypto)
                {
                    // 订阅前先取消订阅，防止重复
                    _wsService.PriceUpdated -= OnDetailPriceUpdated;
                    _wsService.PriceUpdated += OnDetailPriceUpdated;
                    _ = _wsService.SubscribeAsync(WebSocketSubscriberKeys.AssetDetail, [ToBinanceFormat(parameter.Code)]);
                }
            }
        }
    }

    private void OnDetailPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        if (!ToBinanceFormat(AssetCode).Equals(symbol, StringComparison.OrdinalIgnoreCase))
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
        _wsService.PriceUpdated -= OnDetailPriceUpdated;
    }

    public void Dispose()
    {
        _loadingCancellationTokenSource?.Cancel();
        _loadingCancellationTokenSource?.Dispose();
        _wsService.PriceUpdated -= OnDetailPriceUpdated;
        _ = _wsService.UnsubscribeAllAsync(WebSocketSubscriberKeys.AssetDetail);
        GC.SuppressFinalize(this);
    }
}



