using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

public partial class TradingPageViewModel : ViewModelBase, IDisposable
{
    public override string Title => "交易";

    public StrategyConfigViewModel StrategyConfig { get; }
    public TradeMonitorViewModel TradeMonitor { get; }
    public TradeHistoryViewModel TradeHistory { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsStrategyTabSelected => SelectedTabIndex == 0;
    public bool IsMonitorTabSelected => SelectedTabIndex == 1;
    public bool IsHistoryTabSelected => SelectedTabIndex == 2;

    public TradingPageViewModel(
        StrategyConfigViewModel strategyConfig,
        TradeMonitorViewModel tradeMonitor,
        TradeHistoryViewModel tradeHistory,
        ILogger<TradingPageViewModel> logger)
        : base(logger)
    {
        StrategyConfig = strategyConfig;
        TradeMonitor = tradeMonitor;
        TradeHistory = tradeHistory;
    }

    [RelayCommand]
    private void SelectTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out var index))
            SelectedTabIndex = index;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsStrategyTabSelected));
        OnPropertyChanged(nameof(IsMonitorTabSelected));
        OnPropertyChanged(nameof(IsHistoryTabSelected));

        if (value == 1)
            TradeMonitor.RefreshCommand.Execute(null);
        else if (value == 2)
            TradeHistory.RefreshCommand.Execute(null);
    }

    public void Dispose()
    {
        TradeMonitor.Dispose();
        GC.SuppressFinalize(this);
    }
}
