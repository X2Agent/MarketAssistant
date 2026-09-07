using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Applications.Analysis;
using MarketAssistant.Services.Archive;
using MarketAssistant.Services.Export;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 代理分析页面视图模型
/// </summary>
public partial class AgentAnalysisViewModel : ViewModelBase, INavigationAware<AssetNavigationParameter>, IDisposable
{
    public override string Title => MarketContext?.CurrentMarket switch
    {
        MarketType.Crypto => "AI虚拟币分析",
        _ => "AI股票分析"
    };

    private readonly AnalysisOrchestrationService _orchestrationService;

    [ObservableProperty]
    private string _stockCode = "";

    [ObservableProperty]
    private bool _isAnalysisInProgress;

    [ObservableProperty]
    private string _analysisStage = "等待开始分析";

    [ObservableProperty]
    private int _analysisProgressPercent;

    [ObservableProperty]
    private string _failedAnalystsInfo = string.Empty;

    /// <summary>
    /// 当前流程阶段（步骤条高亮）
    /// </summary>
    [ObservableProperty]
    private AnalysisPhase _analysisPhase = AnalysisPhase.Preparing;

    /// <summary>
    /// 各分析师的运行状态行（逐位展示，运行中带实时耗时）
    /// </summary>
    public ObservableCollection<AnalystRunItemViewModel> AnalystRunItems { get; } = [];

    /// <summary>
    /// 平滑百分比插值计时器：目标百分比由完成事件驱动（阶梯式），
    /// 计时器按固定步长逼近目标值，避免 0→50→100 的跳变观感
    /// </summary>
    private readonly DispatcherTimer _progressSmoothTimer;
    private double _smoothedPercent;
    private double _targetPercent;
    private const double SmoothStepSize = 2.0;
    private static readonly TimeSpan SmoothTickInterval = TimeSpan.FromMilliseconds(30);

    /// <summary>
    /// 当前会话是否已有可展示的分析报告（成功产出或加载历史报告后为 true）
    /// </summary>
    [ObservableProperty]
    private bool _hasActiveReport;

    /// <summary>
    /// 分析未完成（失败或取消）时展示给用户的提示信息
    /// </summary>
    [ObservableProperty]
    private string _analysisFailureMessage = string.Empty;

    [ObservableProperty]
    private AnalysisReportViewModel _analysisReportViewModel;

    [ObservableProperty]
    private bool _isChatSidebarVisible;

    public ICommand ToggleChatSidebarCommand { get; private set; }
    public ICommand CancelAnalysisCommand { get; private set; }
    public ICommand RetryAnalysisCommand { get; private set; }

    private MarketAnalysisReport? _lastReport;
    private IStorageProvider? _storageProvider;
    private CancellationTokenSource? _analysisCts;
    private Guid? _activeAnalysisRunId;

    /// <summary>
    /// 供 View 在 AttachedToVisualTree 时注入
    /// </summary>
    public void SetStorageProvider(IStorageProvider? storageProvider) => _storageProvider = storageProvider;

    /// <summary>
    /// 报告是否可导出
    /// </summary>
    [ObservableProperty]
    private bool _canExportReport;

    public ObservableCollection<ReportSummary> ReportHistory { get; } = [];

    [ObservableProperty]
    private bool _hasReportHistory;

    private ChatSidebarViewModel? _chatSidebarViewModel;
    /// <summary>
    /// 聊天侧边栏 ViewModel 引用（用于数据同步）
    /// </summary>
    public ChatSidebarViewModel? ChatSidebarViewModel
    {
        get => _chatSidebarViewModel;
        set
        {
            if (_chatSidebarViewModel != null)
            {
                _chatSidebarViewModel.PropertyChanged -= OnChatSidebarPropertyChanged;
            }

            SetProperty(ref _chatSidebarViewModel, value);

            if (_chatSidebarViewModel != null)
            {
                _chatSidebarViewModel.PropertyChanged += OnChatSidebarPropertyChanged;
            }

            OnPropertyChanged(nameof(ChatMessages));
            OnPropertyChanged(nameof(UserInput));
            OnPropertyChanged(nameof(SendMessageCommand));
        }
    }

    private readonly ObservableCollection<ChatMessageAdapter> _emptyChatMessages = new();
    public ObservableCollection<ChatMessageAdapter> ChatMessages => ChatSidebarViewModel?.ChatMessages ?? _emptyChatMessages;

    public string UserInput
    {
        get => ChatSidebarViewModel?.UserInput ?? string.Empty;
        set
        {
            if (ChatSidebarViewModel != null)
            {
                ChatSidebarViewModel.UserInput = value;
                OnPropertyChanged();
            }
        }
    }

