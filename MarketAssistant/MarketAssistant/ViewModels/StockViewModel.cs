using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels
{
    [QueryProperty(nameof(StockCode), "code")]
    public class StockViewModel : ViewModelBase
    {
        private readonly StockKLineService _stockKLineService;

        public enum KLineType
        {
            Minute5,
            Minute15,
            Daily,
            Weekly,
            Monthly
        }

        private KLineType _currentKLineType = KLineType.Daily;
        public KLineType CurrentKLineType
        {
            get => _currentKLineType;
            set => SetProperty(ref _currentKLineType, value);
        }

        public bool IsMinuteSelected => CurrentKLineType == KLineType.Minute15;
        public bool IsDailySelected => CurrentKLineType == KLineType.Daily;
        public bool IsWeeklySelected => CurrentKLineType == KLineType.Weekly;
        public bool IsMonthlySelected => CurrentKLineType == KLineType.Monthly;

        private string _stockCode = "";
        public string StockCode
        {
            get => _stockCode;
            set
            {
                if (SetProperty(ref _stockCode, value))
                {
                    _ = LoadStockDataAsync(value);
                }
            }
        }

        private string _stockName = string.Empty;
        public string StockName
        {
            get => _stockName;
            set => SetProperty(ref _stockName, value);
        }

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _errorMessage;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _hasError;
        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        private StockKLineDataSet _kLineDataSet;
        public StockKLineDataSet KLineDataSet
        {
            get => _kLineDataSet;
            set => SetProperty(ref _kLineDataSet, value);
        }

        private ObservableCollection<StockKLineData> _kLineData = new ObservableCollection<StockKLineData>();
        public ObservableCollection<StockKLineData> KLineData
        {
            get => _kLineData;
            set => SetProperty(ref _kLineData, value);
        }

        private decimal _currentPrice;
        public decimal CurrentPrice
        {
            get => _currentPrice;
            set => SetProperty(ref _currentPrice, value);
        }

        private decimal _priceChangePercent;
        public decimal PriceChangePercent
        {
            get => _priceChangePercent;
            set => SetProperty(ref _priceChangePercent, value);
        }

        private decimal _priceChange;
        public decimal PriceChange
        {
            get => _priceChange;
            set => SetProperty(ref _priceChange, value);
        }

        public IRelayCommand RefreshDataCommand { get; private set; }
        public IRelayCommand<string> ChangeKLineTypeCommand { get; private set; }

        public StockViewModel(
            ILogger<StockViewModel> logger,
            StockKLineService stockKLineService) : base(logger)
        {
            _stockKLineService = stockKLineService;
            _kLineDataSet = new StockKLineDataSet();

            RefreshDataCommand = new RelayCommand(async () => await LoadStockDataAsync(StockCode));
            ChangeKLineTypeCommand = new RelayCommand<string>(async (type) => await ChangeKLineTypeAsync(type));
        }

        private async Task ChangeKLineTypeAsync(string? type)
        {
            if (string.IsNullOrEmpty(type) || string.IsNullOrEmpty(StockCode))
                return;

            KLineType previousType = CurrentKLineType;

            switch (type.ToLower())
            {
                case "minute":
                    CurrentKLineType = KLineType.Minute15;
                    break;
                case "daily":
                    CurrentKLineType = KLineType.Daily;
                    break;
                case "weekly":
                    CurrentKLineType = KLineType.Weekly;
                    break;
                case "monthly":
                    CurrentKLineType = KLineType.Monthly;
                    break;
                default:
                    return;
            }

            if (previousType != CurrentKLineType)
            {
                OnPropertyChanged(nameof(CurrentKLineType));
                OnPropertyChanged(nameof(IsMinuteSelected));
                OnPropertyChanged(nameof(IsDailySelected));
                OnPropertyChanged(nameof(IsWeeklySelected));
                OnPropertyChanged(nameof(IsMonthlySelected));
                await LoadStockDataAsync(StockCode);
            }
        }

        private async Task LoadStockDataAsync(string stockCode)
        {
            if (string.IsNullOrEmpty(stockCode))
                return;

            try
            {
                IsLoading = true;
                HasError = false;
                ErrorMessage = string.Empty;

                var kLineDataSet = CurrentKLineType switch
                {
                    KLineType.Minute15 => await _stockKLineService.GetMinuteKLineDataAsync(stockCode, "15"),
                    KLineType.Weekly => await _stockKLineService.GetWeeklyKLineDataAsync(stockCode),
                    KLineType.Monthly => await _stockKLineService.GetMonthlyKLineDataAsync(stockCode),
                    _ => await _stockKLineService.GetDailyKLineDataAsync(stockCode)
                };

                KLineDataSet = kLineDataSet;
                KLineData = new ObservableCollection<StockKLineData>(kLineDataSet.Data);
                StockName = kLineDataSet.Name;

                if (kLineDataSet.Data.Count > 0)
                {
                    var latestData = kLineDataSet.Data.Last();
                    var previousData = kLineDataSet.Data.Count > 1 ? kLineDataSet.Data[kLineDataSet.Data.Count - 2] : null;

                    CurrentPrice = latestData.Close;

                    if (previousData != null)
                    {
                        PriceChange = latestData.Close - previousData.Close;
                        PriceChangePercent = previousData.Close != 0 ?
                            Math.Round((latestData.Close - previousData.Close) / previousData.Close * 100, 2) : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                HasError = true;
                ErrorMessage = $"加载K线数据失败: {ex.Message}";
                Console.WriteLine($"加载K线数据异常: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }
    }
}
