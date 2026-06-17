using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

public partial class TradeMonitorViewModel : ViewModelBase, IDisposable
{
    private readonly MarketMonitor _marketMonitor;
    private readonly CryptoPortfolioService _portfolioService;
    private readonly IExchangeClient _exchangeClient;
    private readonly TradingDataService _dataService;
    private readonly TradeExecutor _tradeExecutor;

    public ObservableCollection<AssetBalance> Balances { get; } = [];
    public ObservableCollection<ExchangeOrderResult> OpenOrders { get; } = [];

    [ObservableProperty] private decimal _totalValueUSDT;
    [ObservableProperty] private bool _isMonitorRunning;
    [ObservableProperty] private DailyStats _todayStats = new();

    // Human-in-the-Loop 确认
    [ObservableProperty] private bool _hasPendingConfirmation;
    [ObservableProperty] private string _confirmationSymbol = string.Empty;
    [ObservableProperty] private string _confirmationSide = string.Empty;
    [ObservableProperty] private string _confirmationPrice = string.Empty;
    [ObservableProperty] private string _confirmationQuantity = string.Empty;
    [ObservableProperty] private string _confirmationReason = string.Empty;

    private TaskCompletionSource<bool>? _confirmationTcs;

    public TradeMonitorViewModel(
        MarketMonitor marketMonitor,
        CryptoPortfolioService portfolioService,
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        TradingDataService dataService,
        TradeExecutor tradeExecutor,
        ILogger<TradeMonitorViewModel> logger)
        : base(logger)
    {
        _marketMonitor = marketMonitor;
        _portfolioService = portfolioService;
        _exchangeClient = exchangeClient;
        _dataService = dataService;
        _tradeExecutor = tradeExecutor;

        _isMonitorRunning = _marketMonitor.IsRunning;
        _marketMonitor.StatusChanged += OnMonitorStatusChanged;

        // 接管 TradeExecutor 的确认回调
        _tradeExecutor.ConfirmationCallback = OnTradeConfirmationRequestedAsync;
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

    private Task<bool> OnTradeConfirmationRequestedAsync(
        string symbol, OrderSide side, decimal price, decimal quantity, string reason)
    {
        ConfirmationSymbol = symbol;
        ConfirmationSide = side.ToString();
        ConfirmationPrice = price.ToString("F2");
        ConfirmationQuantity = quantity.ToString("F6");
        ConfirmationReason = reason;
        HasPendingConfirmation = true;

        _confirmationTcs = new TaskCompletionSource<bool>();
        return _confirmationTcs.Task;
    }

    [RelayCommand]
    private void ApproveConfirmation()
    {
        HasPendingConfirmation = false;
        _confirmationTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void RejectConfirmation()
    {
        HasPendingConfirmation = false;
        _confirmationTcs?.TrySetResult(false);
    }

    public void Dispose()
    {
        _marketMonitor.StatusChanged -= OnMonitorStatusChanged;
        GC.SuppressFinalize(this);
    }
}
