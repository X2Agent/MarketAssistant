using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Services.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 聊天侧边栏视图模型
/// </summary>
public partial class ChatSidebarViewModel : ViewModelBase
{
    private readonly MarketChatSession _chatSession;

    /// <summary>
    /// 聊天消息集合
    /// </summary>
    public ObservableCollection<ChatMessageAdapter> ChatMessages { get; } = new ObservableCollection<ChatMessageAdapter>();

    /// <summary>
    /// 用户输入内容
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string _userInput = string.Empty;

    /// <summary>
    /// 当前股票代码（用于上下文）
    /// </summary>
    [ObservableProperty]
    private string _stockCode = string.Empty;

    /// <summary>
    /// 是否正在处理请求
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private bool _isProcessing = false;

    /// <summary>
    /// 发送按钮文本
    /// </summary>
    [ObservableProperty]
    private string _sendButtonText = "➤";

    /// <summary>
    /// 当前取消令牌源
    /// </summary>
    private CancellationTokenSource? _currentCancellationTokenSource;

    public IRelayCommand SendMessageCommand { get; }

    public ChatSidebarViewModel(
        ILogger<ChatSidebarViewModel> logger,
        IChatClientFactory chatClientFactory,
        ILoggerFactory loggerFactory,
        McpService mcpService)
        : base(logger)
    {
        // 创建新的聊天会话
        var chatClient = chatClientFactory.CreateClient();
        var sessionLogger = loggerFactory.CreateLogger<MarketChatSession>();
        _chatSession = new MarketChatSession(chatClient, sessionLogger, mcpService);

        SendMessageCommand = new RelayCommand(SendMessage, CanSendMessage);
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
    private async void SendMessage()
    {
        if (IsProcessing)
        {
            _currentCancellationTokenSource?.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(UserInput))
            return;

        var userMessage = new ChatMessageAdapter(UserInput.Trim(), true, "用户");
        ChatMessages.Add(userMessage);

        var currentInput = UserInput;
        UserInput = string.Empty;

        IsProcessing = true;
        SendButtonText = "⏹";

        var aiMessage = new ChatMessageAdapter("", false, "市场分析助手")
        {
            Status = MessageStatus.Sending
        };
        ChatMessages.Add(aiMessage);

        try
        {
            _currentCancellationTokenSource = new CancellationTokenSource();
            var contentBuilder = new System.Text.StringBuilder();
            bool hasReceivedContent = false;

            await foreach (var chunk in _chatSession.SendMessageStreamAsync(currentInput, _currentCancellationTokenSource.Token))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    contentBuilder.Append(chunk.Content);

                    if (!hasReceivedContent)
                    {
                        hasReceivedContent = true;
                        aiMessage.Status = MessageStatus.Streaming;
                        aiMessage.Content = chunk.Content;
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
            _currentCancellationTokenSource = null;
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

        var welcomeMessage = new ChatMessageAdapter(content, false, "市场分析助手");
        ChatMessages.Add(welcomeMessage);
    }

    /// <summary>
    /// 初始化分析历史记录
    /// </summary>
    public async Task InitializeWithAnalysisHistory(string stockCode, IEnumerable<AnalysisMessage> analysisMessages)
    {
        StockCode = stockCode;
        
        // 设置股票代码（不需要异步操作）
        _chatSession.SetStockCode(stockCode);

        ChatMessages.Clear();

        bool hasVisibleMessages = false;
        foreach (var analysisMessage in analysisMessages)
        {
            if (!string.IsNullOrWhiteSpace(analysisMessage.Content))
            {
                _chatSession.AddAssistantMessage($"分析师观点：{analysisMessage.Content}");

                var displayMessage = new ChatMessageAdapter(analysisMessage);
                ChatMessages.Add(displayMessage);
                hasVisibleMessages = true;
            }
        }

        if (!hasVisibleMessages)
        {
            AddWelcomeMessage();
        }
        else
        {
            var contextMessage = new ChatMessageAdapter(
                $"以上是关于 {stockCode} 的历史分析数据。您可以基于这些信息继续提问。",
                false,
                "系统");
            ChatMessages.Add(contextMessage);
        }
    }

    /// <summary>
    /// 添加系统消息
    /// </summary>
    public void AddSystemMessage(string content)
    {
        var systemMessage = new ChatMessageAdapter(content, false, "系统");
        ChatMessages.Add(systemMessage);

        _chatSession.AddAssistantMessage(content);
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
    /// 清空聊天历史
    /// </summary>
    public void ClearChatHistory()
    {
        ChatMessages.Clear();
        _chatSession.ClearHistory();
            AddWelcomeMessage();
                           }
}

