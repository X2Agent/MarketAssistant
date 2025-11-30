using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Models;
using MarketAssistant.Services.Cache;
using MarketAssistant.Services.Navigation;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 代理分析页面视图模型
/// </summary>
public partial class AgentAnalysisViewModel : ViewModelBase, INavigationAware<StockNavigationParameter>
{
    public override string Title => "AI股票分析";

    private readonly MarketAnalysisWorkflow _marketAnalysisWorkflow;
    private readonly IAnalysisCacheService _analysisCacheService;

    [ObservableProperty]
    private string _stockCode = "";

    [ObservableProperty]
    private string _currentAnalyst = "准备中";

    [ObservableProperty]
    private bool _isAnalysisInProgress;

    [ObservableProperty]
    private string _analysisStage = "等待开始分析";

    [ObservableProperty]
    private AnalysisReportViewModel _analysisReportViewModel;

    [ObservableProperty]
    private bool _isChatSidebarVisible;

    public ICommand ToggleChatSidebarCommand { get; private set; }

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
        ChatSidebarViewModel chatSidebarViewModel,
        ILogger<AgentAnalysisViewModel> logger) : base(logger)
    {
        _marketAnalysisWorkflow = marketAnalysisWorkflow;
        _analysisReportViewModel = analysisReportViewModel;
        _analysisCacheService = analysisCacheService;

        // 通过构造函数注入 ChatSidebarViewModel
        ChatSidebarViewModel = chatSidebarViewModel;
        ChatSidebarViewModel.InitializeEmpty();

        SubscribeToEvents();
        ToggleChatSidebarCommand = new RelayCommand(ToggleChatSidebar);
    }

    private void SubscribeToEvents()
    {
        _marketAnalysisWorkflow.ProgressChanged += OnAnalysisProgressChanged;
        _marketAnalysisWorkflow.AnalystResultReceived += OnAnalysisCompleted;
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
            CurrentAnalyst = e.CurrentAnalyst;
            IsAnalysisInProgress = e.IsInProgress;
            AnalysisStage = e.StageDescription;
        });
    }

    private void OnAnalysisCompleted(object? sender, ChatMessage e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 实时将消息转发给侧边栏
            ChatSidebarViewModel?.AddAnalysisMessage(e);
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
            // 1. 尝试从缓存加载
            var cachedReport = await _analysisCacheService.GetCachedAnalysisAsync(StockCode);
            if (cachedReport != null)
            {
                Logger?.LogInformation("从缓存加载分析结果: {StockCode}", StockCode);

                // 更新 UI（结构化报告 + 侧边栏历史）
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

            // 2. 缓存未命中，执行新分析
            Logger?.LogInformation("开始新的分析: {StockCode}", StockCode);

            // 清空侧边栏（准备接收实时消息）
            await Dispatcher.UIThread.InvokeAsync(() => ChatSidebarViewModel?.InitializeEmpty());

            // 执行工作流（耗时操作，OnAnalysisCompleted 会实时更新侧边栏）
            var report = await _marketAnalysisWorkflow.AnalyzeAsync(StockCode);

            // 3. 分析完成，更新最终报告并缓存
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                AnalysisReportViewModel.UpdateWithReport(report);
                // 缓存操作不需要在 UI 线程等待，可以放飞或在后台等待
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
        // 离开页面时的清理工作
    }
}

