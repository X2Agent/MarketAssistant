using System.Collections.ObjectModel;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Extensions;
using MarketAssistant.Services.Dialog;
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
    private bool _disposed;

    public ObservableCollection<TradingStrategy> Strategies { get; } = [];

    [ObservableProperty] private string _newSymbol = string.Empty;
    [ObservableProperty] private StrategyType _newStrategyType;

    partial void OnNewStrategyTypeChanged(StrategyType value)
    {
        OnPropertyChanged(nameof(IsGridTrading));
        OnPropertyChanged(nameof(IsDCA));
        OnPropertyChanged(nameof(IsBasicStrategy));
        OnPropertyChanged(nameof(SideHintText));
    }

    [ObservableProperty] private OrderSide _newSide = OrderSide.Buy;

    partial void OnNewSideChanged(OrderSide value)
    {
        OnPropertyChanged(nameof(SideHintText));
    }

    [ObservableProperty] private string _newTriggerPrice = string.Empty;
    [ObservableProperty] private string _newQuantity = string.Empty;
    [ObservableProperty] private string _newStopLossPrice = string.Empty;
    [ObservableProperty] private string _newTakeProfitPrice = string.Empty;
    [ObservableProperty] private bool _isCreating;

    /// <summary>
    /// 表单校验错误信息。非空时显示在创建按钮旁。
    /// </summary>
    [ObservableProperty] private string _validationError = string.Empty;

    public StrategyType[] StrategyTypes { get; } = Enum.GetValues<StrategyType>();
    public OrderSide[] OrderSides => Enum.GetValues<OrderSide>();

    // Grid Trading 参数
    [ObservableProperty] private string _gridUpperPrice = string.Empty;
    [ObservableProperty] private string _gridLowerPrice = string.Empty;
    [ObservableProperty] private string _gridCount = "10";
    [ObservableProperty] private string _gridQuantityPerGrid = string.Empty;

    // DCA 参数
    [ObservableProperty] private string _dcaIntervalSeconds = "86400";
    [ObservableProperty] private string _dcaAmountPerInterval = string.Empty;
    [ObservableProperty] private string _dcaMaxBuyPrice = string.Empty;
    [ObservableProperty] private string _dcaDoubleBuyBelowPrice = string.Empty;

    /// <summary>
    /// 当前选择的策略类型是否为网格交易
    /// </summary>
    public bool IsGridTrading => NewStrategyType == StrategyType.GridTrading;

    /// <summary>
    /// 当前选择的策略类型是否为定投
    /// </summary>
    public bool IsDCA => NewStrategyType == StrategyType.DCA;

    /// <summary>
    /// 当前选择的策略类型是否为基础策略（非 Grid/DCA）
    /// </summary>
    public bool IsBasicStrategy => !IsGridTrading && !IsDCA;

    /// <summary>
    /// 针对现货交易者的方向提示：买入止损/止盈通常用于空头对冲，现货做多应选卖出。
    /// </summary>
    public string SideHintText =>
        NewSide == OrderSide.Buy &&
        (NewStrategyType == StrategyType.StopLoss || NewStrategyType == StrategyType.TakeProfit)
            ? "⚠️ 买入方向的止损/止盈通常用于空头对冲（期货）；现货多头持仓请选「卖出」方向"
            : string.Empty;

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
        ILogger<StrategyConfigViewModel> logger)
        : base(logger)
    {
        _strategyService = strategyService;
        _dataService = dataService;
        _marketMonitor = marketMonitor;
        _dialogService = dialogService;
        IsMonitorRunning = _marketMonitor.IsRunning;
        _marketMonitor.StatusChanged += OnMonitorStatusChanged;
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

            var strategy = new TradingStrategy
            {
                Symbol = NewSymbol.ToUpper().Trim(),
                Type = NewStrategyType,
                Status = StrategyStatus.Active,
                Side = NewSide
            };

            switch (NewStrategyType)
            {
                case StrategyType.GridTrading:
                    if (!decimal.TryParse(GridUpperPrice, out var upper) ||
                        !decimal.TryParse(GridLowerPrice, out var lower) ||
                        !int.TryParse(GridCount, out var gridCount) ||
                        !decimal.TryParse(GridQuantityPerGrid, out var qtyPerGrid))
                    {
                        ValidationError = "请填写完整的网格交易参数（上界价格、下界价格、网格数量、每格数量）";
                        return;
                    }

                    var gridParams = new GridTradingParams
                    {
                        UpperPrice = upper,
                        LowerPrice = lower,
                        GridCount = gridCount,
                        QuantityPerGrid = qtyPerGrid
                    };
                    strategy.CustomParams = JsonSerializer.Serialize(gridParams);
                    strategy.TriggerPrice = lower; // 用下界作为参考触发价
                    strategy.Quantity = qtyPerGrid;
                    break;

                case StrategyType.DCA:
                    if (!decimal.TryParse(DcaAmountPerInterval, out var amount))
                    {
                        ValidationError = "请填写有效的定投金额（USDT）";
                        return;
                    }

                    var dcaParams = new DCAParams { AmountPerInterval = amount };
                    if (int.TryParse(DcaIntervalSeconds, out var interval))
                        dcaParams.IntervalSeconds = interval;
                    if (decimal.TryParse(DcaMaxBuyPrice, out var maxPrice))
                        dcaParams.MaxBuyPrice = maxPrice;
                    if (decimal.TryParse(DcaDoubleBuyBelowPrice, out var doublePrice))
                        dcaParams.DoubleBuyBelowPrice = doublePrice;

                    strategy.CustomParams = JsonSerializer.Serialize(dcaParams);
                    strategy.TriggerPrice = maxPrice > 0 ? maxPrice : 0;
                    // DCA 的 Quantity 存储每次定投的 USDT 金额（代币数量在执行时按实时价格换算）
                    strategy.Quantity = amount;
                    break;

                default:
                    if (!decimal.TryParse(NewTriggerPrice, out var triggerPrice) ||
                        !decimal.TryParse(NewQuantity, out var quantity))
                    {
                        ValidationError = "请填写有效的触发价格和交易数量";
                        return;
                    }

                    strategy.TriggerPrice = triggerPrice;
                    strategy.Quantity = quantity;

                    if (decimal.TryParse(NewStopLossPrice, out var sl))
                        strategy.StopLossPrice = sl;
                    if (decimal.TryParse(NewTakeProfitPrice, out var tp))
                        strategy.TakeProfitPrice = tp;
                    break;
            }

            await _strategyService.SaveStrategyAsync(strategy);
            Strategies.Insert(0, strategy);

            ClearForm();
        }, "创建策略");
    }

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
        NewTriggerPrice = string.Empty;
        NewQuantity = string.Empty;
        NewStopLossPrice = string.Empty;
        NewTakeProfitPrice = string.Empty;
        GridUpperPrice = string.Empty;
        GridLowerPrice = string.Empty;
        GridCount = "10";
        GridQuantityPerGrid = string.Empty;
        DcaIntervalSeconds = "86400";
        DcaAmountPerInterval = string.Empty;
        DcaMaxBuyPrice = string.Empty;
        DcaDoubleBuyBelowPrice = string.Empty;
        ValidationError = string.Empty;
        IsCreating = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _marketMonitor.StatusChanged -= OnMonitorStatusChanged;
        GC.SuppressFinalize(this);
    }
}