    private static readonly ICommand _noopCommand = new RelayCommand(() => { });
    public ICommand SendMessageCommand => ChatSidebarViewModel?.SendMessageCommand ?? _noopCommand;

    public AgentAnalysisViewModel(
        AnalysisOrchestrationService orchestrationService,
        AnalysisReportViewModel analysisReportViewModel,
        ChatSidebarViewModel chatSidebarViewModel,
        MarketContext marketContext,
        ILogger<AgentAnalysisViewModel> logger) : base(logger)
    {
        _orchestrationService = orchestrationService;
        _analysisReportViewModel = analysisReportViewModel;

        _progressSmoothTimer = new DispatcherTimer(SmoothTickInterval, DispatcherPriority.Background, OnProgressSmoothTimerTick);
        _progressSmoothTimer.Stop();

        SubscribeToMarketChanges(marketContext);

        ChatSidebarViewModel = chatSidebarViewModel;
        ChatSidebarViewModel.InitializeEmpty();

        SubscribeToEvents();
        ToggleChatSidebarCommand = new RelayCommand(ToggleChatSidebar);
        CancelAnalysisCommand = new RelayCommand(CancelAnalysis);
        RetryAnalysisCommand = new AsyncRelayCommand(LoadAnalysisDataAsync);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync);
        LoadHistoryReportCommand = new AsyncRelayCommand<ReportSummary>(LoadHistoryReportAsync);
    }

    public ICommand ExportReportCommand { get; }
    public ICommand LoadHistoryReportCommand { get; }

    private void SubscribeToEvents()
    {
        _orchestrationService.ProgressChanged += OnAnalysisProgressChanged;
    }

    /// <summary>
    /// 处理 ChatSidebarViewModel 的属性变更
    /// </summary>
    private void OnChatSidebarPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ChatSidebarViewModel.UserInput):
                OnPropertyChanged(nameof(UserInput));
                break;
            case nameof(ChatSidebarViewModel.ChatMessages):
                OnPropertyChanged(nameof(ChatMessages));
                break;
        }
    }

    private void OnAnalysisProgressChanged(object? sender, AnalysisProgressEventArgs e)
    {
        if (_activeAnalysisRunId != e.RunId ||
            !string.Equals(StockCode, e.AssetSymbol, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsAnalysisInProgress = e.IsInProgress;
            AnalysisStage = e.StageDescription;

            if (e.FailedAnalysts.Count > 0)
            {
                FailedAnalystsInfo = $"部分分析师失败: {string.Join(", ", e.FailedAnalysts)}";
            }

            // 终态事件（完成/失败/取消）均携带分析师快照，行条目由快照直接定格
            if (e.Analysts.Count > 0)
            {
                AnalysisPhase = e.Phase;
                SyncAnalystRunItems(e.Analysts);
            }

            UpdateTargetPercent(e);

            if (!_progressSmoothTimer.IsEnabled)
            {
                _progressSmoothTimer.Start();
            }
        });
    }

    /// <summary>
    /// 将最新快照同步到逐分析师状态行：按显示名增量更新，保持行顺序稳定，避免整体重建闪烁
    /// </summary>
    private void SyncAnalystRunItems(IReadOnlyList<AnalystRunSnapshot> snapshots)
    {
        var existing = AnalystRunItems.ToDictionary(i => i.DisplayName, i => i);
        foreach (var snapshot in snapshots)
        {
            if (existing.TryGetValue(snapshot.DisplayName, out var item))
            {
                item.UpdateFrom(snapshot);
            }
            else
            {
                AnalystRunItems.Add(AnalystRunItemViewModel.From(snapshot));
            }
        }
    }

    /// <summary>
    /// 根据进度事件计算目标百分比：
    /// 分析阶段对运行中的分析师按耗时折算部分完成度，使百分比持续平滑推进而非停在整档
    /// </summary>
    private void UpdateTargetPercent(AnalysisProgressEventArgs e)
    {
        if (!e.IsInProgress)
        {
            _targetPercent = 100;
            return;
        }

        _targetPercent = e.Phase switch
        {
            AnalysisPhase.Preparing => 3,
            AnalysisPhase.Analyzing when e.Analysts.Count > 0 => 5 + Math.Clamp(
                e.Analysts.Sum(a => a.Status switch
                {
                    AnalystRunStatus.Completed or AnalystRunStatus.Failed => 1.0,
                    AnalystRunStatus.Running => EstimateRunningCredit(a.Elapsed.TotalSeconds),
                    _ => 0.0
                }) / e.Analysts.Count * 80.0, 0, 80),
            AnalysisPhase.Aggregating => 89,
            AnalysisPhase.Reporting => 95,
            AnalysisPhase.Completed => 100,
            _ => _targetPercent
        };
    }

    /// <summary>
    /// 运行中分析师的完成度估算：前 60 秒线性增长至 70%，之后渐近逼近 90%
    /// </summary>
    private static double EstimateRunningCredit(double elapsedSeconds)
        => elapsedSeconds <= 60
            ? elapsedSeconds / 60 * 0.7
            : 0.7 + (1 - Math.Exp(-(elapsedSeconds - 60) / 120)) * 0.2;

    /// <summary>
    /// 平滑百分比插值：按固定步长逼近目标值，同时刷新运行中分析师的实时耗时
    /// </summary>
    private void OnProgressSmoothTimerTick(object? sender, EventArgs e)
    {
        var hasRunning = false;
        foreach (var item in AnalystRunItems)
        {
            if (item.Status == AnalystRunStatus.Running)
            {
                item.RefreshElapsed();
                hasRunning = true;
            }
        }

        if (hasRunning && AnalysisProgressPercent < 100)
        {
            RecomputeTargetPercentFromItems();
        }

        var delta = _targetPercent - _smoothedPercent;
        if (Math.Abs(delta) > 0.01)
        {
            _smoothedPercent += Math.Clamp(delta, -SmoothStepSize, SmoothStepSize);
        }

        var rounded = (int)Math.Round(Math.Clamp(_smoothedPercent, 0, 100));
        if (rounded != AnalysisProgressPercent)
        {
            AnalysisProgressPercent = rounded;
        }

        if (!IsAnalysisInProgress && !hasRunning && Math.Abs(_targetPercent - _smoothedPercent) <= 0.01)
        {
            _progressSmoothTimer.Stop();
        }
    }
    /// <summary>
    /// 分析执行中：按运行中分析师的实时耗时持续重算目标百分比，
    /// 使两位分析师进度事件之间的空闲期百分比仍平滑推进，而非长时间停滞。
    /// 仅在存在运行中条目时生效，避免覆盖阶段事件给出的目标值（聚合 89% / 报告 95%）
    /// </summary>
    private void RecomputeTargetPercentFromItems()
    {
        var hasRunning = false;
        var credit = 0.0;
        foreach (var item in AnalystRunItems)
        {
            if (item.Status == AnalystRunStatus.Pending)
                continue;

            if (item.Status == AnalystRunStatus.Running)
            {
                hasRunning = true;
                credit += EstimateRunningCredit(item.CurrentElapsed.TotalSeconds);
            }
            else
            {
                credit += 1.0;
            }
        }

        if (hasRunning)
        {
            _targetPercent = 5 + Math.Clamp(credit / AnalystRunItems.Count * 80.0, 0, 80);
        }
    }
    /// <summary>
    /// 重置进度展示状态（新一次分析开始或市场切换时调用）
    /// </summary>
    private void ResetProgressDisplay()
    {
        _smoothedPercent = 0;
        _targetPercent = 0;
        _progressSmoothTimer.Stop();
        AnalysisProgressPercent = 0;
        AnalysisPhase = AnalysisPhase.Preparing;
        FailedAnalystsInfo = string.Empty;
        AnalystRunItems.Clear();
    }

    private void CancelAnalysis()
    {
        _analysisCts?.Cancel();
        Logger?.LogInformation("用户取消了分析任务");
    }

    /// <summary>
    /// 加载分析数据。
    /// 成功产出报告后才切换到报告视图；失败或取消时停留在"分析未完成"状态，
    /// 不再弹出全局错误框并落入空白的报告页。
    /// </summary>
    public async Task LoadAnalysisDataAsync()
    {
        if (string.IsNullOrEmpty(StockCode))
            return;

        IsBusy = true;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ResetProgressDisplay();
                AnalysisStage = "准备开始...";
                HasActiveReport = false;
                AnalysisFailureMessage = string.Empty;
            });

            await RefreshHistoryAsync(StockCode);

            // 只取消上一个在飞分析，不 Dispose（其令牌仍被持有）
            _analysisCts?.Cancel();
            _analysisCts = new CancellationTokenSource();
            var runId = Guid.NewGuid();
            _activeAnalysisRunId = runId;
            var assetCode = StockCode;

            var result = await _orchestrationService.AnalyzeAsync(
                assetCode,
                runId,
                _analysisCts.Token);
            var report = result.Report;

            _lastReport = report;
            CanExportReport = true;
            HasActiveReport = true;

            if (!result.FromCache)
                await RefreshHistoryAsync(StockCode);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                AnalysisReportViewModel.UpdateWithReport(report);
                if (ChatSidebarViewModel != null)
                {
                    await ChatSidebarViewModel.InitializeWithAnalysisHistory(StockCode, report.AnalystMessages);
                }
            });
        }
        catch (OperationCanceledException)
        {
            Logger?.LogInformation("资产 {StockCode} 的分析已取消", StockCode);
            AnalysisFailureMessage = "分析已取消，可点击下方按钮重新发起分析";
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "执行 '资产分析' 时发生错误");
            AnalysisFailureMessage = ErrorMessageMapper.GetUserFriendlyMessageWithContext(ex, "资产分析");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 切换聊天侧边栏显示状态
    /// </summary>
    private void ToggleChatSidebar()
    {
        IsChatSidebarVisible = !IsChatSidebarVisible;
    }

    /// <summary>
    /// 导出分析报告为 Markdown 文件
    /// </summary>
    private async Task ExportReportAsync()
    {
        if (_lastReport == null || _storageProvider == null)
            return;

        var suggestedName = $"{_lastReport.AssetSymbol}_分析报告_{_lastReport.CreatedAt.ToLocalTime():yyyyMMdd}";
        var file = await _storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "导出分析报告",
            SuggestedFileName = suggestedName,
            FileTypeChoices =
            [
                new FilePickerFileType("Markdown") { Patterns = ["*.md"] },
            ],
            DefaultExtension = "md"
        });

        if (file == null) return;

        var markdown = MarkdownReportExporter.Export(_lastReport);
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8);
        await writer.WriteAsync(markdown);

        Logger?.LogInformation("分析报告已导出: {Path}", file.Name);
    }

    public void OnNavigatedTo(AssetNavigationParameter parameter, bool isReactivation)
    {
        if (!string.IsNullOrEmpty(parameter.Code))
        {
            StockCode = parameter.Code;
            Logger?.LogInformation("导航到 AI 分析页面，资产代码: {Code}，重新激活: {IsReactivation}", StockCode, isReactivation);
            // GoBack 重新激活时不重复启动分析，避免重复请求
            if (!isReactivation)
            {
                _ = LoadAnalysisDataAsync();
            }
        }
        else
        {
            Logger?.LogInformation("导航到 AI 分析页面，但未提供资产代码");
        }
    }

    public void OnNavigatedFrom()
    {
        _analysisCts?.Cancel();
        _activeAnalysisRunId = null;
        IsChatSidebarVisible = false;
    }

    private async Task RefreshHistoryAsync(string assetCode)
    {
        var summaries = await _orchestrationService.GetReportHistoryAsync(assetCode);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ReportHistory.Clear();
            foreach (var s in summaries)
                ReportHistory.Add(s);
            HasReportHistory = ReportHistory.Count > 0;
        });
    }

    private async Task LoadHistoryReportAsync(ReportSummary? summary)
    {
        if (summary == null) return;

        await SafeExecuteAsync(async () =>
        {
            var report = await _orchestrationService.LoadHistoryReportAsync(summary.Id);
            if (report == null) return;

            _lastReport = report;
            CanExportReport = true;
            HasActiveReport = true;
            AnalysisFailureMessage = string.Empty;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                AnalysisReportViewModel.UpdateWithReport(report);
                if (ChatSidebarViewModel != null)
                    await ChatSidebarViewModel.InitializeWithAnalysisHistory(report.AssetSymbol, report.AnalystMessages);
            });
        }, "加载历史报告");
    }

    private bool _disposed;

    protected override void OnMarketChanged(MarketType newMarket)
    {
        // 事件来自单例 MarketContext，Dispose 后不得再触发（重置 UI 状态/启动加载）
        if (_disposed)
            return;

        // 取消进行中的分析任务，避免旧市场的分析结果污染新市场；
        // 只取消不 Dispose：在飞分析仍持有旧令牌
        _analysisCts?.Cancel();
        _analysisCts = null;
        _activeAnalysisRunId = null;
        _lastReport = null;

        // 重置分析状态
        IsAnalysisInProgress = false;
        AnalysisStage = "等待开始分析";
        ResetProgressDisplay();
        CanExportReport = false;
        HasActiveReport = false;
        AnalysisFailureMessage = string.Empty;

        OnPropertyChanged(nameof(Title));
    }

    public void Dispose()
    {
        _disposed = true;
        _progressSmoothTimer.Stop();
        _progressSmoothTimer.Tick -= OnProgressSmoothTimerTick;
        _orchestrationService.ProgressChanged -= OnAnalysisProgressChanged;
        _analysisCts?.Cancel();
        _analysisCts?.Dispose();
        _activeAnalysisRunId = null;

        if (MarketContext != null)
            UnsubscribeFromMarketChanges(MarketContext);

        if (_chatSidebarViewModel != null)
        {
            _chatSidebarViewModel.PropertyChanged -= OnChatSidebarPropertyChanged;
            _chatSidebarViewModel.Dispose();
        }

        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 进度面板中的单个分析师状态行视图模型。
/// </summary>
public partial class AnalystRunItemViewModel : ObservableObject
{
    private long _runningStartedTimestamp;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private AnalystRunStatus _status = AnalystRunStatus.Pending;

    [ObservableProperty]
    private string _elapsedText = "--:--";

    /// <summary>状态徽标文本（完成 ✓ / 失败 ✗ / 其他为空）</summary>
    [ObservableProperty]
    private string _statusMark = string.Empty;

    public bool IsCompleted => Status == AnalystRunStatus.Completed;

    public bool IsFailed => Status == AnalystRunStatus.Failed;

    public bool IsRunning => Status == AnalystRunStatus.Running;

    /// <summary>等待执行的灰色静默点</summary>
    public bool IsPending => Status == AnalystRunStatus.Pending;

    /// <summary>终态（完成/失败）则不再需要呼吸动画</summary>
    public bool IsLive => IsRunning;

    public static AnalystRunItemViewModel From(AnalystRunSnapshot snapshot)
    {
        var item = new AnalystRunItemViewModel { DisplayName = snapshot.DisplayName };
        item.ApplySnapshot(snapshot);
        return item;
    }

    /// <summary>
    /// 用最新快照增量更新本行；从运行态转为终态时定格耗时
    /// </summary>
    public void UpdateFrom(AnalystRunSnapshot snapshot)
    {
        ApplySnapshot(snapshot);
    }

    /// <summary>
    /// 计时器驱动：刷新运行中条目的实时耗时显示
    /// </summary>
    public void RefreshElapsed()
    {
        if (Status != AnalystRunStatus.Running)
            return;

        ElapsedText = FormatElapsed(Stopwatch.GetElapsedTime(_runningStartedTimestamp));
    }

    /// <summary>当前实时耗时（运行中取本地 Stopwatch；仅供进度预估消费运行中条目）</summary>
    public TimeSpan CurrentElapsed => Status == AnalystRunStatus.Running
        ? Stopwatch.GetElapsedTime(_runningStartedTimestamp)
        : TimeSpan.Zero;

    private void ApplySnapshot(AnalystRunSnapshot snapshot)
    {
        Status = snapshot.Status;
        if (snapshot.Status == AnalystRunStatus.Running && _runningStartedTimestamp == 0)
        {
            // 运行起点只记录一次：无论经 UpdateFrom 还是 From 首见 Running 快照都确保有起点，
            // 避免 Stopwatch.GetElapsedTime(0) 把进程运行时长误算为耗时
            _runningStartedTimestamp = Stopwatch.GetTimestamp();
        }
        StatusMark = snapshot.Status switch
        {
            AnalystRunStatus.Completed => "✓",
            AnalystRunStatus.Failed => "✗",
            _ => string.Empty
        };
        if (snapshot.Status == AnalystRunStatus.Running)
        {
            ElapsedText = FormatElapsed(Stopwatch.GetElapsedTime(_runningStartedTimestamp));
        }
        else
        {
            // 终态耗时以工作流快照为准；从未运行过的条目（如取消时仍 Pending）保持 --:--
            ElapsedText = snapshot.Elapsed > TimeSpan.Zero
                ? FormatElapsed(snapshot.Elapsed)
                : "--:--";
        }

        OnPropertyChanged(nameof(IsCompleted));
        OnPropertyChanged(nameof(IsFailed));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsPending));
        OnPropertyChanged(nameof(IsLive));
    }

    /// <summary>
    /// 耗时格式化为 mm:ss（金融终端风格等宽字体展示）
    /// </summary>
    private static string FormatElapsed(TimeSpan elapsed)
    {
        var totalSeconds = (int)elapsed.TotalSeconds;
        return $"{totalSeconds / 60:D2}:{totalSeconds % 60:D2}";
    }
}

