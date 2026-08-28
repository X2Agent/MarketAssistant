using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.DataProviders;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Extensions;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Notification;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using MarketAssistant.Views.Windows;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

public partial class StrategyConfigViewModel : ViewModelBase, IDisposable
{
    private readonly TradingStrategyService _strategyService;
    private readonly TradingDataService _dataService;
    private readonly MarketMonitor _marketMonitor;
    private readonly IDialogService _dialogService;
    private readonly BinanceMarketDataService _marketDataService;
    private readonly INotificationService _notificationService;
    private bool _disposed;

    public ObservableCollection<TradingStrategy> Strategies { get; } = [];

    [ObservableProperty] private string _newSymbol = string.Empty;

    /// <summary>当前选择的场景卡片（UI 唯一创建入口：智能策略 / 省心定投 / 区间网格）。</summary>
    [ObservableProperty] private ScenarioKind _selectedScenario = ScenarioKind.AISmart;

    /// <summary>当前选择的风险档案，驱动场景参数预填与引擎兜底护栏。</summary>
    [ObservableProperty] private RiskProfile _selectedProfile = RiskProfile.Balanced;

    partial void OnSelectedScenarioChanged(ScenarioKind value)
    {
        OnPropertyChanged(nameof(IsAISmartScenario));
        OnPropertyChanged(nameof(IsDCAScenario));
        OnPropertyChanged(nameof(IsGridScenario));
        ApplyScenarioPreset();
    }

    partial void OnSelectedProfileChanged(RiskProfile value)
    {
        OnPropertyChanged(nameof(IsConservativeProfile));
        OnPropertyChanged(nameof(IsBalancedProfile));
        OnPropertyChanged(nameof(IsAggressiveProfile));
        ApplyScenarioPreset();
    }

    public bool IsAISmartScenario
    {
        get => SelectedScenario == ScenarioKind.AISmart;
        set { if (value) SelectedScenario = ScenarioKind.AISmart; }
    }

    public bool IsDCAScenario
    {
        get => SelectedScenario == ScenarioKind.DCA;
        set { if (value) SelectedScenario = ScenarioKind.DCA; }
    }

    public bool IsGridScenario
    {
        get => SelectedScenario == ScenarioKind.Grid;
        set { if (value) SelectedScenario = ScenarioKind.Grid; }
    }

    public bool IsConservativeProfile
    {
        get => SelectedProfile == RiskProfile.Conservative;
        set { if (value) SelectedProfile = RiskProfile.Conservative; }
    }

    public bool IsBalancedProfile
    {
        get => SelectedProfile == RiskProfile.Balanced;
        set { if (value) SelectedProfile = RiskProfile.Balanced; }
    }

    public bool IsAggressiveProfile
    {
        get => SelectedProfile == RiskProfile.Aggressive;
        set { if (value) SelectedProfile = RiskProfile.Aggressive; }
    }

    public OrderSide[] OrderSides => Enum.GetValues<OrderSide>();

    // ---- 智能策略（AI）参数 ----
    [ObservableProperty] private string _aiBudgetUsdt = string.Empty;
    [ObservableProperty] private string _aiIntervalSeconds = string.Empty;
    [ObservableProperty] private string _aiConfidenceThreshold = string.Empty;
    [ObservableProperty] private string _aiMaxPositionPercent = string.Empty;
    [ObservableProperty] private string _aiStopLossPercent = string.Empty;
    [ObservableProperty] private string _aiTakeProfitPercent = string.Empty;
    [ObservableProperty] private bool _isAiTrailingExit;
    [ObservableProperty] private string _aiTrailingPercent = string.Empty;
    [ObservableProperty] private bool _aiShadowMode;
    [ObservableProperty] private OrderSide _aiSide = OrderSide.Buy;

    [ObservableProperty] private bool _isCreating;

    /// <summary>
    /// 表单校验错误信息。非空时显示在创建按钮旁。
    /// </summary>
    [ObservableProperty] private string _validationError = string.Empty;

    // ---- 网格参数 ----
    [ObservableProperty] private string _gridUpperPrice = string.Empty;
    [ObservableProperty] private string _gridLowerPrice = string.Empty;
    [ObservableProperty] private string _gridCount = "10";
    [ObservableProperty] private string _gridQuantityPerGrid = string.Empty;
    [ObservableProperty] private string _gridStopLossPrice = string.Empty;
    [ObservableProperty] private string _gridTakeProfitPrice = string.Empty;

