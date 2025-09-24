using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Applications.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace MarketAssistant.ViewModels;

[QueryProperty(nameof(StockCode), "code")]
public partial class AgentAnalysisViewModel : ViewModelBase
{
    private readonly MarketAnalysisAgent _marketAnalysisAgent;
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

    private bool _isRawDataViewVisible;
    public bool IsRawDataViewVisible
    {
        get => _isRawDataViewVisible;
        set => SetProperty(ref _isRawDataViewVisible, value);
    }

    private AnalysisReportViewModel _analysisReportViewModel;
    public AnalysisReportViewModel AnalysisReportViewModel
    {
        get => _analysisReportViewModel;
        set => SetProperty(ref _analysisReportViewModel, value);
    }

    public ICommand ToggleViewCommand { get; private set; }

    // 聊天侧边栏控制
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
                // 取消订阅旧的 ViewModel
                _chatSidebarViewModel.PropertyChanged -= OnChatSidebarPropertyChanged;
            }

            SetProperty(ref _chatSidebarViewModel, value);

            if (_chatSidebarViewModel != null)
            {
                // 订阅新的 ViewModel 的属性变更
                _chatSidebarViewModel.PropertyChanged += OnChatSidebarPropertyChanged;
            }

            // 通知代理属性已更改
            OnPropertyChanged(nameof(ChatMessages));
            OnPropertyChanged(nameof(UserInput));
            OnPropertyChanged(nameof(SendMessageCommand));
        }
    }

    // 聊天功能的代理属性，直接转发到 ChatSidebarViewModel
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
                OnPropertyChanged(); // 通知UI属性已更改
            }
        }
    }
    public ICommand SendMessageCommand => ChatSidebarViewModel?.SendMessageCommand ?? new RelayCommand(() => { });

    public AgentAnalysisViewModel(
        MarketAnalysisAgent marketAnalysisAgent,
        AnalysisReportViewModel analysisReportViewModel,
        IAnalysisCacheService analysisCacheService,
        ILogger<AgentAnalysisViewModel> logger) : base(logger)
    {
        _marketAnalysisAgent = marketAnalysisAgent;
        _analysisReportViewModel = analysisReportViewModel;
        _analysisCacheService = analysisCacheService;

        SubscribeToEvents();
        ToggleViewCommand = new RelayCommand(ToggleView);
        ToggleChatSidebarCommand = new RelayCommand(ToggleChatSidebar);

        // 临时调试：添加测试消息到空集合
        _emptyChatMessages.Add(new ChatMessageAdapter(
            "🔧 调试消息：如果你看到这条消息，说明绑定工作正常，但 ChatSidebarViewModel 为 null",
            false,
            "调试系统"));
    }

    private void SubscribeToEvents()
    {
        _marketAnalysisAgent.ProgressChanged += OnAnalysisProgressChanged;
        _marketAnalysisAgent.AnalysisCompleted += OnAnalysisCompleted;
    }

    /// <summary>
    /// 处理 ChatSidebarViewModel 的属性变更
    /// </summary>
    private void OnChatSidebarPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 当 ChatSidebarViewModel 的属性变更时，通知对应的代理属性
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

    private void ToggleView()
    {
        IsRawDataViewVisible = !IsRawDataViewVisible;
    }


    private void OnAnalysisProgressChanged(object sender, AnalysisProgressEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            CurrentAnalyst = e.CurrentAnalyst;
            IsAnalysisInProgress = e.IsInProgress;
            AnalysisStage = e.StageDescription;
        });
    }

    private void OnAnalysisCompleted(object sender, ChatMessageContent e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var message = new AnalysisMessage
            {
                Sender = e.AuthorName ?? string.Empty,
                Content = e.Content ?? string.Empty,
                Timestamp = DateTime.Now,
            };

            AnalysisMessages.Add(message);
            await AnalysisReportViewModel.ProcessAnalysisMessageAsync(message);
        });
    }


    public async Task LoadAnalysisDataAsync()
    {
        if (string.IsNullOrEmpty(StockCode))
            return;

        await SafeExecuteAsync(async () =>
        {
            // 首先尝试从缓存获取分析结果
            var cachedResult = await _analysisCacheService.GetCachedAnalysisAsync(StockCode);
            if (cachedResult != null)
            {
                Logger?.LogInformation("从缓存加载分析结果: {StockCode}", StockCode);

                // 使用缓存结果更新UI
                AnalysisReportViewModel.UpdateWithResult(cachedResult);
                return;
            }

            // 缓存中没有结果，执行新的分析
            Logger?.LogInformation("缓存中没有结果，开始新的分析: {StockCode}", StockCode);
            AnalysisMessages.Clear();

#if DEBUG
            // 模拟分析数据（避免调试时浪费Token）
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
                // 模拟分析过程的延迟
                await Task.Delay(200);
            }
#else
            var history = await _marketAnalysisAgent.AnalysisAsync(StockCode);
            foreach (var message in history)
            {
                if (message.Role != Microsoft.SemanticKernel.ChatCompletion.AuthorRole.Assistant)
                {
                    continue; // 只处理助手的消息
                }
                if (string.IsNullOrEmpty(message.Content.Replace("\n\n", "")))
                {
                    continue;
                }
                var analysisMessage = new AnalysisMessage
                {
                    Sender = message.AuthorName ?? string.Empty,
                    Content = message.Content ?? string.Empty,
                    Timestamp = DateTime.Now,
                };
                if (message.Metadata.TryGetValue("Usage", out var usageObject))
                {
                    if (usageObject is OpenAI.Chat.ChatTokenUsage openAIUsage)
                    {
                        analysisMessage.InputTokenCount = openAIUsage.InputTokenCount;
                        analysisMessage.OutputTokenCount = openAIUsage.OutputTokenCount;
                    }
                }

                AnalysisMessages.Add(analysisMessage);
            }
#endif
            //更新聊天侧边栏，初始化分析历史记录
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