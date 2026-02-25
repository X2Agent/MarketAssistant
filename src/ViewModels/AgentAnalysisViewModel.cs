using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Infrastructure;
using MarketAssistant.Services.Archive;
using MarketAssistant.Services.Cache;
using MarketAssistant.Services.Export;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 代理分析页面视图模型
/// </summary>
public partial class AgentAnalysisViewModel : ViewModelBase, INavigationAware<StockNavigationParameter>, IDisposable
{
    public override string Title => "AI股票分析";

    private readonly MarketAnalysisWorkflow _marketAnalysisWorkflow;
    private readonly IAnalysisCacheService _analysisCacheService;
    private readonly ReportArchiveService _reportArchiveService;

    [ObservableProperty]
    private string _stockCode = "";

    [ObservableProperty]
    private bool _isAnalysisInProgress;

    [ObservableProperty]
    private string _analysisStage = "等待开始分析";

    [ObservableProperty]
    private AnalysisReportViewModel _analysisReportViewModel;

    [ObservableProperty]
    private bool _isChatSidebarVisible;

    public ICommand ToggleChatSidebarCommand { get; private set; }

    private MarketAnalysisReport? _lastReport;
    private IStorageProvider? _storageProvider;

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

    public ICommand SendMessageCommand => ChatSidebarViewModel?.SendMessageCommand ?? new RelayCommand(() => { });

    public AgentAnalysisViewModel(
        MarketAnalysisWorkflow marketAnalysisWorkflow,
        AnalysisReportViewModel analysisReportViewModel,
        IAnalysisCacheService analysisCacheService,
        ReportArchiveService reportArchiveService,
        ChatSidebarViewModel chatSidebarViewModel,
        ILogger<AgentAnalysisViewModel> logger) : base(logger)
    {
        _marketAnalysisWorkflow = marketAnalysisWorkflow;
        _analysisReportViewModel = analysisReportViewModel;
        _analysisCacheService = analysisCacheService;
        _reportArchiveService = reportArchiveService;

        ChatSidebarViewModel = chatSidebarViewModel;
        ChatSidebarViewModel.InitializeEmpty();

        SubscribeToEvents();
        ToggleChatSidebarCommand = new RelayCommand(ToggleChatSidebar);
        ExportReportCommand = new AsyncRelayCommand(ExportReportAsync);
        LoadHistoryReportCommand = new AsyncRelayCommand<ReportSummary>(LoadHistoryReportAsync);
    }

    public ICommand ExportReportCommand { get; }
    public ICommand LoadHistoryReportCommand { get; }

    private void SubscribeToEvents()
    {
        _marketAnalysisWorkflow.ProgressChanged += OnAnalysisProgressChanged;
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
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsAnalysisInProgress = e.IsInProgress;
            AnalysisStage = e.StageDescription;
        });
    }

    /// <summary>
    /// 加载分析数据
    /// </summary>
    public async Task LoadAnalysisDataAsync()
    {
        if (string.IsNullOrEmpty(StockCode))
            return;

        await SafeExecuteAsync(async () =>
        {
            // 重置进度状态
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                AnalysisStage = "准备开始...";
            });

            await RefreshHistoryAsync(StockCode);

            var cachedReport = await _analysisCacheService.GetCachedAnalysisAsync(StockCode);
            if (cachedReport != null)
            {
                Logger?.LogInformation("从缓存加载分析结果: {StockCode}", StockCode);

                _lastReport = cachedReport;
                CanExportReport = true;

                await Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    AnalysisReportViewModel.UpdateWithReport(cachedReport);
                    if (ChatSidebarViewModel != null)
                    {
                        await ChatSidebarViewModel.InitializeWithAnalysisHistory(StockCode, cachedReport.AnalystMessages);
                    }
                });
                return;
            }

            Logger?.LogInformation("开始新的分析: {StockCode}", StockCode);
            await Dispatcher.UIThread.InvokeAsync(() => ChatSidebarViewModel?.InitializeEmpty());

            var report = await _marketAnalysisWorkflow.AnalyzeAsync(StockCode);

            _lastReport = report;
            CanExportReport = true;

            await _reportArchiveService.SaveAsync(report);
            await RefreshHistoryAsync(StockCode);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                AnalysisReportViewModel.UpdateWithReport(report);
                if (ChatSidebarViewModel != null)
                {
                    await ChatSidebarViewModel.InitializeWithAnalysisHistory(StockCode, report.AnalystMessages);
                }
                _ = _analysisCacheService.CacheAnalysisAsync(StockCode, report);
            });

        }, "股票分析");
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

        var suggestedName = $"{_lastReport.StockSymbol}_分析报告_{_lastReport.CreatedAt.ToLocalTime():yyyyMMdd}";
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

    public void OnNavigatedTo(StockNavigationParameter parameter)
    {
        if (!string.IsNullOrEmpty(parameter.StockCode))
        {
            StockCode = parameter.StockCode;
            Logger?.LogInformation("导航到 AI 股票分析页面，股票代码: {Code}", StockCode);
            // 异步加载数据
            _ = LoadAnalysisDataAsync();
        }
        else
        {
            Logger?.LogInformation("导航到 AI 股票分析页面，但未提供股票代码");
        }
    }

    public void OnNavigatedFrom()
    {
    }

    private async Task RefreshHistoryAsync(string assetCode)
    {
        var summaries = await _reportArchiveService.GetSummariesAsync(assetCode);
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
            var report = await _reportArchiveService.LoadAsync(summary.Id);
            if (report == null) return;

            _lastReport = report;
            CanExportReport = true;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                AnalysisReportViewModel.UpdateWithReport(report);
                if (ChatSidebarViewModel != null)
                    await ChatSidebarViewModel.InitializeWithAnalysisHistory(report.StockSymbol, report.AnalystMessages);
            });
        }, "加载历史报告");
    }

    public void Dispose()
    {
        _marketAnalysisWorkflow.ProgressChanged -= OnAnalysisProgressChanged;

        if (_chatSidebarViewModel != null)
            _chatSidebarViewModel.PropertyChanged -= OnChatSidebarPropertyChanged;

        GC.SuppressFinalize(this);
    }
}

