using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Portfolio;
using MarketAssistant.Applications.Portfolio.Models;
using MarketAssistant.Infrastructure;
using MarketAssistant.Services.Data;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Notification;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using static MarketAssistant.Infrastructure.Core.CryptoSymbolConverter;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 持仓组合管理页面 ViewModel
/// </summary>
public partial class PortfolioPageViewModel : ViewModelBase, IDisposable
{
    public override string Title => "持仓管理";

    private readonly PortfolioService _portfolioService;
    private readonly BinanceWebSocketService _wsService;
    private readonly MarketContext _marketContext;
    private readonly INotificationService _notificationService;

    public ObservableCollection<PortfolioPosition> Positions { get; } = [];

    [ObservableProperty] private decimal _totalMarketValue;
    [ObservableProperty] private decimal _totalCost;
    [ObservableProperty] private decimal _totalProfitLoss;
    [ObservableProperty] private decimal _totalProfitLossPercent;

    [ObservableProperty] private string _tradeAssetCode = string.Empty;
    [ObservableProperty] private string _tradeAssetName = string.Empty;
    [ObservableProperty] private string _tradePrice = string.Empty;
    [ObservableProperty] private string _tradeQuantity = string.Empty;

    public PortfolioPageViewModel(
        PortfolioService portfolioService,
        BinanceWebSocketService wsService,
        MarketContext marketContext,
        INotificationService notificationService,
        ILogger<PortfolioPageViewModel> logger) : base(logger)
    {
        _portfolioService = portfolioService;
        _wsService = wsService;
        _marketContext = marketContext;
        _notificationService = notificationService;

        _portfolioService.PortfolioChanged += RefreshPositions;
        _wsService.PriceUpdated += OnPriceUpdated;

        RefreshPositions();
        SubscribeWebSocket();
    }

    [RelayCommand]
    private void ExecuteBuy()
    {
        if (!ValidateTradeInput(out var price, out var quantity)) return;

        _portfolioService.Buy(
            TradeAssetCode.Trim(),
            string.IsNullOrWhiteSpace(TradeAssetName) ? TradeAssetCode.Trim() : TradeAssetName.Trim(),
            _marketContext.CurrentMarket,
            price, quantity);

        _notificationService.ShowSuccess($"买入 {TradeAssetCode} {quantity} @ {price}");
        ClearTradeForm();
    }

    [RelayCommand]
    private void ExecuteSell()
    {
        if (!ValidateTradeInput(out var price, out var quantity)) return;

        var success = _portfolioService.Sell(TradeAssetCode.Trim(), price, quantity);
        if (success)
        {
            _notificationService.ShowSuccess($"卖出 {TradeAssetCode} {quantity} @ {price}");
            ClearTradeForm();
        }
        else
        {
            _notificationService.ShowWarning("持仓不足或未找到该资产");
        }
    }

    [RelayCommand]
    private void RemovePosition(PortfolioPosition? position)
    {
        if (position == null) return;
        _portfolioService.RemovePosition(position.Id);
    }

    private bool ValidateTradeInput(out decimal price, out decimal quantity)
    {
        price = 0;
        quantity = 0;

        if (string.IsNullOrWhiteSpace(TradeAssetCode))
        {
            _notificationService.ShowWarning("请输入资产代码");
            return false;
        }
        if (!decimal.TryParse(TradePrice, out price) || price <= 0)
        {
            _notificationService.ShowWarning("请输入有效的价格");
            return false;
        }
        if (!decimal.TryParse(TradeQuantity, out quantity) || quantity <= 0)
        {
            _notificationService.ShowWarning("请输入有效的数量");
            return false;
        }
        return true;
    }

    private void ClearTradeForm()
    {
        TradeAssetCode = string.Empty;
        TradeAssetName = string.Empty;
        TradePrice = string.Empty;
        TradeQuantity = string.Empty;
    }

    private void RefreshPositions()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Positions.Clear();
            foreach (var p in _portfolioService.Positions)
                Positions.Add(p);

            TotalMarketValue = _portfolioService.TotalMarketValue;
            TotalCost = _portfolioService.TotalCost;
            TotalProfitLoss = _portfolioService.TotalProfitLoss;
            TotalProfitLossPercent = TotalCost > 0
                ? Math.Round(TotalProfitLoss / TotalCost * 100, 2)
                : 0;
        });
    }

    private void SubscribeWebSocket()
    {
        var symbols = _portfolioService.Positions
            .Where(p => p.MarketType == Infrastructure.Core.MarketType.Crypto)
            .Select(p => ToBinanceFormat(p.AssetCode))
            .Distinct()
            .ToList();

        if (symbols.Count > 0)
            _ = _wsService.SubscribeAsync(symbols);
    }

    private void OnPriceUpdated(string symbol, decimal lastPrice, decimal changePercent)
    {
        var pos = _portfolioService.Positions.FirstOrDefault(
            p => ToBinanceFormat(p.AssetCode).Equals(symbol, StringComparison.OrdinalIgnoreCase));

        if (pos == null) return;

        _portfolioService.UpdateCurrentPrice(pos.AssetCode, lastPrice);
        RefreshPositions();
    }

    public void Dispose()
    {
        _portfolioService.PortfolioChanged -= RefreshPositions;
        _wsService.PriceUpdated -= OnPriceUpdated;
        GC.SuppressFinalize(this);
    }
}
