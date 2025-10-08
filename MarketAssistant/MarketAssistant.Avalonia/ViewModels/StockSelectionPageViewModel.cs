using Avalonia.Threading;
using MarketAssistant.Applications.StockSelection;
using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Applications.StockSelection;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.StockSelection;
using MarketAssistant.Agents;
using MarketAssistant.Applications.StockSelection;
using MarketAssistant.Infrastructure;
using MarketAssistant.Applications.StockSelection;
using Microsoft.Extensions.Logging;
using MarketAssistant.Applications.StockSelection;
using System.Collections.ObjectModel;
using MarketAssistant.Applications.StockSelection;

namespace MarketAssistant.Avalonia.ViewModels;

/// <summary>
/// 选股模式项
/// </summary>
public partial class SelectionModeItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _icon = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private SelectionModeType _modeType;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>
/// 选股模式类型
/// </summary>
public enum SelectionModeType
{
    UserRequirement,
    NewsAnalysis,
    QuickStrategy
}

/// <summary>
/// AI选股功能的ViewModel
/// </summary>
public partial class StockSelectionPageViewModel : ViewModelBase
{
    private readonly StockSelectionService _stockSelectionService;

    [ObservableProperty]
    private string _userRequirements = string.Empty;

    [ObservableProperty]
    private string _newsContent = string.Empty;

    [ObservableProperty]
    private string _inputContent = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SelectionModeItem> _selectionModes = new();

    [ObservableProperty]
    private SelectionModeItem? _selectedMode;

    [ObservableProperty]
    private StockSelectionResult? _selectionResult;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private ObservableCollection<QuickSelectionStrategyInfo> _quickStrategies = new();

    /// <summary>
    /// 输入区域是否可见（非快速策略模式时显示）
    /// </summary>
    public bool IsInputAreaVisible => SelectedMode?.ModeType != SelectionModeType.QuickStrategy;

    /// <summary>
    /// 快速策略区域是否可见（快速策略模式时显示）
    /// </summary>
    public bool IsQuickStrategyAreaVisible => SelectedMode?.ModeType == SelectionModeType.QuickStrategy;

    /// <summary>
    /// 当前占位符文本
    /// </summary>
    public string CurrentPlaceholder => SelectedMode?.ModeType switch
    {
        SelectionModeType.UserRequirement => "请描述您的选股需求，例如：寻找市值在100-500亿之间，PE低于20倍，近期涨幅不超过10%的价值股",
        SelectionModeType.NewsAnalysis => "请输入新闻内容或热点信息，例如：央行降准利好银行股，新能源汽车销量创新高等",
        _ => "请输入内容"
    };

    /// <summary>
    /// 当前按钮文本
    /// </summary>
    public string CurrentButtonText => SelectedMode?.ModeType switch
    {
        SelectionModeType.UserRequirement => "开始选股",
        SelectionModeType.NewsAnalysis => "基于新闻选股",
        _ => "开始分析"
    };

    /// <summary>
    /// 推荐股票列表（用于UI绑定）
    /// </summary>
    public ObservableCollection<StockRecommendation> RecommendedStocks =>
        SelectionResult?.Recommendations != null
            ? new ObservableCollection<StockRecommendation>(SelectionResult.Recommendations)
            : new ObservableCollection<StockRecommendation>();

    /// <summary>
    /// 格式化的风险提示文本（用于UI绑定）
    /// </summary>
    public string FormattedRiskWarnings =>
        SelectionResult?.RiskWarnings != null && SelectionResult.RiskWarnings.Count > 0
            ? string.Join("\n• ", new[] { "" }.Concat(SelectionResult.RiskWarnings))
            : string.Empty;

    /// <summary>
    /// 是否有风险提示
    /// </summary>
    public bool HasRiskWarnings =>
        SelectionResult?.RiskWarnings != null && SelectionResult.RiskWarnings.Count > 0;

    /// <summary>
    /// 构造函数（使用依赖注入）
    /// </summary>
    public StockSelectionPageViewModel(
        ILogger<StockSelectionPageViewModel> logger,
        StockSelectionService stockSelectionService) : base(logger)
    {
        _stockSelectionService = stockSelectionService;
        _ = LoadQuickStrategiesAsync();
        _ = LoadSelectionModesAsync();
    }

    partial void OnSelectedModeChanged(SelectionModeItem? value)
    {
        if (value != null)
        {
            UpdateCurrentMode();
            OnPropertyChanged(nameof(CurrentPlaceholder));
            OnPropertyChanged(nameof(CurrentButtonText));
            OnPropertyChanged(nameof(IsInputAreaVisible));
            OnPropertyChanged(nameof(IsQuickStrategyAreaVisible));
        }
    }

    partial void OnSelectionResultChanged(StockSelectionResult? value)
    {
        OnPropertyChanged(nameof(RecommendedStocks));
        OnPropertyChanged(nameof(FormattedRiskWarnings));
        OnPropertyChanged(nameof(HasRiskWarnings));
    }

