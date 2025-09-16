using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 最近查看股票ViewModel
/// </summary>
public partial class RecentStocksViewModel : ViewModelBase
{
    private readonly IHomeStockService _homeStockService;

    /// <summary>
    /// 最近查看股票集合
    /// </summary>
    public ObservableCollection<StockItem> RecentStocks { get; } = new();

    /// <summary>
    /// 选择最近股票命令
    /// </summary>
    public IRelayCommand<StockItem> SelectRecentStockCommand { get; }

    /// <summary>
    /// 添加到收藏命令
    /// </summary>
    public IAsyncRelayCommand<StockItem> AddToFavoriteCommand { get; }

    /// <summary>
    /// 刷新最近股票命令
    /// </summary>
    public IRelayCommand RefreshCommand { get; }

    /// <summary>
    /// 最近股票选择事件
    /// </summary>
    public event EventHandler<StockItem>? RecentStockSelected;

    public RecentStocksViewModel(IHomeStockService homeStockService, ILogger<RecentStocksViewModel> logger) 
        : base(logger)
    {
        _homeStockService = homeStockService;
        
        SelectRecentStockCommand = new RelayCommand<StockItem>(OnSelectRecentStock);
        AddToFavoriteCommand = new AsyncRelayCommand<StockItem>(OnAddToFavoriteAsync);
        RefreshCommand = new RelayCommand(LoadRecentStocks);

        // 自动加载最近股票
        LoadRecentStocks();
    }

    /// <summary>
    /// 加载最近查看股票
    /// </summary>
    public void LoadRecentStocks()
    {
        SafeExecute(() =>
        {
            var recentStocks = _homeStockService.GetRecentStocks();
            
            RecentStocks.Clear();
            foreach (var stock in recentStocks)
            {
                RecentStocks.Add(stock);
            }
        }, "加载最近查看股票");
    }

    /// <summary>
    /// 添加股票到最近查看
    /// </summary>
    public void AddToRecentStocks(StockItem stock)
    {
        SafeExecute(() =>
        {
            _homeStockService.AddToRecentStocks(stock);
            LoadRecentStocks(); // 刷新列表
        }, "添加到最近查看");
    }

    /// <summary>
    /// 选择最近股票
    /// </summary>
    private void OnSelectRecentStock(StockItem? stock)
    {
        if (stock == null) return;
        
        // 通知父ViewModel
        RecentStockSelected?.Invoke(this, stock);
    }

    /// <summary>
    /// 添加到收藏
    /// </summary>
    private async Task OnAddToFavoriteAsync(StockItem? stock)
    {
        if (stock == null) return;

        await SafeExecuteAsync(async () =>
        {
            await _homeStockService.AddToFavoriteAsync(stock);
        }, "添加收藏");
    }
}