    // ---- DCA 参数 ----
    [ObservableProperty] private string _dcaIntervalSeconds = "86400";
    [ObservableProperty] private string _dcaAmountPerInterval = string.Empty;
    [ObservableProperty] private string _dcaMaxBuyPrice = string.Empty;
    [ObservableProperty] private string _dcaDoubleBuyBelowPrice = string.Empty;
    [ObservableProperty] private string _dcaTakeProfitPercent = string.Empty;
    [ObservableProperty] private string _dcaStopLossPercent = string.Empty;
    [ObservableProperty] private bool _dcaStopLossSellOut;

    /// <summary>
    /// 按当前风险档案与场景预填表单参数（网格区间需现价，仅预填格数）。
    /// </summary>
    private void ApplyScenarioPreset()
    {
        switch (SelectedScenario)
        {
            case ScenarioKind.AISmart:
                var ai = ScenarioPresets.GetAISignalPreset(SelectedProfile);
                AiIntervalSeconds = ai.AnalysisIntervalSeconds.ToString();
                AiConfidenceThreshold = ai.ConfidenceThreshold.ToString();
                AiMaxPositionPercent = ai.MaxPositionPercent.ToString(CultureInfo.InvariantCulture);
                AiStopLossPercent = ai.StopLossPercent.ToString(CultureInfo.InvariantCulture);
                AiTakeProfitPercent = ai.TakeProfitPercent.ToString(CultureInfo.InvariantCulture);
                IsAiTrailingExit = ai.ExitStyle == ExitStyle.TrailingStop;
                AiTrailingPercent = ai.TrailingPercent.ToString(CultureInfo.InvariantCulture);
                break;

            case ScenarioKind.DCA:
                var dca = ScenarioPresets.GetDcaPreset(SelectedProfile);
                DcaIntervalSeconds = dca.IntervalSeconds.ToString();
                DcaTakeProfitPercent = dca.TakeProfitPercent.ToString(CultureInfo.InvariantCulture);
                DcaStopLossPercent = dca.StopLossPercent.ToString(CultureInfo.InvariantCulture);
                DcaStopLossSellOut = dca.StopLossSellOut;
                break;

            case ScenarioKind.Grid:
                GridCount = ScenarioPresets.GetGridPreset(SelectedProfile).GridCount.ToString();
                break;
        }
    }

    // 风控配置
    [ObservableProperty] private RiskConfig _riskConfig = new();

    /// <summary>
    /// MarketMonitor 是否正在运行。策略标为 Active 后，只有监控运行中才会真正触发交易。
    /// </summary>
    [ObservableProperty] private bool _isMonitorRunning;

    public StrategyConfigViewModel(
        TradingStrategyService strategyService,
        TradingDataService dataService,
        MarketMonitor marketMonitor,
        IDialogService dialogService,
        BinanceMarketDataService marketDataService,
        INotificationService notificationService,
        ILogger<StrategyConfigViewModel> logger)
        : base(logger)
    {
        _strategyService = strategyService;
        _dataService = dataService;
        _marketMonitor = marketMonitor;
        _dialogService = dialogService;
        _marketDataService = marketDataService;
        _notificationService = notificationService;
        IsMonitorRunning = _marketMonitor.IsRunning;
        _marketMonitor.StatusChanged += OnMonitorStatusChanged;
        ApplyScenarioPreset();
        _ = InitializeAsync();
    }

    private void OnMonitorStatusChanged(bool isRunning)
    {
        // StatusChanged 由后台线程触发，需切回 UI 线程更新绑定属性
        Avalonia.Threading.Dispatcher.UIThread.Post(() => IsMonitorRunning = isRunning);
    }

