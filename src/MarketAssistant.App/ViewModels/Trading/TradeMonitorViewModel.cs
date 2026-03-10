using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

public partial class TradeMonitorViewModel : ViewModelBase, IDisposable
{
    private readonly MarketMonitor _marketMonitor;
    private readonly CryptoPortfolioService _portfolioService;
    private readonly IExchangeClient _exchangeClient;
    private readonly TradingDataService _dataService;

    public ObservableCollection<AssetBalance> Balances { get; } = [];
    public ObservableCollection<ExchangeOrderResult> OpenOrders { get; } = [];

    [ObservableProperty] private decimal _totalValueUSDT;
    [ObservableProperty] private bool _isMonitorRunning;
    [ObservableProperty] private DailyStats _todayStats = new();

    public TradeMonitorViewModel(
        MarketMonitor marketMonitor,
        CryptoPortfolioService portfolioService,
        IExchangeClient exchangeClient,
        TradingDataService dataService,
        ILogger<TradeMonitorViewModel> logger)
        : base(logger)
    {
        _marketMonitor = marketMonitor;
        _portfolioService = portfolioService;
        _exchangeClient = exchangeClient;
        _dataService = dataService;

        _isMonitorRunning = _marketMonitor.IsRunning;
        _marketMonitor.StatusChanged += OnMonitorStatusChanged;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            TodayStats = await _dataService.GetTodayStatsAsync();

            try
            {
                var summary = await _portfolioService.GetAccountBalanceSummaryAsync();
                Balances.Clear();
                foreach (var balance in summary.Assets)
                {
                    Balances.Add(balance);
                }

                TotalValueUSDT = summary.TotalValueUSDT;

                var orders = await _exchangeClient.GetOpenOrdersAsync();
                OpenOrders.Clear();
                foreach (var o in orders)
                    OpenOrders.Add(o);
            }
            catch (InvalidOperationException)
            {
                Logger?.LogWarning("Binance API 未配置，跳过账户数据加载");
            }
        }, "刷新交易监控");
    }

    [RelayCommand]
    private async Task ToggleMonitorAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            if (_marketMonitor.IsRunning)
                await _marketMonitor.StopAsync();
            else
                await _marketMonitor.StartAsync();
        }, "切换监控状态");
    }

    private void OnMonitorStatusChanged(bool isRunning)
    {
        IsMonitorRunning = isRunning;
    }

    public void Dispose()
    {
        _marketMonitor.StatusChanged -= OnMonitorStatusChanged;
        GC.SuppressFinalize(this);
    }
}