    [RelayCommand]
    private async Task ExecuteAnalysisAsync()
    {
        if (SelectedMode == null)
            return;

        switch (SelectedMode.ModeType)
        {
            case SelectionModeType.UserRequirement:
                UserRequirements = InputContent;
                await ExecuteSelectionAsync();
                break;
            case SelectionModeType.NewsAnalysis:
                NewsContent = InputContent;
                await ExecuteNewsSelectionAsync();
                break;
            case SelectionModeType.QuickStrategy:
                await ShowQuickSelectionAsync();
                break;
        }
    }

    [RelayCommand]
    private void SelectMode(SelectionModeItem? mode)
    {
        if (mode == null) return;

        foreach (var item in SelectionModes)
        {
            item.IsSelected = item == mode;
        }

        SelectedMode = mode;
    }

    [RelayCommand]
    private async Task ShowQuickSelectionAsync()
    {
        var firstStrategy = QuickStrategies.FirstOrDefault();
        if (firstStrategy != null)
        {
            await ExecuteQuickSelectionAsync(firstStrategy);
        }
    }

    [RelayCommand]
    private async Task ExecuteQuickSelectionAsync(QuickSelectionStrategyInfo? strategy)
    {
        if (strategy == null)
            return;

        await SafeExecuteAsync(async () =>
        {
            var result = await _stockSelectionService.QuickSelectAsync(strategy.Strategy);
            SelectionResult = result;
            HasResult = result != null && (
                (result.Recommendations?.Count ?? 0) > 0 ||
                !string.IsNullOrWhiteSpace(result.AnalysisSummary));

            UserRequirements = $"快速选股：{strategy.Name}";
        }, $"执行快速选股：{strategy.Name}");
    }

    [RelayCommand]
    private void ClearContent()
    {
        InputContent = string.Empty;
        ClearResult();
    }

    private async Task ExecuteSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(UserRequirements))
            return;

        await SafeExecuteAsync(async () =>
        {
            var request = new StockRecommendationRequest
            {
                UserRequirements = UserRequirements
            };

            var result = await _stockSelectionService.RecommendStocksByUserRequirementAsync(request);
            SelectionResult = result;
            HasResult = result != null && (
                (result.Recommendations?.Count ?? 0) > 0 ||
                !string.IsNullOrWhiteSpace(result.AnalysisSummary));
        }, "执行AI选股");
    }

    private async Task ExecuteNewsSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewsContent))
            return;

        await SafeExecuteAsync(async () =>
        {
            var request = new NewsBasedSelectionRequest
            {
                NewsContent = NewsContent,
                MaxRecommendations = 5
            };

            var result = await _stockSelectionService.RecommendStocksByNewsAsync(request);
            SelectionResult = result;
            HasResult = result != null && (
                (result.Recommendations?.Count ?? 0) > 0 ||
                !string.IsNullOrWhiteSpace(result.AnalysisSummary));
        }, "执行新闻选股");
    }

    private void ClearResult()
    {
        SelectionResult = null;
        HasResult = false;
        UserRequirements = string.Empty;
        NewsContent = string.Empty;
    }

    private async Task LoadQuickStrategiesAsync()
    {
        await SafeExecuteAsync(() =>
        {
            var strategies = _stockSelectionService.GetQuickSelectionStrategies();
            Dispatcher.UIThread.InvokeAsync(() =>
            {
                QuickStrategies.Clear();
                foreach (var strategy in strategies)
                {
                    QuickStrategies.Add(strategy);
                }
            });
            return Task.CompletedTask;
        }, "加载快速选股策略");
    }

    private async Task LoadSelectionModesAsync()
    {
        await SafeExecuteAsync(() =>
        {
            var modes = new ObservableCollection<SelectionModeItem>
            {
                new SelectionModeItem { Name = "用户需求", Icon = "👤", Description = "根据用户输入的选股需求进行选股", ModeType = SelectionModeType.UserRequirement, IsSelected = true },
                new SelectionModeItem { Name = "新闻分析", Icon = "📰", Description = "根据新闻内容进行选股", ModeType = SelectionModeType.NewsAnalysis },
                new SelectionModeItem { Name = "快速策略", Icon = "⚡", Description = "使用预设的快速选股策略", ModeType = SelectionModeType.QuickStrategy }
            };

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectionModes.Clear();
                foreach (var mode in modes)
                {
                    SelectionModes.Add(mode);
                }
                SelectedMode = SelectionModes.FirstOrDefault();
            });
            return Task.CompletedTask;
        }, "加载选股模式");
    }

    private void UpdateCurrentMode()
    {
        if (SelectedMode != null)
        {
            switch (SelectedMode.ModeType)
            {
                case SelectionModeType.UserRequirement:
                    InputContent = UserRequirements;
                    break;
                case SelectionModeType.NewsAnalysis:
                    InputContent = NewsContent;
                    break;
            }
        }
    }
}
