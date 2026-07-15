using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Navigation;
using MarketAssistant.Services.Trading;
using MarketAssistant.Trading.Models;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels.Trading;

/// <summary>
/// 带占比的余额显示项，用于余额详情页表格展示。
/// </summary>
public sealed class BalanceDisplayItem
{
    public string Asset { get; init; } = string.Empty;
    public decimal Free { get; init; }
    public decimal Locked { get; init; }
    public decimal ValueUSDT { get; init; }
    public decimal PortfolioPercent { get; init; }
}

public partial class BalanceDetailPageViewModel : ViewModelBase, INavigationAware
{
    private readonly CryptoPortfolioService _portfolioService;

    public ObservableCollection<BalanceDisplayItem> Balances { get; } = [];

    [ObservableProperty] private decimal _totalValueUSDT;
    [ObservableProperty] private decimal _availableUSDT;
    [ObservableProperty] private decimal _lockedValueUSDT;
    [ObservableProperty] private decimal _positionValueUSDT;
    [ObservableProperty] private decimal _positionPercent;
    [ObservableProperty] private bool _hasData;

    public override string Title => "账户余额";

    public BalanceDetailPageViewModel(
        CryptoPortfolioService portfolioService,
        ILogger<BalanceDetailPageViewModel> logger)
        : base(logger)
    {
        _portfolioService = portfolioService;
    }

    public void OnNavigatedTo(object? parameter, bool isReactivation)
    {
        if (!isReactivation)
        {
            _ = RefreshAsync();
        }
    }

    public void OnNavigatedFrom() { }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var summary = await _portfolioService.GetAccountBalanceSummaryAsync();

            TotalValueUSDT = summary.TotalValueUSDT;
            AvailableUSDT = CryptoPortfolioService.GetUsdtBalance(summary);

            Balances.Clear();
            decimal lockedTotal = 0;
            foreach (var asset in summary.Assets)
            {
                var unitPrice = GetAssetUnitPrice(asset);
                lockedTotal += asset.Locked * unitPrice;

                Balances.Add(new BalanceDisplayItem
                {
                    Asset = asset.Asset,
                    Free = asset.Free,
                    Locked = asset.Locked,
                    ValueUSDT = asset.ValueUSDT,
                    PortfolioPercent = TotalValueUSDT > 0 ? asset.ValueUSDT / TotalValueUSDT * 100 : 0
                });
            }

            LockedValueUSDT = lockedTotal;
            PositionValueUSDT = TotalValueUSDT - AvailableUSDT;
            PositionPercent = TotalValueUSDT > 0 ? PositionValueUSDT / TotalValueUSDT * 100 : 0;
            HasData = Balances.Count > 0;
        }, "刷新账户余额");
    }

    /// <summary>
    /// 获取单个资产的 USDT 折算单价（ValueUSDT / (Free + Locked)）。
    /// </summary>
    private static decimal GetAssetUnitPrice(AssetBalance asset)
    {
        var total = asset.Free + asset.Locked;
        return total > 0 ? asset.ValueUSDT / total : 0;
    }
}
