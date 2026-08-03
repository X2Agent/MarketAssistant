using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

public partial class TradeHistoryViewModel : ViewModelBase
{
    private readonly TradingDataService _dataService;

    public ObservableCollection<TradeRecord> Records { get; } = [];

    [ObservableProperty] private string _filterSymbol = string.Empty;
    [ObservableProperty] private DailyStats _todayStats = new();

    public TradeHistoryViewModel(
        TradingDataService dataService,
        ILogger<TradeHistoryViewModel> logger)
        : base(logger)
    {
        _dataService = dataService;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var symbol = string.IsNullOrWhiteSpace(FilterSymbol) ? null : FilterSymbol.Trim().ToUpper();
            var records = await _dataService.GetTradeRecordsAsync(symbol: symbol, limit: 100);

            Records.Clear();
            foreach (var r in records)
                Records.Add(r);

            TodayStats = await _dataService.GetTodayStatsAsync();
        }, "加载交易历史");
    }

    [RelayCommand]
    private async Task FilterAsync()
    {
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ClearFilterAsync()
    {
        FilterSymbol = string.Empty;
        await RefreshAsync();
    }
}
