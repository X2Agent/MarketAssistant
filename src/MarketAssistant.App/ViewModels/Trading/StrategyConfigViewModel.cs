using System.Collections.ObjectModel;
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
    [ObservableProperty] private OrderSide _newSide = OrderSide.Buy;
    [ObservableProperty] private string _newTriggerPrice = string.Empty;
    [ObservableProperty] private string _newQuantity = string.Empty;
    [ObservableProperty] private string _newStopLossPrice = string.Empty;
    [ObservableProperty] private string _newTakeProfitPrice = string.Empty;
    [ObservableProperty] private bool _isCreating;

    public StrategyType[] StrategyTypes { get; } = Enum.GetValues<StrategyType>()
        .Where(t => t is not (StrategyType.GridTrading or StrategyType.DCA))
        .ToArray();
    public OrderSide[] OrderSides => Enum.GetValues<OrderSide>();

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
        if (string.IsNullOrWhiteSpace(NewSymbol) ||
            !decimal.TryParse(NewTriggerPrice, out var triggerPrice) ||
            !decimal.TryParse(NewQuantity, out var quantity))
            return;

        await SafeExecuteAsync(async () =>
        {
            var strategy = new TradingStrategy
            {
                Symbol = NewSymbol.ToUpper().Trim(),
                Type = NewStrategyType,
                Status = StrategyStatus.Active,
                Side = NewSide,
                TriggerPrice = triggerPrice,
                Quantity = quantity
            };

            if (decimal.TryParse(NewStopLossPrice, out var sl))
                strategy.StopLossPrice = sl;
            if (decimal.TryParse(NewTakeProfitPrice, out var tp))
                strategy.TakeProfitPrice = tp;

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
        IsCreating = false;
    }
}
