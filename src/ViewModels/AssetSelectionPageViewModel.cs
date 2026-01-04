using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.Favorites;
using MarketAssistant.Applications.InvestmentSelection;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Dialog;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

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
public partial class AssetSelectionPageViewModel : ViewModelBase
{
    private readonly InvestmentSelectionService _investmentSelectionService;
    private readonly IServiceProvider _serviceProvider;
    private readonly MarketContext _marketContext;
    private readonly IDialogService _dialogService;

    [ObservableProperty]
    private string _userRequirements = string.Empty;

    [ObservableProperty]
    private string _newsContent = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SelectionModeItem> _selectionModes = new();

    [ObservableProperty]
    private SelectionModeItem? _selectedMode;

    [ObservableProperty]
    private InvestmentSelectionResult? _selectionResult;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private ObservableCollection<QuickSelectionStrategyInfo> _quickStrategies = new();

    [ObservableProperty]
    private string _validationMessage = string.Empty;

    [ObservableProperty]
    private bool _hasValidationMessage;

    /// <summary>
    /// 根据当前选择的模式，动态返回对应的输入内容
    /// </summary>
    public string CurrentInputContent
    {
        get => SelectedMode?.ModeType switch
        {
            SelectionModeType.UserRequirement => UserRequirements,
            SelectionModeType.NewsAnalysis => NewsContent,
            _ => string.Empty
        };
        set
        {
            if (SelectedMode != null)
            {
                switch (SelectedMode.ModeType)
                {
                    case SelectionModeType.UserRequirement:
                        UserRequirements = value;
                        break;
                    case SelectionModeType.NewsAnalysis:
                        NewsContent = value;
                        break;
                }
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                ClearValidationMessage();
            }

            OnPropertyChanged();
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

    /// <summary>
    /// 推荐投资标的列表（用于UI绑定）
    /// </summary>
    public ObservableCollection<InvestmentRecommendation> RecommendedStocks =>
        SelectionResult?.Recommendations != null
            ? new ObservableCollection<InvestmentRecommendation>(SelectionResult.Recommendations)
            : new ObservableCollection<InvestmentRecommendation>();

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
    public AssetSelectionPageViewModel(
        ILogger<AssetSelectionPageViewModel> logger,
        InvestmentSelectionService investmentSelectionService,
        IServiceProvider serviceProvider,
        MarketContext marketContext,
        IDialogService dialogService) : base(logger)
    {
        _investmentSelectionService = investmentSelectionService;
        _serviceProvider = serviceProvider;
        _marketContext = marketContext;
        _dialogService = dialogService;
        _ = LoadQuickStrategiesAsync();
        _ = LoadSelectionModesAsync();
    }

    partial void OnSelectedModeChanged(SelectionModeItem? value)
    {
        if (value != null)
        {
            OnPropertyChanged(nameof(CurrentInputContent));
            OnPropertyChanged(nameof(CurrentPlaceholder));
            OnPropertyChanged(nameof(CurrentButtonText));
            OnPropertyChanged(nameof(IsInputAreaVisible));
            OnPropertyChanged(nameof(IsQuickStrategyAreaVisible));
        }
    }

    partial void OnSelectionResultChanged(InvestmentSelectionResult? value)
    {
        OnPropertyChanged(nameof(RecommendedStocks));
        OnPropertyChanged(nameof(FormattedRiskWarnings));
        OnPropertyChanged(nameof(HasRiskWarnings));
    }

    [RelayCommand]
    private void ViewStockDetail(InvestmentRecommendation? stock)
    {
        if (stock == null) return;

        WeakReferenceMessenger.Default.Send(
            new NavigationMessage("Asset", new AssetNavigationParameter(stock.Symbol, stock.Name)));
    }

    [RelayCommand]
    private async Task AddToFavorites(InvestmentRecommendation? stock)
    {
        if (stock == null) return;

        // 解析 Symbol，例如 SH600000 -> Market: SH, Code: 600000
        string market = "CN";
        string code = stock.Symbol;

        if (stock.Symbol.Length > 2 && (stock.Symbol.StartsWith("SH") || stock.Symbol.StartsWith("SZ")))
        {
            market = stock.Symbol.Substring(0, 2);
            code = stock.Symbol.Substring(2);
        }

        var favoriteService = _serviceProvider.GetRequiredKeyedService<IFavoriteService>(_marketContext.CurrentMarket);
        if (favoriteService.IsFavorite(code, market))
        {
            await _dialogService.ShowMessageAsync("提示", $"{stock.Name} ({stock.Symbol}) 已在自选列表中");
            return;
        }

        favoriteService.AddFavorite(code, market);
        await _dialogService.ShowMessageAsync("成功", $"已将 {stock.Name} ({stock.Symbol}) 加入自选");
    }

    [RelayCommand]
    private async Task ExecuteAnalysisAsync()
    {
        if (SelectedMode == null)
            return;

        switch (SelectedMode.ModeType)
        {
            case SelectionModeType.UserRequirement:
                await ExecuteSelectionAsync();
                break;
            case SelectionModeType.NewsAnalysis:
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
            var result = await _investmentSelectionService.QuickSelectAsync(strategy.Strategy);
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
        CurrentInputContent = string.Empty;
        ClearResult();
    }

    private async Task ExecuteSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(UserRequirements))
        {
            ShowValidationMessage("请输入您的选股需求，例如：寻找市值在100-500亿之间，PE低于20倍的价值股");
            return;
        }

        ClearValidationMessage();
        await SafeExecuteAsync(async () =>
        {
            var request = new InvestmentRecommendationRequest
            {
                MarketType = _marketContext.CurrentMarket,
                UserRequirements = UserRequirements
            };

            var result = await _investmentSelectionService.RecommendByUserRequirementAsync(request);
            SelectionResult = result;
            HasResult = result != null && (
                (result.Recommendations?.Count ?? 0) > 0 ||
                !string.IsNullOrWhiteSpace(result.AnalysisSummary));
        }, "执行AI选股");
    }

    private async Task ExecuteNewsSelectionAsync()
    {
        if (string.IsNullOrWhiteSpace(NewsContent))
        {
            ShowValidationMessage("请输入新闻内容或热点信息，例如：央行降准利好银行股，新能源汽车销量创新高等");
            return;
        }

        ClearValidationMessage();
        await SafeExecuteAsync(async () =>
        {
            var request = new NewsBasedInvestmentRequest
            {
                MarketType = _marketContext.CurrentMarket,
                NewsContent = NewsContent,
                MaxRecommendations = 5
            };

            var result = await _investmentSelectionService.RecommendByNewsAsync(request);
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
        ClearValidationMessage();
    }

    private void ShowValidationMessage(string message)
    {
        ValidationMessage = message;
        HasValidationMessage = true;
    }

    private void ClearValidationMessage()
    {
        ValidationMessage = string.Empty;
        HasValidationMessage = false;
    }

    private async Task LoadQuickStrategiesAsync()
    {
        await SafeExecuteAsync(() =>
        {
            var strategies = _investmentSelectionService.GetQuickSelectionStrategies();
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
}



