using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.InvestmentSelection.Models;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// AI 选股页 - 结果部分：推荐列表与结果头部、市场环境分析、自选星标回显，以及从推荐卡跳转资产页 / 研判页。
/// 与主视图模型拆分为 partial 类，避免单文件继续膨胀。
/// </summary>
public partial class AssetSelectionPageViewModel
{
    private DateTime _generatedAt;

    [ObservableProperty]
    private InvestmentSelectionResult? _selectionResult;

    /// <summary>「市场环境分析」默认收起，避免三段长文把推荐列表顶出首屏；每次新结果重置。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EnvironmentToggleText))]
    private bool _isEnvironmentExpanded;

    public bool HasResult => SelectionResult != null;

    public bool HasRecommendations => Recommendations.Count > 0;

    public bool HasRiskWarnings => SelectionResult?.RiskWarnings.Count > 0;

    public bool HasAppliedCriteria => !string.IsNullOrEmpty(SelectionResult?.AppliedCriteria);

    /// <summary>结果头下方的“已应用的筛选条件”文案。</summary>
    public string AppliedCriteriaText => $"筛选条件：{SelectionResult?.AppliedCriteria}";

    /// <summary>
    /// 推荐投资标的列表（用于UI绑定）。
    /// 集合实例保持不变、结果变更时原地替换内容：
    /// 每次绑定求值新建集合会让整个 ItemsControl 重建（丢滚动位置）
    /// </summary>
    public ObservableCollection<RecommendationItemViewModel> Recommendations { get; } = [];

    /// <summary>推荐列表标题旁的数量说明，如“共 5 只”。</summary>
    public string RecommendationCountText =>
        $"共 {Recommendations.Count} {(_marketContext.CurrentMarket == MarketType.Crypto ? "个" : "只")}";

    /// <summary>结果头部副标题：查询摘要 + 生成时间。</summary>
    public string ResultSubtitle => $"{QueryDescription} · 生成于 {_generatedAt:HH:mm}";

    public string EnvironmentToggleText => IsEnvironmentExpanded ? "收起市场环境分析" : "展开市场环境分析";

    /// <summary>结果头右侧的返回入口文案，如“新的选股”。</summary>
    public string NewSelectionText => $"新的{SelectionNoun}";

    /// <summary>
    /// 结果区域标题
    /// </summary>
    public string ResultTitle => _marketContext.CurrentMarket == MarketType.Crypto ? "选币结果" : "选股结果";

    /// <summary>
    /// 推荐列表标题
    /// </summary>
    public string RecommendationTitle => _marketContext.CurrentMarket == MarketType.Crypto
        ? "推荐虚拟币"
        : "推荐股票";

    private IFavoriteService CurrentFavoriteService => _marketContext.GetService<IFavoriteService>();

    partial void OnSelectionResultChanged(InvestmentSelectionResult? value)
    {
        Recommendations.Clear();
        if (value != null)
        {
            var rank = 1;
            foreach (var recommendation in value.Recommendations)
            {
                Recommendations.Add(new RecommendationItemViewModel(recommendation, rank++, _marketContext.CurrentMarket));
            }

            _ = GlobalExceptionHandler.SafeExecuteAsync(RefreshFavoriteFlagsAsync, operationName: "刷新自选状态", logger: Logger);
        }

        IsEnvironmentExpanded = false;
        OnPropertyChanged(nameof(IsComposing));
        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(HasRiskWarnings));
        OnPropertyChanged(nameof(HasAppliedCriteria));
        OnPropertyChanged(nameof(AppliedCriteriaText));
        OnPropertyChanged(nameof(RecommendationCountText));
        OnPropertyChanged(nameof(ResultSubtitle));
    }

    /// <summary>丢弃当前结果，回到输入态；输入内容与所选模式保持不变。</summary>
    [RelayCommand]
    private void NewSelection() => SelectionResult = null;

    [RelayCommand]
    private void ToggleEnvironment() => IsEnvironmentExpanded = !IsEnvironmentExpanded;

    [RelayCommand]
    private void ViewRecommendationDetail(RecommendationItemViewModel? item)
    {
        if (item == null) return;

        WeakReferenceMessenger.Default.Send(
            new NavigationMessage("Asset", new AssetNavigationParameter(item.Symbol, item.Name)));
    }

    [RelayCommand]
    private void AnalyzeRecommendation(RecommendationItemViewModel? item)
    {
        if (item == null) return;

        WeakReferenceMessenger.Default.Send(
            new NavigationMessage("Analysis", new AssetNavigationParameter(item.Symbol, item.Name)));
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(RecommendationItemViewModel? item)
    {
        if (item == null) return;

        // 不走 SafeExecuteAsync：它会切换 IsBusy，而本页的 IsBusy 表示“选股运行中”。
        // 星标不在这里改：收藏服务写库后会广播 AssetFavoritesChanged，由 Receive 统一回显
        await GlobalExceptionHandler.SafeExecuteAsync(async () =>
        {
            if (item.IsFavorite)
            {
                await CurrentFavoriteService.RemoveFavoriteAsync(item.FavoriteCode, item.FavoriteMarket);
            }
            else
            {
                await CurrentFavoriteService.AddFavoriteAsync(item.FavoriteCode, item.FavoriteMarket);
            }
        }, operationName: "更新自选", logger: Logger);
    }

    /// <summary>按收藏库的真实状态回显所有推荐卡的星标；结果生成、切换与跨页同步都走这一条路径。</summary>
    private async Task RefreshFavoriteFlagsAsync()
    {
        if (Recommendations.Count == 0)
            return;

        var favorites = await CurrentFavoriteService.GetFavoritesCodesAsync();
        var favoriteCodes = favorites.Select(favorite => favorite.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Recommendations)
        {
            item.IsFavorite = favoriteCodes.Contains(item.FavoriteCode);
        }
    }

    public void Receive(AssetFavoritesChanged message) =>
        Dispatcher.UIThread.Post(() => _ = GlobalExceptionHandler.SafeExecuteAsync(RefreshFavoriteFlagsAsync, operationName: "刷新自选状态", logger: Logger));
}
