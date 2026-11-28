using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Abstractions;
using MarketAssistant.Trading.Models;
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
    private readonly TradingEnvironmentService _environmentService;
    private readonly IDialogService _dialogService;
    private readonly INotificationService _notificationService;

    public ObservableCollection<AssetBalance> Balances { get; } = [];
    public ObservableCollection<ExchangeOrderResult> OpenOrders { get; } = [];
    /// <summary>持仓快照：以交易所为准（含未实现盈亏、标记价、强平价、杠杆）。</summary>
    public ObservableCollection<PositionInfo> Positions { get; } = [];
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
    [ObservableProperty] private bool _isDailyLossHigh;
    [ObservableProperty] private bool _isPositionHigh;

    /// <summary>
    /// 币安 API 数据（余额/挂单/持仓快照）本轮刷新是否失败。
    /// 失败时页面保留上一轮数据并显示陈旧提示——"数据源挂了"必须与"数据是新的"可区分。
    /// </summary>
    [ObservableProperty] private bool _isBinanceDataStale;

    /// <summary>是否存在合约强平风险数据（控制风险卡片显隐）。</summary>
    public bool HasLiquidationRisks => LiquidationRisks.Count > 0;

    /// <summary>合约名义敞口展示文本（现货模式显示"--"）。</summary>
    public string FuturesNotionalText =>
        _exchangeClient.IsFutures
            ? $"{FuturesNotionalPercent:F0}% / {RiskConfig.MaxFuturesNotionalPercent:F0}%"
            : "--";

    /// <summary>当前交易环境名称，写进启动 / 紧急停止等确认框，让用户在决策点就看到订单会落到哪里。</summary>
    private string CurrentEnvironmentText => TradingEnvironmentService.GetModeDisplayName(_environmentService.CurrentMode);

    // 说明：交易环境（实盘 / 测试网）由 TradingPageViewModel 在页签栏统一展示，本页不再重复。
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
        TradingEnvironmentService environmentService,
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
        _environmentService = environmentService;
        _dialogService = dialogService;
        _notificationService = notificationService;

        _isMonitorRunning = _marketMonitor.IsRunning;
        _marketMonitor.StatusChanged += OnMonitorStatusChanged;

        // 停机状态是本页横幅与按钮的数据源；首屏加载时刷新
        _haltState.HaltStateChanged += OnHaltStateChanged;

        // 合约 / 现货切换会改变名义敞口的展示，随环境切换刷新
        _environmentService.ModeChanged += OnTradingModeChanged;

        // 本页为分区仪表盘：空态由各分区自行提示；首屏用骨架，失败用页内失败条（HasError + RetryCommand），
        // 不用居中浮层，避免盖住紧急停止与风控指标
        SetRetryCommand(() => RefreshAsync());
    }

    private void OnTradingModeChanged(TradingMode mode)
    {
        // ModeChanged 可能由后台线程触发（如监控停止路径），绑定属性更新必须切回 UI 线程
        Avalonia.Threading.Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(FuturesNotionalText)));
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
            // 持仓展示以交易所快照为准（下方 try 块内），这里只保留本地策略关联信息

            // 以下为币安 HTTP 调用，API 未配置或网络异常时降级处理：
            // 保留上一轮数据并置陈旧标记，让"数据源失败"在 UI 上可感知，而不是伪装成最新数据
            IsBinanceDataStale = false;
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
                }
                else
                {
                    TodayPnlPercent = 0;
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

                // 持仓快照以交易所为准：含未实现盈亏、标记价、强平价与杠杆。
                // 本地 FIFO 只用于补策略归属，不作为持仓真相（外部成交会让本地账本漂移）
                var snapshotPositions = await _portfolioService.GetCurrentPositionsAsync();
                var strategyIdsBySymbol = await GetStrategyIdsBySymbolAsync();
                Positions.Clear();
                foreach (var position in snapshotPositions)
                {
                    if (strategyIdsBySymbol.TryGetValue(position.Symbol, out var strategyId))
                        position.StrategyId = strategyId;

                    Positions.Add(position);
                }

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
                IsBinanceDataStale = true;
            }
            catch (Exception ex) when (ex is FriendlyException ||
                                       ex is HttpRequestException ||
                                       ex.InnerException is HttpRequestException)
            {
                // API 已配置但网络/请求失败：保留本地数据，仅记录日志
                Logger?.LogWarning(ex, "Binance API 调用失败，账户余额与未完成订单未刷新");
                IsBinanceDataStale = true;
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

    /// <summary>
    /// 标的 → 策略 ID 映射（来自本地 FIFO 开仓记录）。
    /// 只用于在交易所持仓快照上补"这条仓位属于哪条策略"，不作为持仓真相；
    /// 同一标的有多条策略时取最近开仓的那条，手动开仓（无 StrategyId）不参与。
    /// </summary>
    private async Task<Dictionary<string, string>> GetStrategyIdsBySymbolAsync()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var openPositions = await _dataService.GetOpenPositionsAsync();
        foreach (var position in openPositions
                     .Where(p => !string.IsNullOrEmpty(p.StrategyId))
                     .OrderByDescending(p => p.OpenedAt))
        {
            map.TryAdd(position.Symbol, position.StrategyId!);
        }

        return map;
    }

    [RelayCommand]
    private async Task ToggleMonitorAsync()
    {
        // 停止方向永不二次确认：这是资金安全动作，必须一拨即停。
        if (!_marketMonitor.IsRunning)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "启动交易监控",
                $"当前环境：{CurrentEnvironmentText}\n\n" +
                "启动后，已启用的策略会在价格条件满足时自动下单。是否继续？",
                "启动监控",
                "取消",
                topmost: true);

            // 取消后必须回推一次，把开关视觉复位到真实状态（IsChecked 是 OneWay 绑定）
            if (!confirmed)
            {
                OnPropertyChanged(nameof(IsMonitorRunning));
                return;
            }
        }

        await SafeExecuteAsync(async () =>
        {
            if (_marketMonitor.IsRunning)
                await _marketMonitor.StopAsync();
            else
                await _marketMonitor.StartAsync();
        }, "切换监控状态");

        // 紧急停止期间 StartAsync 会静默拒绝启动，再对齐一次视觉
        OnPropertyChanged(nameof(IsMonitorRunning));
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
        _environmentService.ModeChanged -= OnTradingModeChanged;
        GC.SuppressFinalize(this);
    }
}
