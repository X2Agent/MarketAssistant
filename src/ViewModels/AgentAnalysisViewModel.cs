using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Agents.MarketAnalysis;
using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Services.Cache;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 代理分析页面视图模型
/// </summary>
public partial class AgentAnalysisViewModel : ViewModelBase
{
    private readonly MarketAnalysisWorkflow _marketAnalysisWorkflow;
    private readonly IAnalysisCacheService _analysisCacheService;

    private string _stockCode = "";
    public string StockCode
    {
        get => _stockCode;
        set
        {
            _stockCode = value;
            OnPropertyChanged();
        }
    }

    private string _currentAnalyst = "准备中";
    public string CurrentAnalyst
    {
        get => _currentAnalyst;
        set => SetProperty(ref _currentAnalyst, value);
    }

    private bool _isAnalysisInProgress;
    public bool IsAnalysisInProgress
    {
        get => _isAnalysisInProgress;
        set => SetProperty(ref _isAnalysisInProgress, value);
    }

    private string _analysisStage = "等待开始分析";
    public string AnalysisStage
    {
        get => _analysisStage;
        set => SetProperty(ref _analysisStage, value);
    }

    public ObservableCollection<AnalysisMessage> AnalysisMessages { get; } = new ObservableCollection<AnalysisMessage>();

    private AnalysisReportViewModel _analysisReportViewModel;
    public AnalysisReportViewModel AnalysisReportViewModel
    {
        get => _analysisReportViewModel;
        set => SetProperty(ref _analysisReportViewModel, value);
    }

    private bool _isChatSidebarVisible;
    public bool IsChatSidebarVisible
    {
        get => _isChatSidebarVisible;
        set => SetProperty(ref _isChatSidebarVisible, value);
    }

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
        CurrentAnalyst = e.CurrentAnalyst;
        IsAnalysisInProgress = e.IsInProgress;
        AnalysisStage = e.StageDescription;
    }

    private void OnAnalysisCompleted(object? sender, ChatMessage e)
    {
        var message = new AnalysisMessage
        {
            Sender = e.AuthorName ?? string.Empty,
            Content = e.Text ?? string.Empty,
            Timestamp = DateTime.Now,
        };

        AnalysisMessages.Add(message);
        _ = AnalysisReportViewModel.ProcessAnalysisMessageAsync(message);
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
            var cachedReport = await _analysisCacheService.GetCachedAnalysisAsync(StockCode);
            if (cachedReport != null)
            {
                Logger?.LogInformation("从缓存加载分析结果: {StockCode}", StockCode);
                AnalysisReportViewModel.UpdateWithReport(cachedReport);
                return;
            }

            Logger?.LogInformation("缓存中没有结果，开始新的分析: {StockCode}", StockCode);
            AnalysisMessages.Clear();

#if DEBUG
            var mockAnalysisMessages = new List<AnalysisMessage>
            {
                new AnalysisMessage
                {
                    Sender = "技术分析师",
                    Content = $"【技术面分析】{StockCode} 当前技术指标显示：\n\n" +
                             "• MA5 和 MA10 呈现金叉形态，短期趋势向好\n" +
                             "• RSI 指标为 65，处于相对强势区间\n" +
                             "• MACD 柱状图由负转正，动能开始增强\n" +
                             "• 成交量较前期放大约 20%，资金关注度提升\n\n" +
                             "**技术面评级：看多** 📈",
                    Timestamp = DateTime.Now.AddMinutes(-5),
                    InputTokenCount = 156
                },
                new AnalysisMessage
                {
                    Sender = "基本面分析师",
                    Content = $"【基本面分析】{StockCode} 财务状况评估：\n\n" +
                             "• 最新季度营收同比增长 12.3%，盈利能力稳定\n" +
                             "• 毛利率维持在 35% 左右，成本控制良好\n" +
                             "• 资产负债率 45%，财务结构健康\n" +
                             "• ROE 为 15.2%，股东回报率较为理想\n" +
                             "• 现金流充裕，经营活动现金流为正\n\n" +
                             "**基本面评级：中性偏多** 📊",
                    Timestamp = DateTime.Now.AddMinutes(-4),
                    InputTokenCount = 189
                },
                new AnalysisMessage
                {
                    Sender = "综合策略分析师",
                    Content = $"【投资建议】{StockCode} 综合评估报告：\n\n" +
                             "**综合评级：买入** 🎯\n\n" +
                             "**核心逻辑：**\n" +
                             "1. 技术面多头排列，短期趋势明确向上\n" +
                             "2. 基本面稳健，盈利能力持续改善\n" +
                             "3. 资金面积极，机构资金持续流入\n" +
                             "4. 估值合理，仍有上升空间\n\n" +
                             "**操作建议：**\n" +
                             "• 目标价位：当前价格+15% 作为第一目标\n" +
                             "• 止损位：跌破 MA20 考虑减仓\n" +
                             "• 持有周期：建议 3-6 个月\n\n" +
                             "**风险提示：** 请注意控制仓位，做好风险管理 📋",
                    Timestamp = DateTime.Now.AddMinutes(-1),
                    InputTokenCount = 225
                }
            };

            foreach (var mockMessage in mockAnalysisMessages)
            {
                AnalysisMessages.Add(mockMessage);
                await Task.Delay(200);
            }

            // 加载模拟的分析报告数据
            AnalysisReportViewModel.LoadSampleData();
#else
            var report = await _marketAnalysisWorkflow.AnalyzeAsync(StockCode);
            
            // 处理分析结果（AnalystResultReceived 事件已经触发，这里处理 ChatHistory）
            foreach (var message in report.ChatHistory)
            {
                if (message.Role != Microsoft.Extensions.AI.ChatRole.Assistant)
                {
                    continue;
                }
                if (string.IsNullOrEmpty(message.Text?.Replace("\n\n", "")))
                {
                    continue;
                }
                var analysisMessage = new AnalysisMessage
                {
                    Sender = message.AuthorName ?? string.Empty,
                    Content = message.Text ?? string.Empty,
                    Timestamp = DateTime.Now,
                };
                if (message.AdditionalProperties != null && message.AdditionalProperties.TryGetValue("Usage", out var usageObject))
                {
                    if (usageObject is OpenAI.Chat.ChatTokenUsage openAIUsage)
                    {
                        analysisMessage.InputTokenCount = openAIUsage.InputTokenCount;
                        analysisMessage.OutputTokenCount = openAIUsage.OutputTokenCount;
                    }
                }

                AnalysisMessages.Add(analysisMessage);
            }
            
            // TODO: 缓存整个分析报告（需要扩展 IAnalysisCacheService 支持 MarketAnalysisReport）
            // await _analysisCacheService.CacheAnalysisAsync(StockCode, report);
#endif
            if (ChatSidebarViewModel != null)
            {
                await ChatSidebarViewModel.InitializeWithAnalysisHistory(StockCode, AnalysisMessages);
            }
        }, "股票分析");
    }

    /// <summary>
    /// 切换聊天侧边栏显示状态
    /// </summary>
    private void ToggleChatSidebar()
    {
        IsChatSidebarVisible = !IsChatSidebarVisible;
    }
}

