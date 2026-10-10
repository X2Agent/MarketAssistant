using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.AdaptiveCards;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services;
using MarketAssistant.Services.Dialog;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.Diagnostics;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 聊天侧边栏视图模型
/// </summary>
public partial class ChatSidebarViewModel : ViewModelBase, IDisposable
{
    private readonly IMarketChatSessionFactory _chatSessionFactory;
    private readonly ChatSessionPersistenceService _sessionPersistence;
    private readonly IDialogService _dialogService;
    private readonly AdaptiveCardConverter _adaptiveCardConverter;
    private MarketChatSession? _chatSession;
    private string? _pendingContextStockCode;
    private List<ChatMessage>? _pendingAnalysisMessages;

    public ObservableCollection<ChatMessageAdapter> ChatMessages { get; } = [];

    /// <summary>历史会话列表（按最近活跃倒序，来自持久化层摘要查询）。</summary>
    public ObservableCollection<ChatSessionSummaryItem> SessionHistory { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string _userInput = string.Empty;

    [ObservableProperty]
    private string _stockCode = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private bool _isProcessing = false;

    [ObservableProperty]
    private string _sendButtonText = "➤";

    /// <summary>历史会话面板是否展开。</summary>
    [ObservableProperty]
    private bool _isSessionHistoryOpen;

    /// <summary>是否存在历史会话（控制列表空态提示的显隐）。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoSessionHistory))]
    private bool _hasSessionHistory;

    /// <summary>无历史会话时的空态提示（取反便于编译绑定直接使用）。</summary>
    public bool HasNoSessionHistory => !HasSessionHistory;

    private CancellationTokenSource? _currentCancellationTokenSource;

    /// <summary>
    /// 流式内容刷新到 UI 的最小间隔：把逐 chunk 的高频赋值合并为按时间片批量刷新
    /// </summary>
    private static readonly TimeSpan ContentFlushInterval = TimeSpan.FromMilliseconds(100);
    private static readonly long FlushIntervalTicks = ContentFlushInterval.Ticks * Stopwatch.Frequency / TimeSpan.TicksPerSecond;
    private long _lastFlushTimestamp;

    public IAsyncRelayCommand SendMessageCommand { get; }

    /// <summary>展开/收起历史会话面板（展开时加载列表）。</summary>
    public IAsyncRelayCommand ToggleSessionHistoryCommand { get; }

    /// <summary>刷新历史会话列表。</summary>
    public IAsyncRelayCommand RefreshSessionHistoryCommand { get; }

    /// <summary>恢复指定历史会话（参数为 <see cref="ChatSessionSummaryItem"/>）。</summary>
    public IAsyncRelayCommand RestoreSessionCommand { get; }

    /// <summary>删除指定历史会话（参数为 <see cref="ChatSessionSummaryItem"/>）。</summary>
    public IAsyncRelayCommand DeleteSessionCommand { get; }

    /// <summary>开启新会话（丢弃当前内存会话，下次发言时按当前标的重建）。</summary>
    public IRelayCommand NewSessionCommand { get; }

    public ChatSidebarViewModel(
        ILogger<ChatSidebarViewModel> logger,
        IMarketChatSessionFactory chatSessionFactory,
        ChatSessionPersistenceService sessionPersistence,
        IDialogService dialogService,
        AdaptiveCardConverter adaptiveCardConverter)
        : base(logger)
    {
        _chatSessionFactory = chatSessionFactory;
        _sessionPersistence = sessionPersistence;
        _dialogService = dialogService;
        _adaptiveCardConverter = adaptiveCardConverter;

        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessage);
        ToggleSessionHistoryCommand = new AsyncRelayCommand(ToggleSessionHistoryAsync);
        RefreshSessionHistoryCommand = new AsyncRelayCommand(LoadSessionHistoryAsync);
        RestoreSessionCommand = new AsyncRelayCommand<ChatSessionSummaryItem?>(RestoreSessionAsync);
        DeleteSessionCommand = new AsyncRelayCommand<ChatSessionSummaryItem?>(DeleteSessionAsync);
        NewSessionCommand = new RelayCommand(StartNewSession);
    }

