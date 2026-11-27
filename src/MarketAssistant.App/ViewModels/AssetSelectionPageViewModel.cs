using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using MarketAssistant.Applications.InvestmentSelection;
using MarketAssistant.Applications.InvestmentSelection.Models;
using MarketAssistant.Services.Market;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

public partial class SelectionModeItem : ObservableObject
{
    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _iconPath = string.Empty;

    [ObservableProperty]
    private string _description = string.Empty;

    [ObservableProperty]
    private SelectionModeType _modeType;

    [ObservableProperty]
    private bool _isSelected;
}

public enum SelectionModeType
{
    UserRequirement,
    NewsAnalysis,
    QuickStrategy
}

public partial class AssetSelectionPageViewModel : ViewModelBase, IRecipient<AssetFavoritesChanged>, IDisposable
{
    private readonly InvestmentSelectionService _investmentSelectionService;
    private readonly MarketContext _marketContext;
    private bool _disposed;

    [ObservableProperty]
    private string _userRequirements = string.Empty;

    [ObservableProperty]
    private string _newsContent = string.Empty;

    [ObservableProperty]
    private ObservableCollection<SelectionModeItem> _selectionModes = [];

    [ObservableProperty]
    private SelectionModeItem? _selectedMode;

    [ObservableProperty]
    private ObservableCollection<QuickSelectionStrategyInfo> _quickStrategies = [];

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
    /// 输入态：既没有在运行，也没有结果。三个阶段（输入 / 运行 / 结果）互斥显示。
    /// </summary>
    public bool IsComposing => !IsBusy && SelectionResult == null;

    /// <summary>
    /// 输入区域是否可见（非快速策略模式时显示）
    /// </summary>
    public bool IsInputAreaVisible => SelectedMode?.ModeType != SelectionModeType.QuickStrategy;

    /// <summary>
    /// 快速策略区域是否可见（快速策略模式时显示）
    /// </summary>
    public bool IsQuickStrategyAreaVisible => SelectedMode?.ModeType == SelectionModeType.QuickStrategy;

    /// <summary>
    /// 当前占位符文本，根据市场类型动态调整
    /// </summary>
    public string CurrentPlaceholder => SelectedMode?.ModeType switch
    {
        SelectionModeType.UserRequirement => _marketContext.CurrentMarket == MarketType.Crypto
            ? "请描述您的选币需求，例如：寻找市值前50、24h涨幅超过5%、交易量活跃的虚拟币"
            : "请描述您的选股需求，例如：寻找市值在100-500亿之间，PE低于20倍，近期涨幅不超过10%的价值股",
        SelectionModeType.NewsAnalysis => _marketContext.CurrentMarket == MarketType.Crypto
            ? "请输入新闻内容或热点信息，例如：交易所上线新币、链上活跃地址创新高、机构增持比特币等"
            : "请输入新闻内容或热点信息，例如：央行降准利好银行股，新能源汽车销量创新高等",
        _ => "请输入内容"
    };

    /// <summary>
    /// 当前按钮文本（随模式与市场类型切换，如"开始选股"/"开始选币"）
    /// </summary>
    public string CurrentButtonText
    {
        get
        {
            var noun = SelectionNoun;
            return SelectedMode?.ModeType switch
            {
                SelectionModeType.UserRequirement => $"开始{noun}",
                SelectionModeType.NewsAnalysis => $"基于新闻{noun}",
                _ => "开始分析"
            };
        }
    }

    /// <summary>
    /// 页面标题，根据当前市场类型动态调整
    /// </summary>
    public string PageTitle => _marketContext.CurrentMarket == MarketType.Crypto
        ? "AI智能选币"
        : "AI智能选股";

    /// <summary>
    /// 模式标签标题
    /// </summary>
    public string ModeLabel => _marketContext.CurrentMarket == MarketType.Crypto ? "选币模式" : "选股模式";

    /// <summary>
    /// 快速策略标题
    /// </summary>
    public string QuickStrategyTitle => _marketContext.CurrentMarket == MarketType.Crypto ? "快速选币策略" : "快速选股策略";

    /// <summary>
    /// 快速策略描述
    /// </summary>
    public string QuickStrategyDescription => _marketContext.CurrentMarket == MarketType.Crypto
        ? "选择预设的选币策略，快速获得投资建议"
        : "选择预设的选股策略，快速获得投资建议";

    /// <summary>
    /// 标的类别名词（选股 / 选币）：拼进页面内所有"选股"类文案，避免虚拟币市场出现股票措辞。
    /// </summary>
    private string SelectionNoun => _marketContext.CurrentMarket == MarketType.Crypto ? "选币" : "选股";

    public AssetSelectionPageViewModel(
        ILogger<AssetSelectionPageViewModel> logger,
        InvestmentSelectionService investmentSelectionService,
        MarketContext marketContext) : base(logger)
    {
        _investmentSelectionService = investmentSelectionService;
        _marketContext = marketContext;
        SubscribeToMarketChanges(_marketContext);
        WeakReferenceMessenger.Default.Register(this);

        _elapsedTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, OnElapsedTimerTick);
        _elapsedTimer.Stop();

