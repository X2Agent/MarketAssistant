using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

[QueryProperty(nameof(StockCode), "code")]
public partial class StockViewModel : ViewModelBase
{
    private readonly StockKLineService _stockKLineService;
    private CancellationTokenSource? _loadingCancellationTokenSource;

    [ObservableProperty]
    private KLineType _currentKLineType = KLineType.Daily;

    [ObservableProperty]
    private string _stockCode = "";

    [ObservableProperty]
    private string _stockName = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    private bool _hasError;

    [ObservableProperty]
    private StockKLineDataSet? _kLineDataSet;

    [ObservableProperty]
    private ObservableCollection<StockKLineData> _kLineData = new();

    [ObservableProperty]
    private decimal _currentPrice;

    [ObservableProperty]
    private decimal _priceChangePercent;

    [ObservableProperty]
    private decimal _priceChange;

    // 计算属性用于UI绑定
    public bool IsMinuteSelected => CurrentKLineType == KLineType.Minute15;
    public bool IsDailySelected => CurrentKLineType == KLineType.Daily;
    public bool IsWeeklySelected => CurrentKLineType == KLineType.Weekly;
    public bool IsMonthlySelected => CurrentKLineType == KLineType.Monthly;

    public IRelayCommand RefreshDataCommand { get; private set; }
    public IRelayCommand<string> ChangeKLineTypeCommand { get; private set; }
    public IRelayCommand NavigateToAnalysisCommand { get; private set; }

    public StockViewModel(
        ILogger<StockViewModel> logger,
        StockKLineService stockKLineService) : base(logger)
    {
        _stockKLineService = stockKLineService;

        RefreshDataCommand = new RelayCommand(RefreshDataAsync);
        ChangeKLineTypeCommand = new RelayCommand<string>(ChangeKLineTypeAsync);
        NavigateToAnalysisCommand = new RelayCommand(NavigateToAnalysisAsync);
    }

    /// <summary>
    /// 当股票代码变化时自动加载数据
    /// </summary>
    partial void OnStockCodeChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _ = LoadStockDataAsync(value);
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
        
        if (!string.IsNullOrEmpty(StockCode))
        {
            _ = LoadStockDataAsync(StockCode);
        }
    }

    /// <summary>
    /// 刷新股票数据
    /// </summary>
    private async void RefreshDataAsync()
    {
        if (!string.IsNullOrEmpty(StockCode))
        {
            await LoadStockDataAsync(StockCode);
        }
    }

    /// <summary>
    /// 导航到股票分析页面
    /// </summary>
    private async void NavigateToAnalysisAsync()
    {
        if (string.IsNullOrEmpty(StockCode))
            return;

        await SafeExecuteAsync(async () =>
        {
            await Shell.Current.GoToAsync("analysis", new Dictionary<string, object>
            {
                { "code", StockCode }
            });
        }, "导航到股票分析页");
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
    /// 加载股票K线数据
    /// </summary>
    private async Task LoadStockDataAsync(string stockCode)
    {
        if (string.IsNullOrEmpty(stockCode))
            return;

        // 取消之前的加载操作
        _loadingCancellationTokenSource?.Cancel();
        _loadingCancellationTokenSource = new CancellationTokenSource();
        var cancellationToken = _loadingCancellationTokenSource.Token;

        await SafeExecuteAsync(async () =>
        {
            var kLineDataSet = CurrentKLineType switch
            {
                KLineType.Minute15 => await _stockKLineService.GetMinuteKLineDataAsync(stockCode, "15"),
                KLineType.Weekly => await _stockKLineService.GetWeeklyKLineDataAsync(stockCode),
                KLineType.Monthly => await _stockKLineService.GetMonthlyKLineDataAsync(stockCode),
                _ => await _stockKLineService.GetDailyKLineDataAsync(stockCode)
            };

            // 检查是否已被取消
            cancellationToken.ThrowIfCancellationRequested();

            KLineDataSet = kLineDataSet;
            KLineData = new ObservableCollection<StockKLineData>(kLineDataSet.Data);

            // 计算价格信息
            CalculatePriceInfo(kLineDataSet.Data);

        }, $"加载股票 {stockCode} 的K线数据");
    }

    /// <summary>
    /// 计算价格相关信息
    /// </summary>
    private void CalculatePriceInfo(List<StockKLineData> data)
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
}
