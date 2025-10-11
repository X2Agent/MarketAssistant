using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Applications.Stocks.Models;
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

    [ObservableProperty]
    private bool _isSearching;

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
    /// 当 SearchQuery 变化时自动触发搜索
    /// </summary>
    partial void OnSearchQueryChanged(string value)
    {
        // 触发搜索（空字符串会在 OnSearchAsync 中自动处理）
        _ = Task.Run(async () =>
        {
            try
            {
                await OnSearchAsync(value);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "搜索股票时发生错误，查询：{Query}", value);
            }
        });
    }

    /// <summary>
    /// 执行搜索
    /// </summary>
    private async Task OnSearchAsync(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearching = false;
            IsSearchResultVisible = false;
            SearchResults.Clear();
            Logger?.LogDebug("搜索查询为空，清空结果");
            return;
        }

        Logger?.LogInformation("开始搜索股票，查询：{Query}", query);
        IsSearching = true;

        try
        {
            var results = await _homeStockService.SearchStockAsync(query, CancellationToken.None);

            Logger?.LogInformation("搜索完成，找到 {Count} 个结果", results.Count);

            // 确保在 UI 线程上更新集合
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SearchResults.Clear();
                foreach (var stock in results)
                {
                    SearchResults.Add(stock);
                    Logger?.LogDebug("添加搜索结果：{Name} ({Code})", stock.Name, stock.Code);
                }

                IsSearchResultVisible = SearchResults.Count > 0;
            });
            
            if (results.Count == 0)
            {
                Logger?.LogWarning("未找到匹配的股票，查询：{Query}", query);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "搜索股票失败，查询：{Query}", query);
            
            // 确保在异常时也清空结果
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SearchResults.Clear();
                IsSearchResultVisible = false;
            });
        }
        finally
        {
            IsSearching = false;
        }
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
    /// 清空搜索（包括文本和结果）
    /// </summary>
    public void ClearSearch()
    {
        SearchQuery = string.Empty;
        SearchResults.Clear();
        IsSearchResultVisible = false;
        IsSearching = false;
    }
}
