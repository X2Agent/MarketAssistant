using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.Messaging;

namespace MarketAssistant.ViewModels.Trading;

public partial class TradeMonitorViewModel : ViewModelBase, IDisposable
{
    private const decimal RiskWarningThreshold = 0.8m;
    private const int ConfirmationTimeoutSeconds = 60;

    private readonly MarketMonitor _marketMonitor;
    private readonly CryptoPortfolioService _portfolioService;
    private readonly IExchangeClient _exchangeClient;
    private readonly TradingDataService _dataService;
    private readonly TradingStrategyService _strategyService;
    private readonly OrderStateSyncService _orderStateSyncService;
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
        TradingStrategyService strategyService,
        OrderStateSyncService orderStateSyncService,
        TradeExecutor tradeExecutor,
        ILogger<TradeMonitorViewModel> logger)
        : base(logger)
    {
        _marketMonitor = marketMonitor;
        _portfolioService = portfolioService;
        _exchangeClient = exchangeClient;
        _dataService = dataService;
        _strategyService = strategyService;
        _orderStateSyncService = orderStateSyncService;
        _tradeExecutor = tradeExecutor;

        _isMonitorRunning = _marketMonitor.IsRunning;
        _marketMonitor.StatusChanged += OnMonitorStatusChanged;

        // 接管 TradeExecutor 的确认事件（使用事件模式，Dispose 时取消订阅）
        _tradeExecutor.ConfirmationRequested += OnTradeConfirmationRequestedAsync;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            await _orderStateSyncService.SyncPendingOrdersAsync(force: true);

            TodayStats = await _dataService.GetTodayStatsAsync();
            RiskConfig = await _dataService.LoadRiskConfigAsync();

            // 计算风控指标
            RemainingDailyTrades = Math.Max(0, RiskConfig.MaxDailyTrades - TodayStats.TradeCount);

            // 本地 SQLite 数据不依赖币安 API，独立加载避免被 API 异常连带跳过
            var positions = await _dataService.GetOpenPositionsAsync();
            Positions.Clear();
            foreach (var p in positions)
            {
                Positions.Add(p);
            }

            // 以下为币安 HTTP 调用，API 未配置或网络异常时降级处理
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
                                  && DailyLossPercent / RiskConfig.MaxDailyLossPercent >= RiskWarningThreshold;
                IsPositionHigh = RiskConfig.MaxTotalPositionPercent > 0
                                 && TotalPositionPercent / RiskConfig.MaxTotalPositionPercent >= RiskWarningThreshold;

                var orders = await _exchangeClient.GetOpenOrdersAsync();
                OpenOrders.Clear();
                foreach (var o in orders)
                    OpenOrders.Add(o);
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                                       ex.InnerException is InvalidOperationException)
            {
                // API 未配置场景：BinanceAuthService.EnsureConfigured 抛 InvalidOperationException
                // 被 BinanceAccountServiceBase 包装为 FriendlyException(InnerException = InvalidOperationException)
                Logger?.LogWarning("Binance API 未配置，跳过账户余额与未完成订单加载");
            }
            catch (Exception ex) when (ex is FriendlyException ||
                                       ex.InnerException is HttpRequestException)
            {
                // API 已配置但网络/请求失败：保留本地数据，仅记录日志
                Logger?.LogWarning(ex, "Binance API 调用失败，账户余额与未完成订单未刷新");
            }

            // 加载活跃策略
            var activeStrategies = await _strategyService.GetStrategiesByStatusAsync(StrategyStatus.Active);
            ActiveStrategies.Clear();
            foreach (var s in activeStrategies)
            {
                ActiveStrategies.Add(s);
            }
        }, "刷新交易监控");
    }

    [RelayCommand]
    private void ShowBalanceDetail()
    {
        WeakReferenceMessenger.Default.Send(new NavigationMessage("BalanceDetail"));
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
        _confirmationCts = new CancellationTokenSource(TimeSpan.FromSeconds(ConfirmationTimeoutSeconds));
        _confirmationCts.Token.Register(() => _confirmationTcs.TrySetResult(false));

        return _confirmationTcs.Task;
    }

    [RelayCommand]
    private void ApproveConfirmation()
    {
        HasPendingConfirmation = false;
        // 注意：必须 Dispose 而非 Cancel。
        // Cancel 会同步触发 Token.Register 的回调（TrySetResult(false)），
        // 导致随后的 TrySetResult(true) 被忽略，用户批准反而变成拒绝。
        _confirmationCts?.Dispose();
        _confirmationCts = null;
        _confirmationTcs?.TrySetResult(true);
    }

    [RelayCommand]
    private void RejectConfirmation()
    {
        HasPendingConfirmation = false;
        _confirmationCts?.Dispose();
        _confirmationCts = null;
        _confirmationTcs?.TrySetResult(false);
    }

    public void Dispose()
    {
        _marketMonitor.StatusChanged -= OnMonitorStatusChanged;
        // 取消订阅事件，避免单例 TradeExecutor 持有已 Dispose 的 ViewModel 引用
        _tradeExecutor.ConfirmationRequested -= OnTradeConfirmationRequestedAsync;
        // 释放前若仍有待确认请求，按拒绝处理，避免调用方永久挂起
        _confirmationTcs?.TrySetResult(false);
        _confirmationCts?.Dispose();
        _confirmationCts = null;
        GC.SuppressFinalize(this);
    }
}
