using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Home;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels.Home;

/// <summary>
/// 主页搜索功能ViewModel
/// </summary>
public partial class HomeSearchViewModel : ViewModelBase, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly MarketContext _marketContext;
    private CancellationTokenSource? _debounceCts;
    private const int DebounceDelayMs = 200;

    private IHomeAssetService HomeAssetService =>
        _serviceProvider.GetRequiredKeyedService<IHomeAssetService>(_marketContext.CurrentMarket);

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private bool _isSearchResultVisible;

    [ObservableProperty]
    private bool _isSearching;

    /// <summary>
    /// 当前键盘/鼠标高亮的结果项（Up/Down 仅移动高亮，导航由 Enter 或点击显式触发）
    /// </summary>
    [ObservableProperty]
    private AssetItem? _selectedResult;

    /// <summary>
    /// 搜索结果集合
    /// </summary>
    public ObservableCollection<AssetItem> SearchResults { get; } = new();

    /// <summary>
    /// 资产选择事件
    /// </summary>
    public event EventHandler<AssetItem>? AssetSelected;

    public HomeSearchViewModel(
        IServiceProvider serviceProvider,
        MarketContext marketContext,
        ILogger<HomeSearchViewModel> logger)
        : base(logger)
    {
        _serviceProvider = serviceProvider;
        _marketContext = marketContext;
    }

    /// <summary>
    /// 当 SearchQuery 变化时自动触发搜索（带200毫秒防抖）
    /// </summary>
    partial void OnSearchQueryChanged(string value)
    {
        // 取消之前的防抖任务
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = new CancellationTokenSource();

        var cancellationToken = _debounceCts.Token;

        // 空字符串立即清空，不需要防抖
        if (string.IsNullOrWhiteSpace(value))
        {
            IsSearching = false;
            IsSearchResultVisible = false;
            SearchResults.Clear();
            Logger?.LogDebug("搜索查询为空，清空结果");
            return;
        }

        // 触发防抖搜索：延迟与请求共用同一令牌，避免慢响应的旧查询覆盖新查询结果
        _ = DebouncedSearchAsync(value, cancellationToken);
    }

    /// <summary>
    /// 防抖后执行搜索
    /// </summary>
    private async Task DebouncedSearchAsync(string value, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DebounceDelayMs, cancellationToken);
            await SearchAsync(value, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 防抖或请求被取消，正常情况，不记录日志
            Logger?.LogDebug("搜索被取消，查询：{Query}", value);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "搜索资产时发生错误，查询：{Query}", value);
        }
    }

    /// <summary>
    /// 执行搜索并刷新结果列表
    /// </summary>
    private async Task SearchAsync(string query, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            IsSearching = false;
            IsSearchResultVisible = false;
            SearchResults.Clear();
            SelectedResult = null;
            Logger?.LogDebug("搜索查询为空，清空结果");
            return;
        }

        Logger?.LogInformation("开始搜索资产，查询：{Query}", query);
        IsSearching = true;

        await SafeExecuteAsync(async () =>
        {
            var results = await HomeAssetService.SearchAssetAsync(query, cancellationToken);

            // 请求期间查询已变化则丢弃本次结果
            cancellationToken.ThrowIfCancellationRequested();

            Logger?.LogInformation("搜索完成，找到 {Count} 个结果", results.Count);

            // 确保在 UI 线程上更新集合
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedResult = null;
                SearchResults.Clear();
                foreach (var asset in results)
                {
                    SearchResults.Add(asset);
                    Logger?.LogDebug("添加搜索结果：{Name} ({Code})", asset.Name, asset.Code);
                }

                IsSearchResultVisible = SearchResults.Count > 0;
            });

            if (results.Count == 0)
            {
                Logger?.LogWarning("未找到匹配的资产，查询：{Query}", query);
            }
        }, "搜索资产");

        IsSearching = false;
    }

    /// <summary>
    /// 选择资产
    /// </summary>
    [RelayCommand]
    private void NavigateToAsset(AssetItem? asset)
    {
        if (asset == null) return;

        // 隐藏搜索结果
        IsSearchResultVisible = false;

        // 通知父ViewModel
        AssetSelected?.Invoke(this, asset);
    }

    /// <summary>
    /// 在结果列表中移动键盘高亮项，返回是否发生了移动。
    /// 仅移动选中项不触发导航，导航由 Enter 或点击显式触发。
    /// </summary>
    public bool MoveSelection(int offset)
    {
        if (SearchResults.Count == 0)
        {
            return false;
        }

        var currentIndex = SelectedResult is null ? -1 : SearchResults.IndexOf(SelectedResult);
        var newIndex = Math.Clamp(currentIndex + offset, 0, SearchResults.Count - 1);
        var target = SearchResults[newIndex];

        if (target == SelectedResult)
        {
            return false;
        }

        SelectedResult = target;
        return true;
    }

    /// <summary>
    /// 清空搜索（包括文本和结果）
    /// </summary>
    public void ClearSearch()
    {
        // 取消防抖任务
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;

        SearchQuery = string.Empty;
        SearchResults.Clear();
        SelectedResult = null;
        IsSearchResultVisible = false;
        IsSearching = false;
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts?.Dispose();
        _debounceCts = null;
        GC.SuppressFinalize(this);
    }
}
