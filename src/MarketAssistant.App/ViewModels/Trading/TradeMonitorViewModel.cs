using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
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

    private readonly MarketMonitor _marketMonitor;
    private readonly CryptoPortfolioService _portfolioService;
    private readonly IExchangeClient _exchangeClient;
    private readonly TradingDataService _dataService;
    private readonly TradingStrategyService _strategyService;
    private readonly OrderStateSyncService _orderStateSyncService;
    private readonly EmergencyStopService _emergencyStopService;
    private readonly TradingHaltState _haltState;
    private readonly LiquidationRiskEvaluator _liquidationRiskEvaluator;
    private readonly StrategyPerformanceService _performanceService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;

    public ObservableCollection<AssetBalance> Balances { get; } = [];
    public ObservableCollection<ExchangeOrderResult> OpenOrders { get; } = [];
    public ObservableCollection<Position> Positions { get; } = [];
    public ObservableCollection<TradingStrategy> ActiveStrategies { get; } = [];
    /// <summary>合约持仓强平风险（仅合约模式有数据）。</summary>
    public ObservableCollection<LiquidationRiskInfo> LiquidationRisks { get; } = [];

    [ObservableProperty] private decimal _totalValueUSDT;
    [ObservableProperty] private bool _isMonitorRunning;
    [ObservableProperty] private DailyStats _todayStats = new();

    // 风控指标
    [ObservableProperty] private decimal _dailyLossPercent;
    [ObservableProperty] private decimal _totalPositionPercent;
    [ObservableProperty] private int _remainingDailyTrades;
    [ObservableProperty] private RiskConfig _riskConfig = new();

    /// <summary>合约名义敞口占权益百分比（现货恒为 0）。</summary>
    [ObservableProperty] private decimal _futuresNotionalPercent;

    // 派生展示属性（由 RefreshAsync 计算）
    [ObservableProperty] private decimal _todayPnlPercent;
    [ObservableProperty] private bool _isTodayProfitable;
    [ObservableProperty] private bool _isDailyLossHigh;
    [ObservableProperty] private bool _isPositionHigh;

    /// <summary>是否存在合约强平风险数据（控制风险卡片显隐）。</summary>
    public bool HasLiquidationRisks => LiquidationRisks.Count > 0;

    /// <summary>合约名义敞口展示文本（现货模式显示"--"）。</summary>
    public string FuturesNotionalText =>
        _exchangeClient.IsFutures
            ? $"{FuturesNotionalPercent:F0}% / {RiskConfig.MaxFuturesNotionalPercent:F0}%"
            : "--";

    // 说明：Human-in-the-Loop 交易确认已上移至应用级 TradeConfirmationService
    // （全局对话框，不依赖本页存活），本 ViewModel 不再订阅 ConfirmationRequested。

    public TradeMonitorViewModel(
        MarketMonitor marketMonitor,
        CryptoPortfolioService portfolioService,
        [FromKeyedServices(MarketType.Crypto)] IExchangeClient exchangeClient,
        TradingDataService dataService,
        TradingStrategyService strategyService,
        OrderStateSyncService orderStateSyncService,
        EmergencyStopService emergencyStopService,
        TradingHaltState haltState,
        LiquidationRiskEvaluator liquidationRiskEvaluator,
        StrategyPerformanceService performanceService,
        IDialogService dialogService,
        INotificationService notificationService,
        ILogger<TradeMonitorViewModel> logger)
        : base(logger)
    {
        _marketMonitor = marketMonitor;
        _portfolioService = portfolioService;
        _exchangeClient = exchangeClient;
        _dataService = dataService;
        _strategyService = strategyService;
        _orderStateSyncService = orderStateSyncService;
        _emergencyStopService = emergencyStopService;
        _haltState = haltState;
        _liquidationRiskEvaluator = liquidationRiskEvaluator;
        _performanceService = performanceService;
        _dialogService = dialogService;
        _notificationService = notificationService;

        _isMonitorRunning = _marketMonitor.IsRunning;
        _marketMonitor.StatusChanged += OnMonitorStatusChanged;

        // 停机状态是本页横幅与按钮的数据源；首屏加载时刷新
        _haltState.HaltStateChanged += OnHaltStateChanged;

        // 本页为分区仪表盘：空态由各分区自行提示，浮层只承担首屏骨架与失败重试
        SetRetryCommand(() => RefreshAsync());
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadAsync(async _ =>
        {
            await _orderStateSyncService.SyncPendingOrdersAsync(force: true);

            TodayStats = await _dataService.GetTodayStatsAsync();
            RiskConfig = await _dataService.LoadRiskConfigAsync();

            // 停机状态是页面横幅与紧急停止按钮的数据源，随刷新同步
            await _haltState.EnsureLoadedAsync();
            SyncHaltState();

            // 绩效统计走本地数据库，与交易所可用性无关
            await LoadPerformanceAsync();

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

                // 强平风险快照：由周期评估器维护，此处只读取（避免页面刷新触发额外交易所查询）
                LiquidationRisks.Clear();
                foreach (var risk in _liquidationRiskEvaluator.LatestRisks)
                    LiquidationRisks.Add(risk);
                OnPropertyChanged(nameof(HasLiquidationRisks));

                // 合约名义敞口：现货无杠杆敞口概念，保持 0
                FuturesNotionalPercent = 0;
                if (_exchangeClient.IsFutures && TotalValueUSDT > 0)
                {
                    var exchangePositions = await _exchangeClient.GetPositionsAsync();
                    var notional = exchangePositions.Sum(p =>
                        Math.Abs(p.PositionAmt) * (p.MarkPrice > 0 ? p.MarkPrice : p.EntryPrice));
                    FuturesNotionalPercent = notional / TotalValueUSDT * 100;
                    OnPropertyChanged(nameof(FuturesNotionalText));
                }
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

            return true;
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
        // StatusChanged 由后台线程触发，需切回 UI 线程更新绑定属性
        Avalonia.Threading.Dispatcher.UIThread.Post(() => IsMonitorRunning = isRunning);
    }

    /// <summary>停机状态变化（可能来自其它页面或重启后的首次加载）时同步到绑定属性。</summary>
    private void OnHaltStateChanged(bool halted) => SyncHaltState();

    public void Dispose()
    {
        _marketMonitor.StatusChanged -= OnMonitorStatusChanged;
        _haltState.HaltStateChanged -= OnHaltStateChanged;
        GC.SuppressFinalize(this);
    }
}
