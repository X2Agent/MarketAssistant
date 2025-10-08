using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Services;
using MarketAssistant.Applications.Stocks;
using Microsoft.Extensions.Logging;
using MarketAssistant.Applications.Stocks;
using System.Collections.ObjectModel;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Avalonia.ViewModels;
using MarketAssistant.Applications.Stocks;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 热门股票ViewModel
/// </summary>
public partial class HotStocksViewModel : ViewModelBase
{
    private readonly IHomeStockService _homeStockService;

    /// <summary>
    /// 热门股票集合
    /// </summary>
    public ObservableCollection<HotStock> HotStocks { get; } = new();

    /// <summary>
    /// 选择热门股票命令
    /// </summary>
    public IRelayCommand<HotStock> SelectHotStockCommand { get; }

    /// <summary>
    /// 添加到收藏命令
    /// </summary>
    public IAsyncRelayCommand<HotStock> AddToFavoriteCommand { get; }

    /// <summary>
    /// 刷新热门股票命令
    /// </summary>
    public IAsyncRelayCommand RefreshCommand { get; }

    /// <summary>
    /// 热门股票选择事件
    /// </summary>
    public event EventHandler<HotStock>? HotStockSelected;

    public HotStocksViewModel(IHomeStockService homeStockService, ILogger<HotStocksViewModel> logger) 
        : base(logger)
    {
        _homeStockService = homeStockService;
        
        SelectHotStockCommand = new RelayCommand<HotStock>(OnSelectHotStock);
        AddToFavoriteCommand = new AsyncRelayCommand<HotStock>(OnAddToFavoriteAsync);
        RefreshCommand = new AsyncRelayCommand(LoadHotStocksAsync);

        // 自动加载热门股票
        _ = LoadHotStocksAsync();
    }

    /// <summary>
    /// 加载热门股票
    /// </summary>
    public async Task LoadHotStocksAsync()
    {
        await SafeExecuteAsync(async () =>
        {
            var hotStocks = await _homeStockService.GetHotStocksAsync();
            
            HotStocks.Clear();
            foreach (var stock in hotStocks)
            {
                HotStocks.Add(stock);
            }
        }, "加载热门股票");
    }

    /// <summary>
    /// 选择热门股票
    /// </summary>
    private void OnSelectHotStock(HotStock? stock)
    {
        if (stock == null) return;
        
        // 通知父ViewModel
        HotStockSelected?.Invoke(this, stock);
    }

    /// <summary>
    /// 添加到收藏
    /// </summary>
    private async Task OnAddToFavoriteAsync(HotStock? stock)
    {
        if (stock == null) return;

        await SafeExecuteAsync(async () =>
        {
            await _homeStockService.AddToFavoriteAsync(stock);
        }, "添加收藏");
    }
}
