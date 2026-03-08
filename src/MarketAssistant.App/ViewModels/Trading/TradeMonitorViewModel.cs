using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Crypto;
using MarketAssistant.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

public partial class TradeMonitorViewModel : ViewModelBase, IDisposable
{
    private readonly MarketMonitor _marketMonitor;
    private readonly BinanceAccountService _accountService;
    private readonly TradingDataService _dataService;

    public ObservableCollection<AssetBalance> Balances { get; } = [];
    public ObservableCollection<BinanceOrderResponse> OpenOrders { get; } = [];

    [ObservableProperty] private decimal _totalValueUSDT;
    [ObservableProperty] private bool _isMonitorRunning;
    [ObservableProperty] private DailyStats _todayStats = new();

    public TradeMonitorViewModel(
        MarketMonitor marketMonitor,
        BinanceAccountService accountService,
        TradingDataService dataService,
        ILogger<TradeMonitorViewModel> logger)
        : base(logger)
    {
        _marketMonitor = marketMonitor;
        _accountService = accountService;
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
                var accountInfo = await _accountService.GetAccountInfoAsync();
                Balances.Clear();
                decimal total = 0;

                foreach (var balance in accountInfo.Balances)
                {
                    if (!decimal.TryParse(balance.Free, out var free) || !decimal.TryParse(balance.Locked, out var locked))
                        continue;
                    if (free == 0 && locked == 0)
                        continue;

                    var ab = new AssetBalance
                    {
                        Asset = balance.Asset,
                        Free = free,
                        Locked = locked,
                        ValueUSDT = balance.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase) ? free + locked : 0
                    };
                    total += ab.ValueUSDT;
                    Balances.Add(ab);
                }

                TotalValueUSDT = total;

                var orders = await _accountService.GetOpenOrdersAsync();
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
