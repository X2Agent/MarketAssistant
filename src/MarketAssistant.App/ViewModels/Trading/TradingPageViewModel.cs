using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

public partial class TradingPageViewModel : ViewModelBase, IDisposable
{
    private readonly TradingEnvironmentService _tradingEnvironmentService;

    public override string Title => "交易";

    public StrategyConfigViewModel StrategyConfig { get; }
    public TradeMonitorViewModel TradeMonitor { get; }
    public TradeHistoryViewModel TradeHistory { get; }
    public ApiKeyConfigViewModel ApiKeyConfig { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    [ObservableProperty]
    private string _currentTradingModeText = string.Empty;

    [ObservableProperty]
    private string _tradingModeDescription = string.Empty;

    public bool IsConfigTabSelected => SelectedTabIndex == 0;
    public bool IsMonitorTabSelected => SelectedTabIndex == 1;
    public bool IsHistoryTabSelected => SelectedTabIndex == 2;
    public bool IsTestnetTradingMode => _tradingEnvironmentService.IsTestnetMode;

    public TradingPageViewModel(
        StrategyConfigViewModel strategyConfig,
        TradeMonitorViewModel tradeMonitor,
        TradeHistoryViewModel tradeHistory,
        ApiKeyConfigViewModel apiKeyConfig,
        TradingEnvironmentService tradingEnvironmentService,
        ILogger<TradingPageViewModel> logger)
        : base(logger)
    {
        _tradingEnvironmentService = tradingEnvironmentService;
        StrategyConfig = strategyConfig;
        TradeMonitor = tradeMonitor;
        TradeHistory = tradeHistory;
        ApiKeyConfig = apiKeyConfig;
        _tradingEnvironmentService.ModeChanged += OnTradingModeChanged;
        UpdateTradingModeState(_tradingEnvironmentService.CurrentMode);
    }

    [RelayCommand]
    private void SelectTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out var index))
            SelectedTabIndex = index;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsConfigTabSelected));
        OnPropertyChanged(nameof(IsMonitorTabSelected));
        OnPropertyChanged(nameof(IsHistoryTabSelected));

        if (value == 1)
            TradeMonitor.RefreshCommand.Execute(null);
        else if (value == 2)
            TradeHistory.RefreshCommand.Execute(null);
    }

    private void OnTradingModeChanged(CryptoTradingMode mode)
    {
        UpdateTradingModeState(mode);
    }

    private void UpdateTradingModeState(CryptoTradingMode mode)
    {
        CurrentTradingModeText = TradingEnvironmentService.GetModeDisplayName(mode);
        TradingModeDescription = TradingEnvironmentService.GetModeDescription(mode);
        OnPropertyChanged(nameof(IsTestnetTradingMode));
    }

    public void Dispose()
    {
        _tradingEnvironmentService.ModeChanged -= OnTradingModeChanged;
        TradeMonitor.Dispose();
        GC.SuppressFinalize(this);
    }
}
