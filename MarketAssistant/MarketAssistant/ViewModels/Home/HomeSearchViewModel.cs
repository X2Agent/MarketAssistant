using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Stocks.Models;
using MarketAssistant.Services;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 主页搜索功能ViewModel
/// </summary>
public partial class HomeSearchViewModel : ViewModelBase
{
    private readonly IHomeStockService _homeStockService;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearchResultVisible;

    /// <summary>
    /// 搜索结果集合
    /// </summary>
    public ObservableCollection<StockItem> SearchResults { get; } = new();

    /// <summary>
    /// 搜索命令
    /// </summary>
    public IAsyncRelayCommand<string> SearchCommand { get; }

    /// <summary>
    /// 选择股票命令
    /// </summary>
    public IRelayCommand<StockItem> SelectStockCommand { get; }

    /// <summary>
    /// 股票选择事件
    /// </summary>
    public event EventHandler<StockItem>? StockSelected;

    public HomeSearchViewModel(IHomeStockService homeStockService, ILogger<HomeSearchViewModel> logger) 
        : base(logger)
    {
        _homeStockService = homeStockService;
        
        SearchCommand = new AsyncRelayCommand<string>(OnSearchAsync);
        SelectStockCommand = new RelayCommand<StockItem>(OnSelectStock);
    }

    /// <summary>
    /// 执行搜索
    /// </summary>
    private async Task OnSearchAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearchResultVisible = false;
            SearchResults.Clear();
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var results = await _homeStockService.SearchStockAsync(query, CancellationToken.None);
            
            SearchResults.Clear();
            foreach (var stock in results)
            {
                SearchResults.Add(stock);
            }
            
            IsSearchResultVisible = true;
        }, "搜索股票");
    }

    /// <summary>
    /// 选择股票
    /// </summary>
    private void OnSelectStock(StockItem? stock)
    {
        if (stock == null) return;

        // 隐藏搜索结果
        IsSearchResultVisible = false;
        
        // 通知父ViewModel
        StockSelected?.Invoke(this, stock);
    }

    /// <summary>
    /// 清除搜索结果
    /// </summary>
    public void ClearSearchResults()
    {
        SearchResults.Clear();
        IsSearchResultVisible = false;
        SearchQuery = string.Empty;
    }
}
