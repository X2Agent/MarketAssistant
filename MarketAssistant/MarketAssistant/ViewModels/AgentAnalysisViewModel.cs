using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Applications.Cache;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

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
    private readonly ObservableCollection<ChatMessage> _emptyChatMessages = new();
    public ObservableCollection<ChatMessage> ChatMessages => ChatSidebarViewModel?.ChatMessages ?? _emptyChatMessages;
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
        _emptyChatMessages.Add(new ChatMessage
        {
            Content = "🔧 调试消息：如果你看到这条消息，说明绑定工作正常，但 ChatSidebarViewModel 为 null",
            IsUser = false,
            Sender = "调试系统",
            Timestamp = DateTime.Now,
            Status = MessageStatus.Sent
        });
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
                InputTokenCount = 0,
                OutputTokenCount = 0,
                TotalTokenCount = 0
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
            var history = await _marketAnalysisAgent.AnalysisAsync(StockCode);

            foreach (var message in history)
            {
                if (message.Role != AuthorRole.Assistant)
                {
                    continue; // 只处理助手的消息
                }
                if (string.IsNullOrEmpty(message.Content.Replace("\n\n", "")))
                {
                    continue;
                }
                AnalysisMessages.Add(new AnalysisMessage()
                {
                    Sender = message.AuthorName ?? string.Empty,
                    Content = message.Content ?? string.Empty,
                    Timestamp = DateTime.Now,
                    InputTokenCount = 0,
                    OutputTokenCount = 0,
                    TotalTokenCount = 0
                });
            }
        }, "股票分析");
    }

    /// <summary>
    /// 切换聊天侧边栏显示状态
    /// </summary>
    private void ToggleChatSidebar()
    {
        IsChatSidebarVisible = !IsChatSidebarVisible;
        
        // 当打开聊天侧边栏时，更新聊天上下文
        if (IsChatSidebarVisible && ChatSidebarViewModel != null)
        {
            ChatSidebarViewModel.UpdateAnalysisContext(StockCode, AnalysisMessages);
        }
    }





}