    /// <summary>
    /// 是否可以发送消息
    /// </summary>
    private bool CanSendMessage()
    {
        return !string.IsNullOrWhiteSpace(UserInput) || IsProcessing;
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    private async Task SendMessageAsync()
    {
        if (IsProcessing)
        {
            _currentCancellationTokenSource?.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(UserInput))
            return;

        var userMessage = new ChatMessageAdapter(new ChatMessage(ChatRole.User, UserInput.Trim()) { AuthorName = "用户" }, _adaptiveCardConverter);
        ChatMessages.Add(userMessage);

        var currentInput = UserInput;
        UserInput = string.Empty;

        IsProcessing = true;
        SendButtonText = "⏹";

        var aiMessage = new ChatMessageAdapter(new ChatMessage(ChatRole.Assistant, "") { AuthorName = "市场分析助手" }, _adaptiveCardConverter)
        {
            Status = MessageStatus.Sending
        };
        ChatMessages.Add(aiMessage);

        try
        {
            EnsureChatSession();
            _currentCancellationTokenSource = new CancellationTokenSource();
            var contentBuilder = new System.Text.StringBuilder();
            bool hasReceivedContent = false;
            _lastFlushTimestamp = 0L;

            await foreach (var chunk in _chatSession!.SendMessageStreamAsync(currentInput, _currentCancellationTokenSource.Token))
            {
                if (string.IsNullOrEmpty(chunk))
                    continue;

                contentBuilder.Append(chunk);

                if (!hasReceivedContent)
                {
                    hasReceivedContent = true;
                    aiMessage.Status = MessageStatus.Streaming;
                }

                // chunk 合并节流：逐 chunk 全量赋值会让绑定→重渲染管线高频空转，
                // 按固定间隔把累积内容批量刷到 Content，流结束后再补一次最终刷新
                var now = Stopwatch.GetTimestamp();
                if (now - _lastFlushTimestamp >= FlushIntervalTicks)
                {
                    _lastFlushTimestamp = now;
                    aiMessage.Content = contentBuilder.ToString();
                }
            }

            aiMessage.Content = contentBuilder.ToString();
            aiMessage.Status = MessageStatus.Sent;
        }
        catch (OperationCanceledException)
        {
            aiMessage.Content = "对话已取消";
            aiMessage.Status = MessageStatus.Failed;
            Logger?.LogInformation("用户取消了对话请求");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "发送消息失败");

            // 根据异常类型提供更友好的提示
            aiMessage.Content = ex switch
            {
                HttpRequestException => "网络连接失败，请检查网络后重试",
                UnauthorizedAccessException => "API密钥无效，请在设置中检查配置",
                TaskCanceledException => "请求超时，请稍后重试",
                _ => ErrorMessageMapper.GetUserFriendlyMessage(ex)
            };

            aiMessage.Status = MessageStatus.Failed;
        }
        finally
        {
            IsProcessing = false;
            SendButtonText = "➤";
            _currentCancellationTokenSource?.Dispose();
            _currentCancellationTokenSource = null;
        }
    }

    private void EnsureChatSession()
    {
        if (_chatSession is not null)
            return;

        _chatSession = _chatSessionFactory.Create(_pendingContextStockCode ?? StockCode);

        if (_pendingContextStockCode is not null && _pendingAnalysisMessages is not null)
        {
            _chatSession!.InjectAnalysisContext(_pendingContextStockCode, _pendingAnalysisMessages);
            _pendingContextStockCode = null;
            _pendingAnalysisMessages = null;
        }
    }

    /// <summary>
    /// 展开/收起历史会话面板：展开时拉取会话摘要并标记当前会话。
    /// </summary>
    private async Task ToggleSessionHistoryAsync()
    {
        IsSessionHistoryOpen = !IsSessionHistoryOpen;
        if (IsSessionHistoryOpen)
        {
            await LoadSessionHistoryAsync();
        }
    }

    /// <summary>
    /// 加载历史会话列表（按最近活跃倒序）。持久化层在每次对话结束后自动保存，
    /// 打开面板时拉取即可反映最新状态，无需额外的手动保存入口。
    /// </summary>
    private async Task LoadSessionHistoryAsync()
    {
        List<ChatSessionSummary> summaries;
        try
        {
            summaries = await _sessionPersistence.GetSessionSummariesAsync();
        }
        catch (Exception ex)
        {
            // 列表加载失败不应影响聊天主流程：降级为空列表，面板内展示空态
            Logger?.LogError(ex, "加载历史会话列表失败");
            SessionHistory.Clear();
            HasSessionHistory = false;
            return;
        }

        var activeSessionId = _chatSession?.SessionId;
        SessionHistory.Clear();
        foreach (var summary in summaries)
        {
            SessionHistory.Add(new ChatSessionSummaryItem(summary)
            {
                IsActive = summary.Id == activeSessionId
            });
        }

        HasSessionHistory = SessionHistory.Count > 0;
    }

    /// <summary>
    /// 恢复历史会话：反序列化 MAF 会话状态与消息镜像，并用恢复后的历史重绘聊天列表。
    /// </summary>
    private async Task RestoreSessionAsync(ChatSessionSummaryItem? item)
    {
        if (item is null)
            return;

        // 会话懒创建：侧边栏尚未发过消息时 _chatSession 为 null，先建壳再恢复
        EnsureChatSession();

        bool restored;
        try
        {
            restored = await _chatSession!.RestoreSessionAsync(item.Id);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "恢复历史会话失败: {SessionId}", item.Id);
            return;
        }

        if (!restored)
        {
            // 快照已不存在（别处删除）：刷新列表让其消失，当前会话保持不变
            Logger?.LogInformation("历史会话快照已不存在，刷新列表: {SessionId}", item.Id);
            await LoadSessionHistoryAsync();
            return;
        }

        StockCode = _chatSession!.CurrentStockCode;
        await RefreshMessagesFromSessionAsync();
        IsSessionHistoryOpen = false;
        await LoadSessionHistoryAsync();

