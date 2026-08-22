using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.AdaptiveCards;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 聊天侧边栏视图模型
/// </summary>
public partial class ChatSidebarViewModel : ViewModelBase, IDisposable
{
    private readonly IMarketChatSessionFactory _chatSessionFactory;
    private readonly AdaptiveCardConverter _adaptiveCardConverter;
    private MarketChatSession? _chatSession;
    private string? _pendingContextStockCode;
    private List<ChatMessage>? _pendingAnalysisMessages;

    public ObservableCollection<ChatMessageAdapter> ChatMessages { get; } = [];

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

    private CancellationTokenSource? _currentCancellationTokenSource;

    public IAsyncRelayCommand SendMessageCommand { get; }

    public ChatSidebarViewModel(
        ILogger<ChatSidebarViewModel> logger,
        IMarketChatSessionFactory chatSessionFactory,
        AdaptiveCardConverter adaptiveCardConverter)
        : base(logger)
    {
        _chatSessionFactory = chatSessionFactory;
        _adaptiveCardConverter = adaptiveCardConverter;

        SendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessage);
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

            await foreach (var chunk in _chatSession!.SendMessageStreamAsync(currentInput, _currentCancellationTokenSource.Token))
            {
                if (!string.IsNullOrEmpty(chunk))
                {
                    contentBuilder.Append(chunk);

                    if (!hasReceivedContent)
                    {
                        hasReceivedContent = true;
                        aiMessage.Status = MessageStatus.Streaming;
                        aiMessage.Content = chunk;
                    }
                    else
                    {
                        aiMessage.Content = contentBuilder.ToString();
                    }
                }
            }

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
    /// 添加欢迎消息
    /// </summary>
    private void AddWelcomeMessage()
    {
        var content = string.IsNullOrEmpty(StockCode)
            ? "欢迎使用智能对话功能！请先选择要分析的股票。"
            : $"欢迎使用智能对话功能！当前股票：{StockCode}。请开始分析后查看历史对话。";

        var welcomeMessage = new ChatMessageAdapter(new ChatMessage(ChatRole.Assistant, content) { AuthorName = "市场分析助手" }, _adaptiveCardConverter);
        ChatMessages.Add(welcomeMessage);
    }

    /// <summary>
    /// 使用分析结果初始化对话上下文。
    /// 分析结果会注入到 MAF 会话的系统指令中，同时在 UI 上展示。
    /// </summary>
    public Task InitializeWithAnalysisHistory(string stockCode, IEnumerable<ChatMessage> analysisMessages)
    {
        StockCode = stockCode;

        var messages = analysisMessages.ToList();
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
            if (string.IsNullOrWhiteSpace(message.Text)) continue;

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

