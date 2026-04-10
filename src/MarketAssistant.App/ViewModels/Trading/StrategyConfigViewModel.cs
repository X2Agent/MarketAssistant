using System.Collections.ObjectModel;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

public partial class StrategyConfigViewModel : ViewModelBase
{
    private readonly TradingDataService _dataService;
    private readonly MarketMonitor _marketMonitor;

    public ObservableCollection<TradingStrategy> Strategies { get; } = [];

    [ObservableProperty] private string _newSymbol = string.Empty;
    [ObservableProperty] private StrategyType _newStrategyType;

    partial void OnNewStrategyTypeChanged(StrategyType value)
    {
        OnPropertyChanged(nameof(IsGridTrading));
        OnPropertyChanged(nameof(IsDCA));
        OnPropertyChanged(nameof(IsBasicStrategy));
    }

    [ObservableProperty] private OrderSide _newSide = OrderSide.Buy;
    [ObservableProperty] private string _newTriggerPrice = string.Empty;
    [ObservableProperty] private string _newQuantity = string.Empty;
    [ObservableProperty] private string _newStopLossPrice = string.Empty;
    [ObservableProperty] private string _newTakeProfitPrice = string.Empty;
    [ObservableProperty] private bool _isCreating;

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

    // 风控配置
    [ObservableProperty] private RiskConfig _riskConfig = new();

    public StrategyConfigViewModel(
        TradingDataService dataService,
        MarketMonitor marketMonitor,
        ILogger<StrategyConfigViewModel> logger)
        : base(logger)
    {
        _dataService = dataService;
        _marketMonitor = marketMonitor;
        _riskConfig = _dataService.LoadRiskConfig();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var strategies = await _dataService.GetAllStrategiesAsync();
            Strategies.Clear();
            foreach (var s in strategies)
                Strategies.Add(s);
        }, "加载策略列表");
    }

    [RelayCommand]
    private async Task LoadStrategiesAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var strategies = await _dataService.GetAllStrategiesAsync();
            Strategies.Clear();
            foreach (var s in strategies)
                Strategies.Add(s);
        }, "加载策略列表");
    }

    [RelayCommand]
    private async Task CreateStrategyAsync()
    {
        if (string.IsNullOrWhiteSpace(NewSymbol))
            return;

        await SafeExecuteAsync(async () =>
        {
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
                        return;

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
                        return;

                    var dcaParams = new DCAParams { AmountPerInterval = amount };
                    if (int.TryParse(DcaIntervalSeconds, out var interval))
                        dcaParams.IntervalSeconds = interval;
                    if (decimal.TryParse(DcaMaxBuyPrice, out var maxPrice))
                        dcaParams.MaxBuyPrice = maxPrice;
                    if (decimal.TryParse(DcaDoubleBuyBelowPrice, out var doublePrice))
                        dcaParams.DoubleBuyBelowPrice = doublePrice;

                    strategy.CustomParams = JsonSerializer.Serialize(dcaParams);
                    strategy.TriggerPrice = maxPrice > 0 ? maxPrice : 0;
                    strategy.Quantity = amount;
                    break;

                default:
                    if (!decimal.TryParse(NewTriggerPrice, out var triggerPrice) ||
                        !decimal.TryParse(NewQuantity, out var quantity))
                        return;

                    strategy.TriggerPrice = triggerPrice;
                    strategy.Quantity = quantity;

                    if (decimal.TryParse(NewStopLossPrice, out var sl))
                        strategy.StopLossPrice = sl;
                    if (decimal.TryParse(NewTakeProfitPrice, out var tp))
                        strategy.TakeProfitPrice = tp;
                    break;
            }

            await _dataService.SaveStrategyAsync(strategy);
            Strategies.Insert(0, strategy);

            ClearForm();
            await _marketMonitor.RefreshSubscriptionsAsync();
        }, "创建策略");
    }

    [RelayCommand]
    private async Task ToggleStrategyAsync(TradingStrategy strategy)
    {
        await SafeExecuteAsync(async () =>
        {
            var newStatus = strategy.Status == StrategyStatus.Active
                ? StrategyStatus.Paused
                : StrategyStatus.Active;

            await _dataService.UpdateStrategyStatusAsync(strategy.Id, newStatus);
            strategy.Status = newStatus;

            var index = Strategies.IndexOf(strategy);
            if (index >= 0)
            {
                Strategies.RemoveAt(index);
                Strategies.Insert(index, strategy);
            }

            await _marketMonitor.RefreshSubscriptionsAsync();
        }, "切换策略状态");
    }

    [RelayCommand]
    private async Task DeleteStrategyAsync(TradingStrategy strategy)
    {
        await SafeExecuteAsync(async () =>
        {
            await _dataService.DeleteStrategyAsync(strategy.Id);
            Strategies.Remove(strategy);
            await _marketMonitor.RefreshSubscriptionsAsync();
        }, "删除策略");
    }

    [RelayCommand]
    private void SaveRiskConfig()
    {
        _dataService.SaveRiskConfig(RiskConfig);
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
        IsCreating = false;
    }
}
