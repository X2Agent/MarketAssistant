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
    public ObservableCollection<Position> Positions { get; } = [];
    public ObservableCollection<TradingStrategy> ActiveStrategies { get; } = [];

    [ObservableProperty] private decimal _totalValueUSDT;
    [ObservableProperty] private bool _isMonitorRunning;
    [ObservableProperty] private DailyStats _todayStats = new();

    // 风控指标
    [ObservableProperty] private decimal _dailyLossPercent;
    [ObservableProperty] private decimal _totalPositionPercent;
    [ObservableProperty] private int _remainingDailyTrades;
    [ObservableProperty] private RiskConfig _riskConfig = new();

    // 派生展示属性（由 RefreshAsync 计算）
    [ObservableProperty] private decimal _todayPnlPercent;
    [ObservableProperty] private bool _isTodayProfitable;
    [ObservableProperty] private bool _isDailyLossHigh;
    [ObservableProperty] private bool _isPositionHigh;

    // Human-in-the-Loop 确认
    [ObservableProperty] private bool _hasPendingConfirmation;
    [ObservableProperty] private string _confirmationSymbol = string.Empty;
    [ObservableProperty] private string _confirmationSide = string.Empty;
    [ObservableProperty] private string _confirmationPrice = string.Empty;
    [ObservableProperty] private string _confirmationQuantity = string.Empty;
    [ObservableProperty] private string _confirmationReason = string.Empty;

    private TaskCompletionSource<bool>? _confirmationTcs;
    private CancellationTokenSource? _confirmationCts;

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
            RiskConfig = _dataService.LoadRiskConfig();

            // 计算风控指标
            RemainingDailyTrades = Math.Max(0, RiskConfig.MaxDailyTrades - TodayStats.TradeCount);

            try
            {
                var summary = await _portfolioService.GetAccountBalanceSummaryAsync();
                Balances.Clear();
                foreach (var balance in summary.Assets)
                {
                    Balances.Add(balance);
                }

                TotalValueUSDT = summary.TotalValueUSDT;

                // 计算风控百分比指标
                if (TotalValueUSDT > 0)
                {
                    DailyLossPercent = TodayStats.TotalPnl < 0
                        ? Math.Abs(TodayStats.TotalPnl) / TotalValueUSDT * 100
                        : 0;

                    var usdtBalance = CryptoPortfolioService.GetUsdtBalance(summary);
                    var nonUsdtValue = TotalValueUSDT - usdtBalance;
                    TotalPositionPercent = nonUsdtValue / TotalValueUSDT * 100;

                    // 派生展示属性
                    TodayPnlPercent = TodayStats.TotalPnl / TotalValueUSDT * 100;
                    IsTodayProfitable = TodayStats.TotalPnl >= 0;
                }
                else
                {
                    TodayPnlPercent = 0;
                    IsTodayProfitable = true;
                }

                // 风控阈值预警（达到限额 80% 视为高位）
                IsDailyLossHigh = RiskConfig.MaxDailyLossPercent > 0
                                  && DailyLossPercent / RiskConfig.MaxDailyLossPercent >= 0.8m;
                IsPositionHigh = RiskConfig.MaxTotalPositionPercent > 0
                                 && TotalPositionPercent / RiskConfig.MaxTotalPositionPercent >= 0.8m;

                // 加载 FIFO 持仓
                var positions = await _dataService.GetOpenPositionsAsync();
                Positions.Clear();
                foreach (var p in positions)
                {
                    Positions.Add(p);
                }

                var orders = await _exchangeClient.GetOpenOrdersAsync();
                OpenOrders.Clear();
                foreach (var o in orders)
                    OpenOrders.Add(o);
            }
            catch (InvalidOperationException)
            {
                Logger?.LogWarning("Binance API 未配置，跳过账户数据加载");
            }

            // 加载活跃策略
            var activeStrategies = await _dataService.GetStrategiesByStatusAsync(StrategyStatus.Active);
            ActiveStrategies.Clear();
            foreach (var s in activeStrategies)
            {
                ActiveStrategies.Add(s);
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

        // 60 秒超时自动拒绝，避免用户离开后交易长时间挂起
        _confirmationCts?.Dispose();
        _confirmationCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        _confirmationCts.Token.Register(() => _confirmationTcs.TrySetResult(false));

        return _confirmationTcs.Task;
    }

    [RelayCommand]
    private void ApproveConfirmation()
    {
        HasPendingConfirmation = false;
        _confirmationCts?.Cancel();
        _confirmationTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void RejectConfirmation()
    {
        HasPendingConfirmation = false;
        _confirmationCts?.Cancel();
        _confirmationTcs?.TrySetResult(false);
    }

    public void Dispose()
    {
        _marketMonitor.StatusChanged -= OnMonitorStatusChanged;
        _confirmationCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}