    private async Task InitializeAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            RiskConfig = await _strategyService.LoadRiskConfigAsync();
            var strategies = await _strategyService.GetAllStrategiesAsync();
            Strategies.Clear();
            foreach (var s in strategies)
                Strategies.Add(s);
        }, "加载策略列表");
    }

    [RelayCommand]
    private async Task CreateStrategyAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSymbol))
        {
            ValidationError = "请填写交易对（如 BTCUSDT）";
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            ValidationError = string.Empty;
            var symbol = NewSymbol.ToUpperInvariant().Trim();

            TradingStrategy? strategy = SelectedScenario switch
            {
                ScenarioKind.AISmart => BuildAISignalStrategy(symbol),
                ScenarioKind.DCA => BuildDCAStrategy(symbol),
                ScenarioKind.Grid => BuildGridStrategy(symbol),
                _ => null
            };

            if (strategy == null)
                return; // 校验失败，ValidationError 已设置

            await _strategyService.SaveStrategyAsync(strategy);
            Strategies.Insert(0, strategy);

            // 安全警示必须显式可见：本应用没有交易所原生条件单兜底，
            // 止损/止盈/追踪止损全部由客户端按秒轮询执行，进程退出或网络中断期间不生效
            _notificationService.ShowWarning(
                "⚠ 注意：止损/止盈/追踪止损由本程序每秒轮询执行，程序退出或断网期间不会触发，请勿完全依赖程序止损。");

            ClearForm();
        }, "创建策略");
    }

    /// <summary>构建智能策略（AI 信号）。止损/止盈价由 AI 决策给出，此处仅保存档案预设约束。</summary>
    private TradingStrategy? BuildAISignalStrategy(string symbol)
    {
        if (!decimal.TryParse(AiBudgetUsdt, NumberStyles.Float, CultureInfo.InvariantCulture, out var budget) || budget <= 0)
        {
            ValidationError = "请填写有效的单次开仓预算（USDT）";
            return null;
        }
        if (budget < RiskConfig.MinOrderAmount)
        {
            ValidationError = $"预算 {budget:F2} USDT 低于最小下单金额 {RiskConfig.MinOrderAmount:F2} USDT，容易被交易所拒绝";
            return null;
        }

        var aiParams = new AISignalParams
        {
            RiskProfile = SelectedProfile.ToString(),
            BudgetUsdt = budget,
            AnalysisIntervalSeconds = ParseIntOr(AiIntervalSeconds, 600),
            ConfidenceThreshold = ParseIntOr(AiConfidenceThreshold, 65),
            MaxPositionPercent = ParseDecimalOr(AiMaxPositionPercent, 10),
            StopLossPercent = ParseDecimalOr(AiStopLossPercent, 8),
            TakeProfitPercent = ParseDecimalOr(AiTakeProfitPercent, 15),
            ExitStyle = IsAiTrailingExit ? "TrailingStop" : "FixedStop",
            TrailingPercent = ParseDecimalOr(AiTrailingPercent, 5),
            ShadowMode = AiShadowMode
        };

        return new TradingStrategy
        {
            Symbol = symbol,
            Type = StrategyType.AISignal,
            Status = StrategyStatus.Active,
            Side = AiSide,
            // AI 场景的 Quantity 语义为单次开仓预算（USDT），实际下单量由执行器按置信度换算
            Quantity = budget,
            MaxPositionPercent = aiParams.MaxPositionPercent,
            CustomParams = JsonSerializer.Serialize(aiParams)
        };
    }

    /// <summary>构建省心定投策略。金额必须满足交易所最小下单金额，避免被拒后策略自动暂停。</summary>
    private TradingStrategy? BuildDCAStrategy(string symbol)
    {
        if (!decimal.TryParse(DcaAmountPerInterval, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ValidationError = "请填写有效的定投金额（USDT）";
            return null;
        }
        if (amount < RiskConfig.MinOrderAmount)
        {
            ValidationError = $"定投金额 {amount:F2} USDT 低于最小下单金额 {RiskConfig.MinOrderAmount:F2} USDT，会被交易所拒绝";
            return null;
        }

        var dcaParams = new DCAParams
        {
            RiskProfile = SelectedProfile.ToString(),
            AmountPerInterval = amount,
            IntervalSeconds = ParseIntOr(DcaIntervalSeconds, 86400),
            MaxBuyPrice = ParseDecimalOr(DcaMaxBuyPrice, 0),
            DoubleBuyBelowPrice = ParseDecimalOr(DcaDoubleBuyBelowPrice, 0),
            TakeProfitPercent = ParseDecimalOr(DcaTakeProfitPercent, 0),
            StopLossPercent = ParseDecimalOr(DcaStopLossPercent, 0),
            StopLossSellOut = DcaStopLossSellOut
        };

        return new TradingStrategy
        {
            Symbol = symbol,
            Type = StrategyType.DCA,
            Status = StrategyStatus.Active,
            Side = OrderSide.Buy,
            TriggerPrice = dcaParams.MaxBuyPrice,
            // DCA 的 Quantity 存储每次定投的 USDT 金额（代币数量在执行时按实时价格换算）
            Quantity = amount,
            CustomParams = JsonSerializer.Serialize(dcaParams)
        };
    }

    /// <summary>构建区间网格策略。校验间距覆盖双边手续费，破网护栏未填时按档案百分比自动生成。</summary>
    private TradingStrategy? BuildGridStrategy(string symbol)
    {
        if (!decimal.TryParse(GridUpperPrice, NumberStyles.Float, CultureInfo.InvariantCulture, out var upper) ||
            !decimal.TryParse(GridLowerPrice, NumberStyles.Float, CultureInfo.InvariantCulture, out var lower) ||
            !int.TryParse(GridCount, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gridCount) ||
            !decimal.TryParse(GridQuantityPerGrid, NumberStyles.Float, CultureInfo.InvariantCulture, out var qtyPerGrid))
        {
            ValidationError = "请填写完整的网格参数（上界价格、下界价格、网格数量、每格数量）";
            return null;
        }
        if (upper <= lower || gridCount < 2 || qtyPerGrid <= 0)
        {
            ValidationError = "网格参数无效：上界须高于下界，网格数量 ≥ 2，每格数量 > 0";
            return null;
        }

        // 间距必须覆盖双边手续费（单边 0.1%），否则每格利润被手续费吃掉
        var midPrice = (upper + lower) / 2m;
        var spacingPercent = midPrice > 0 ? (upper - lower) / gridCount / midPrice * 100m : 0;
        if (spacingPercent < 0.2m)
        {
            ValidationError = $"网格间距 {spacingPercent:F3}% 低于双边手续费（0.2%），请加宽区间或减少格数";
            return null;
        }

        var gridPreset = ScenarioPresets.GetGridPreset(SelectedProfile);
        var gridParams = new GridTradingParams
        {
            RiskProfile = SelectedProfile.ToString(),
            UpperPrice = upper,
            LowerPrice = lower,
            GridCount = gridCount,
            QuantityPerGrid = qtyPerGrid,
            // 破网护栏未填时按风险档案百分比自动生成，保证护栏永不缺失
            StopLossPrice = ParseDecimalOrNullable(GridStopLossPrice) ?? lower * (1 - gridPreset.BreakoutStopLossPercent / 100m),
            TakeProfitPrice = ParseDecimalOrNullable(GridTakeProfitPrice) ?? upper * (1 + gridPreset.BreakoutTakeProfitPercent / 100m)
        };

        return new TradingStrategy
        {
            Symbol = symbol,
            Type = StrategyType.GridTrading,
            Status = StrategyStatus.Active,
            Side = OrderSide.Buy,
            TriggerPrice = lower,
            Quantity = qtyPerGrid,
            CustomParams = JsonSerializer.Serialize(gridParams)
        };
    }

    /// <summary>
    /// 按当前价与风险档案自动生成网格区间（含破网护栏），消除手工定价门槛。
    /// </summary>
    [RelayCommand]
    private async Task GenerateGridRangeAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSymbol))
        {
            ValidationError = "请先填写交易对";
            return;
        }
        if (!decimal.TryParse(GridQuantityPerGrid, NumberStyles.Float, CultureInfo.InvariantCulture, out var qtyPerGrid) || qtyPerGrid <= 0)
        {
            ValidationError = "请先填写每格数量，再生成网格区间";
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            ValidationError = string.Empty;
            var symbol = NewSymbol.ToUpperInvariant().Trim();
            var ticker = await _marketDataService.Get24hrTickerAsync(symbol);
            var lastPrice = ticker?.LastPrice;
            if (lastPrice is not > 0)
            {
                ValidationError = $"无法获取 {symbol} 当前价格，请检查交易对或手动填写区间";
                return;
            }

            var gridParams = ScenarioPresets.CreateGridParams(SelectedProfile, lastPrice.Value, qtyPerGrid);
            GridUpperPrice = gridParams.UpperPrice.ToString(CultureInfo.InvariantCulture);
            GridLowerPrice = gridParams.LowerPrice.ToString(CultureInfo.InvariantCulture);
            GridCount = gridParams.GridCount.ToString();
            GridStopLossPrice = gridParams.StopLossPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            GridTakeProfitPrice = gridParams.TakeProfitPrice?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        }, "生成网格区间");
    }

    private static int ParseIntOr(string? text, int fallback)
        => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;

    private static decimal ParseDecimalOr(string? text, decimal fallback)
        => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : fallback;

    private static decimal? ParseDecimalOrNullable(string? text)
        => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 ? value : null;

    [RelayCommand]
    private async Task ToggleStrategyAsync(TradingStrategy strategy)
    {
        await SafeExecuteAsync(async () =>
        {
            // 已完成或失败的策略不直接切换状态，避免误启动已结束的策略
            if (strategy.Status is StrategyStatus.Completed or StrategyStatus.Failed)
            {
                throw new FriendlyException($"策略当前状态为「{strategy.Status.GetDescription()}」，无法直接切换，请删除后重新创建");
            }

            var newStatus = strategy.Status == StrategyStatus.Active
                ? StrategyStatus.Paused
                : StrategyStatus.Active;

            await _strategyService.UpdateStrategyStatusAsync(strategy.Id, newStatus);
            strategy.Status = newStatus;

            var index = Strategies.IndexOf(strategy);
            if (index >= 0)
            {
                Strategies.RemoveAt(index);
                Strategies.Insert(index, strategy);
            }

            // 启动策略后若监控未运行，提示用户需在交易监控页启动监控才会自动交易
            if (newStatus == StrategyStatus.Active && !_marketMonitor.IsRunning)
            {
                await _dialogService.ShowMessageAsync(
                    "策略已启动",
                    "策略已标记为运行中，但市场监控未启动，暂不会自动交易。请到「交易监控」页面点击「启动监控」后才会根据实时价格触发交易。",
                    "知道了");
            }
        }, "切换策略状态");
    }

    [RelayCommand]
    private async Task DeleteStrategyAsync(TradingStrategy strategy)
    {
        await SafeExecuteAsync(async () =>
        {
            await _strategyService.DeleteStrategyAsync(strategy.Id);
            Strategies.Remove(strategy);
        }, "删除策略");
    }

    [RelayCommand]
    private async Task ViewHistoryAsync(TradingStrategy strategy)
    {
        await SafeExecuteAsync(async () =>
        {
            var records = await _dataService.GetRecordsByStrategyAsync(strategy.Id);

            // 刷新策略最新状态（执行次数可能已被后台引擎更新）
            var latest = await _strategyService.GetStrategyAsync(strategy.Id);
            if (latest != null)
            {
                strategy.ExecutionCount = latest.ExecutionCount;
                strategy.Status = latest.Status;
                strategy.LastTriggeredAt = latest.LastTriggeredAt;
            }

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
                    return;

                var owner = desktop.Windows.FirstOrDefault(w => w.IsActive) ?? desktop.MainWindow;
                if (owner == null) return;

                var window = new StrategyExecutionWindow();
                window.SetContent(strategy, records);
                await window.ShowDialog(owner);
            });
        }, "查看策略执行历史");
    }

    [RelayCommand]
    private async Task SaveRiskConfigAsync()
    {
        await _strategyService.SaveRiskConfigAsync(RiskConfig);
    }

    [RelayCommand]
    private void ToggleCreateForm()
    {
        IsCreating = !IsCreating;
        if (!IsCreating) ClearForm();
    }

    private void ClearForm()
    {
        NewSymbol = string.Empty;
        GridUpperPrice = string.Empty;
        GridLowerPrice = string.Empty;
        GridQuantityPerGrid = string.Empty;
        GridStopLossPrice = string.Empty;
        GridTakeProfitPrice = string.Empty;
        DcaAmountPerInterval = string.Empty;
        DcaMaxBuyPrice = string.Empty;
        DcaDoubleBuyBelowPrice = string.Empty;
        AiBudgetUsdt = string.Empty;
        AiShadowMode = false;
        ValidationError = string.Empty;
        IsCreating = false;
        ApplyScenarioPreset();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _marketMonitor.StatusChanged -= OnMonitorStatusChanged;
        GC.SuppressFinalize(this);
    }
}