        EmptyMessage = $"暂无可用{SelectionNoun}模式，请稍后重试";
        SetRetryCommand(() => LoadPageAsync());
        _ = LoadPageAsync();
    }

    /// <summary>首屏加载：模式卡与快速策略列表。</summary>
    private async Task LoadPageAsync()
    {
        await LoadAsync(async _ =>
        {
            await LoadSelectionModesAsync();
            await LoadQuickStrategiesAsync();
            return SelectionModes.Count > 0;
        }, $"加载{SelectionNoun}页面");
    }

    protected override void OnBusyChanged() => OnPropertyChanged(nameof(IsComposing));

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

    protected override void OnMarketChanged(MarketType newMarket)
    {
        // 事件来自单例 MarketContext，Dispose 后不得再触发（更新状态/启动加载）
        if (_disposed)
            return;

        // 旧市场的在途任务与结果在新市场下不再成立（收藏归属、跳转目标都依赖市场），继续运行只会白耗 Token
        _runCts?.Cancel();
        SelectionResult = null;
        FailureMessage = string.Empty;

        OnPropertyChanged(nameof(PageTitle));
        OnPropertyChanged(nameof(ModeLabel));
        OnPropertyChanged(nameof(QuickStrategyTitle));
        OnPropertyChanged(nameof(QuickStrategyDescription));
        OnPropertyChanged(nameof(ResultTitle));
        OnPropertyChanged(nameof(RecommendationTitle));
        OnPropertyChanged(nameof(RunningTitle));
        OnPropertyChanged(nameof(NewSelectionText));
        OnPropertyChanged(nameof(CurrentPlaceholder));
        OnPropertyChanged(nameof(CurrentButtonText));
        EmptyMessage = $"暂无可用{SelectionNoun}模式，请稍后重试";

        // 模式卡描述原地更新：不重建集合，避免把用户已选的模式重置回第一个
        ApplyModeDescriptions();
        _ = LoadQuickStrategiesAsync();
    }

    [RelayCommand]
    private void SelectMode(SelectionModeItem? mode)
    {
        if (mode == null) return;

        foreach (var item in SelectionModes)
        {
            item.IsSelected = item == mode;
        }

        FailureMessage = string.Empty;
        SelectedMode = mode;
    }

    [RelayCommand]
    private void ClearContent()
    {
        CurrentInputContent = string.Empty;
        ClearValidationMessage();
        FailureMessage = string.Empty;
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
        var strategies = _investmentSelectionService.GetQuickSelectionStrategies(_marketContext.CurrentMarket);

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            QuickStrategies.Clear();
            foreach (var strategy in strategies)
            {
                QuickStrategies.Add(strategy);
            }
        });
    }

    private async Task LoadSelectionModesAsync()
    {
        var modes = new ObservableCollection<SelectionModeItem>
        {
            new SelectionModeItem { Name = "用户需求", IconPath = "avares://MarketAssistant/Assets/Images/icon_users.svg", Description = BuildModeDescription(SelectionModeType.UserRequirement), ModeType = SelectionModeType.UserRequirement, IsSelected = true },
            new SelectionModeItem { Name = "新闻分析", IconPath = "avares://MarketAssistant/Assets/Images/icon_document.svg", Description = BuildModeDescription(SelectionModeType.NewsAnalysis), ModeType = SelectionModeType.NewsAnalysis },
            new SelectionModeItem { Name = "快速策略", IconPath = "avares://MarketAssistant/Assets/Images/icon_bolt.svg", Description = BuildModeDescription(SelectionModeType.QuickStrategy), ModeType = SelectionModeType.QuickStrategy }
        };

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            SelectionModes.Clear();
            foreach (var mode in modes)
            {
                SelectionModes.Add(mode);
            }

            if (SelectedMode is null)
                SelectedMode = SelectionModes.FirstOrDefault();
        });
    }

    /// <summary>切换市场时就地刷新模式卡描述（文案含"选股/选币"，不重建集合以免重置已选模式）。</summary>
    private void ApplyModeDescriptions()
    {
        foreach (var mode in SelectionModes)
        {
            mode.Description = BuildModeDescription(mode.ModeType);
        }
    }

    private string BuildModeDescription(SelectionModeType modeType) => modeType switch
    {
        SelectionModeType.UserRequirement => $"根据用户输入的{SelectionNoun}需求进行{SelectionNoun}",
        SelectionModeType.NewsAnalysis => $"根据新闻内容进行{SelectionNoun}",
        _ => $"使用预设的快速{SelectionNoun}策略"
    };

    public void Dispose()
    {
        _disposed = true;
        _runCts?.Cancel();
        _elapsedTimer.Stop();
        _elapsedTimer.Tick -= OnElapsedTimerTick;
        UnsubscribeFromMarketChanges(_marketContext);
        WeakReferenceMessenger.Default.UnregisterAll(this);
        GC.SuppressFinalize(this);
    }
}
