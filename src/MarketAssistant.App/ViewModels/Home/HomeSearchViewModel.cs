using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications;
using MarketAssistant.Applications.Assets.Models;
using MarketAssistant.Applications.Home;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels.Home;

public partial class HomeSearchViewModel : ViewModelBase, IDisposable
{
    private readonly IMarketServiceRegistry _marketServiceRegistry;
    private readonly MarketContext _marketContext;
    private CancellationTokenSource? _debounceCts;
    private const int DebounceDelayMs = 200;

    private IHomeAssetService HomeAssetService =>
        _marketServiceRegistry.GetHomeAssetService(_marketContext.CurrentMarket);

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

    public ObservableCollection<AssetItem> SearchResults { get; } = new();

    public event EventHandler<AssetItem>? AssetSelected;

    public HomeSearchViewModel(
        IMarketServiceRegistry marketServiceRegistry,
        MarketContext marketContext,
        ILogger<HomeSearchViewModel> logger)
        : base(logger)
    {
        _marketServiceRegistry = marketServiceRegistry;
        _marketContext = marketContext;
    }

    /// <summary>
    /// 当 SearchQuery 变化时自动触发搜索（带200毫秒防抖）
    /// </summary>
    partial void OnSearchQueryChanged(string value)
    {
        // 只取消不 Dispose：在飞请求仍持有旧令牌，立即 Dispose 会偶发 ObjectDisposedException
        // （未释放的 CTS 无非等待 GC，安全）
        _debounceCts?.Cancel();
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

    [RelayCommand]
    private void NavigateToAsset(AssetItem? asset)
    {
        if (asset == null) return;

        IsSearchResultVisible = false;

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

    public void ClearSearch()
    {
        // 只取消不 Dispose：在飞请求仍持有旧令牌
        _debounceCts?.Cancel();
        _debounceCts = null;

        SearchQuery = string.Empty;
        SearchResults.Clear();
        SelectedResult = null;
        IsSearchResultVisible = false;
        IsSearching = false;
    }

    public void Dispose()
    {
        _debounceCts?.Cancel();
        _debounceCts = null;
        GC.SuppressFinalize(this);
    }
}
