using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 选股模式项
/// </summary>
public class SelectionModeItem : ObservableObject
{
    public string Name { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public SelectionModeType ModeType { get; set; }
    
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
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
public class StockSelectionViewModel : ViewModelBase
{
    private readonly StockSelectionService _stockSelectionService;

    private string _userRequirements = string.Empty;
    /// <summary>
    /// 用户选股需求
    /// </summary>
    public string UserRequirements
    {
        get => _userRequirements;
        set => SetProperty(ref _userRequirements, value);
    }

    private string _newsContent = string.Empty;
    /// <summary>
    /// 新闻内容
    /// </summary>
    public string NewsContent
    {
        get => _newsContent;
        set => SetProperty(ref _newsContent, value);
    }

    private string _inputContent = string.Empty;
    /// <summary>
    /// 统一输入内容
    /// </summary>
    public string InputContent
    {
        get => _inputContent;
        set => SetProperty(ref _inputContent, value);
    }

    private ObservableCollection<SelectionModeItem> _selectionModes = new();
    /// <summary>
    /// 选股模式列表
    /// </summary>
    public ObservableCollection<SelectionModeItem> SelectionModes
    {
        get => _selectionModes;
        set => SetProperty(ref _selectionModes, value);
    }

    private SelectionModeItem? _selectedMode;
    /// <summary>
    /// 当前选中的模式
    /// </summary>
    public SelectionModeItem? SelectedMode
    {
        get => _selectedMode;
        set
        {
            if (SetProperty(ref _selectedMode, value))
            {
                UpdateCurrentMode();
                OnPropertyChanged(nameof(CurrentPlaceholder));
                OnPropertyChanged(nameof(CurrentButtonText));
                OnPropertyChanged(nameof(IsInputAreaVisible));
                OnPropertyChanged(nameof(IsQuickStrategyAreaVisible));
            }
        }
    }

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

    private StockSelectionResult? _selectionResult;
    /// <summary>
    /// 选股结果
    /// </summary>
    public StockSelectionResult? SelectionResult
    {
        get => _selectionResult;
        set
        {
            if (SetProperty(ref _selectionResult, value))
            {
                OnPropertyChanged(nameof(RecommendedStocks));
                OnPropertyChanged(nameof(FormattedRiskWarnings));
                OnPropertyChanged(nameof(HasRiskWarnings));
            }
        }
    }

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

    private bool _hasResult;
    /// <summary>
    /// 是否有选股结果
    /// </summary>
    public bool HasResult
    {
        get => _hasResult;
        set => SetProperty(ref _hasResult, value);
    }

    private ObservableCollection<QuickSelectionStrategyInfo> _quickStrategies = new();
    /// <summary>
    /// 快速选股策略列表
    /// </summary>
    public ObservableCollection<QuickSelectionStrategyInfo> QuickStrategies
    {
        get => _quickStrategies;
        set => SetProperty(ref _quickStrategies, value);
    }

    /// <summary>
    /// 执行分析命令（统一的执行命令）
    /// </summary>
    public IRelayCommand ExecuteAnalysisCommand { get; private set; }

    /// <summary>
    /// 选择模式命令
    /// </summary>
    public IRelayCommand<SelectionModeItem> SelectModeCommand { get; private set; }

    /// <summary>
    /// 显示快速选股命令
    /// </summary>
    public IRelayCommand ShowQuickSelectionCommand { get; private set; }

    /// <summary>
    /// 执行快速选股命令
    /// </summary>
    public IRelayCommand<QuickSelectionStrategyInfo> ExecuteQuickSelectionCommand { get; private set; }

    /// <summary>
    /// 清除内容命令
    /// </summary>
    public IRelayCommand ClearContentCommand { get; private set; }

    /// <summary>
    /// 执行AI选股命令（保留兼容性）
    /// </summary>
    public IRelayCommand ExecuteSelectionCommand { get; private set; }

    /// <summary>
    /// 执行新闻选股命令（保留兼容性）
    /// </summary>
    public IRelayCommand ExecuteNewsSelectionCommand { get; private set; }

    /// <summary>
    /// 清除结果命令（保留兼容性）
    /// </summary>
    public IRelayCommand ClearResultCommand { get; private set; }

    /// <summary>
    /// 清除新闻命令（保留兼容性）
    /// </summary>
    public IRelayCommand ClearNewsCommand { get; private set; }

    public StockSelectionViewModel(
        ILogger<StockSelectionViewModel> logger,
        StockSelectionService stockSelectionService) : base(logger)
    {
        _stockSelectionService = stockSelectionService;

        // 新的统一命令
        ExecuteAnalysisCommand = new RelayCommand(async () => await ExecuteAnalysisAsync());
        SelectModeCommand = new RelayCommand<SelectionModeItem>(SelectMode);
        ShowQuickSelectionCommand = new RelayCommand(async () => await ShowQuickSelectionAsync());
        ClearContentCommand = new RelayCommand(ClearContent);

        // 保留的兼容性命令
        ExecuteSelectionCommand = new RelayCommand(async () => await ExecuteSelectionAsync());
        ExecuteNewsSelectionCommand = new RelayCommand(async () => await ExecuteNewsSelectionAsync());
        ExecuteQuickSelectionCommand = new RelayCommand<QuickSelectionStrategyInfo>(async (strategy) => await ExecuteQuickSelectionAsync(strategy));
        ClearResultCommand = new RelayCommand(ClearResult);
        ClearNewsCommand = new RelayCommand(ClearNews);

        _ = LoadQuickStrategiesAsync();
        _ = LoadSelectionModesAsync();
    }

    /// <summary>
    /// 执行AI选股
    /// </summary>
    private async Task ExecuteSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(UserRequirements))
        {
            if (Application.Current?.Windows?.FirstOrDefault()?.Page != null)
                await Application.Current.Windows.First().Page.DisplayAlert("提示", "请输入选股需求", "确定");
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            // 创建用户需求选股请求
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

    /// <summary>
    /// 执行新闻选股
    /// </summary>
    private async Task ExecuteNewsSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewsContent))
        {
            if (Application.Current?.Windows?.FirstOrDefault()?.Page != null)
                await Application.Current.Windows.First().Page.DisplayAlert("提示", "请输入新闻内容", "确定");
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            // 创建新闻选股请求
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

    /// <summary>
    /// 执行快速选股
    /// </summary>
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

            // 更新用户需求显示为所选策略
            UserRequirements = $"快速选股：{strategy.Name}";
        }, $"执行快速选股：{strategy.Name}");
    }

    /// <summary>
    /// 清除选股结果
    /// </summary>
    private void ClearResult()
    {
        SelectionResult = null;
        HasResult = false;
        UserRequirements = string.Empty;
        NewsContent = string.Empty;
    }

    /// <summary>
    /// 清除新闻内容
    /// </summary>
    private void ClearNews()
    {
        NewsContent = string.Empty;
    }

    /// <summary>
    /// 加载快速选股策略
    /// </summary>
    private async Task LoadQuickStrategiesAsync()
    {
        await SafeExecuteAsync(() =>
        {
            var strategies = _stockSelectionService.GetQuickSelectionStrategies();
            QuickStrategies.Clear();

            foreach (var strategy in strategies)
            {
                QuickStrategies.Add(strategy);
            }
            return Task.CompletedTask;
        }, "加载快速选股策略");
    }

    /// <summary>
    /// 加载选股模式
    /// </summary>
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

            SelectionModes.Clear();
            foreach (var mode in modes)
            {
                SelectionModes.Add(mode);
            }

            // 默认选择第一个模式
            SelectedMode = SelectionModes.FirstOrDefault();
            return Task.CompletedTask;
        }, "加载选股模式");
    }

    #region 新的统一方法

    /// <summary>
    /// 统一的执行分析方法
    /// </summary>
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

    /// <summary>
    /// 选择模式
    /// </summary>
    private void SelectMode(SelectionModeItem? mode)
    {
        if (mode == null) return;

        // 更新所有模式的选中状态
        foreach (var item in SelectionModes)
        {
            item.IsSelected = item == mode;
        }

        SelectedMode = mode;
    }

    /// <summary>
    /// 显示快速选股选项
    /// </summary>
    private async Task ShowQuickSelectionAsync()
    {
        // 这里可以显示快速选股的弹窗或导航到快速选股页面
        // 目前先使用第一个快速策略作为示例
        var firstStrategy = QuickStrategies.FirstOrDefault();
        if (firstStrategy != null)
        {
            await ExecuteQuickSelectionAsync(firstStrategy);
        }
    }

    /// <summary>
    /// 清除内容
    /// </summary>
    private void ClearContent()
    {
        InputContent = string.Empty;
        ClearResult();
    }

    /// <summary>
    /// 更新当前模式
    /// </summary>
    private void UpdateCurrentMode()
    {
        // 根据当前模式更新输入内容
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

    #endregion
}