        Logger?.LogInformation("已恢复历史会话: {SessionId} {Title}", item.Id, item.Title);
    }

    /// <summary>
    /// 删除历史会话。删除的若是当前会话，同时开启新会话——
    /// 否则下一条消息会自动保存，把同一 ID 的会话重新写回库，与用户意图相悖。
    /// </summary>
    private async Task DeleteSessionAsync(ChatSessionSummaryItem? item)
    {
        if (item is null)
            return;

        var confirmed = await _dialogService.ShowConfirmationAsync(
            "删除会话",
            $"确定要删除会话「{item.Title}」吗？删除后无法恢复。",
            "删除",
            "取消");
        if (!confirmed)
            return;

        try
        {
            await _sessionPersistence.DeleteSessionAsync(item.Id);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "删除历史会话失败: {SessionId}", item.Id);
            return;
        }

        SessionHistory.Remove(item);
        HasSessionHistory = SessionHistory.Count > 0;

        if (item.IsActive)
        {
            StartNewSession();
        }

        Logger?.LogInformation("已删除历史会话: {SessionId} {Title}", item.Id, item.Title);
    }

    /// <summary>
    /// 开启新会话：丢弃当前内存会话（已自动保存的内容不丢），清空消息列表。
    /// 保留 <see cref="StockCode"/>——新会话仍围绕当前标的继续。
    /// </summary>
    private void StartNewSession()
    {
        _chatSession?.Dispose();
        _chatSession = null;
        ChatMessages.Clear();
        AddWelcomeMessage();
        IsSessionHistoryOpen = false;
    }

    /// <summary>
    /// 用会话内的消息镜像重绘聊天列表（恢复会话后调用）。
    /// </summary>
    private async Task RefreshMessagesFromSessionAsync()
    {
        var history = await _chatSession!.GetConversationHistoryAsync();

        ChatMessages.Clear();
        var hasVisibleMessages = false;
        foreach (var message in history)
        {
            if (!IsDisplayableAnalystMessage(message))
                continue;

            ChatMessages.Add(new ChatMessageAdapter(message, _adaptiveCardConverter));
            hasVisibleMessages = true;
        }

        if (!hasVisibleMessages)
        {
            AddWelcomeMessage();
        }
    }

    /// <summary>
    /// 添加欢迎消息
    /// </summary>
    private void AddWelcomeMessage()
    {
        var content = string.IsNullOrEmpty(StockCode)
            ? "欢迎使用智能助手！请先选择要分析的股票。"
            : $"欢迎使用智能助手！当前股票：{StockCode}。请开始分析后查看历史对话。";

        var welcomeMessage = new ChatMessageAdapter(new ChatMessage(ChatRole.Assistant, content) { AuthorName = "市场分析助手" }, _adaptiveCardConverter);
        ChatMessages.Add(welcomeMessage);
    }

    /// <summary>
    /// 判断分析消息是否适合在聊天侧栏展示：
    /// 过滤系统内部说明（产物读取指引、维度缺失说明等），
    /// 保留分析师的真实结论文本与失败标记（失败标记渲染为灰色占位卡片）。
    /// </summary>
    private static bool IsDisplayableAnalystMessage(ChatMessage message)
        => message.Role != ChatRole.System &&
           !string.Equals(message.AuthorName, "SystemNotice", StringComparison.Ordinal) &&
           !string.IsNullOrWhiteSpace(message.Text);

    /// <summary>
    /// 使用分析结果初始化对话上下文。
    /// 分析结果会注入到 MAF 会话的系统指令中，同时在 UI 上展示。
    /// </summary>
    public Task InitializeWithAnalysisHistory(string stockCode, IEnumerable<ChatMessage> analysisMessages)
    {
        StockCode = stockCode;

        var messages = analysisMessages
            .Where(IsDisplayableAnalystMessage)
            .ToList();
        if (_chatSession is not null)
        {
            _chatSession.InjectAnalysisContext(stockCode, messages);
        }
        else
        {
            _pendingContextStockCode = stockCode;
            _pendingAnalysisMessages = messages;
        }

        ChatMessages.Clear();

        bool hasVisibleMessages = false;
        foreach (var message in messages)
        {
            if (!IsDisplayableAnalystMessage(message)) continue;

            ChatMessages.Add(new ChatMessageAdapter(message, _adaptiveCardConverter));
            hasVisibleMessages = true;
        }

        if (!hasVisibleMessages)
        {
            AddWelcomeMessage();
        }
        else
        {
            var contextMessage = new ChatMessageAdapter(
                new ChatMessage(ChatRole.System, $"以上是关于 {stockCode} 的分析数据，可基于这些信息继续提问。")
                { AuthorName = "系统" }, _adaptiveCardConverter);
            ChatMessages.Add(contextMessage);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 初始化为空白状态（显示欢迎消息）
    /// </summary>
    public void InitializeEmpty()
    {
        ChatMessages.Clear();
        AddWelcomeMessage();
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    public void Dispose()
    {
        _chatSession?.Dispose();
        _currentCancellationTokenSource?.Cancel();
        _currentCancellationTokenSource?.Dispose();
        _currentCancellationTokenSource = null;
        GC.SuppressFinalize(this);
    }
}

