using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using MarketAssistant.Agents;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 聊天侧边栏视图模型
/// </summary>
public partial class ChatSidebarViewModel : ViewModelBase
{
    #region 私有字段

    private readonly MarketChatAgent _chatAgent;

    #endregion

    #region 属性

    /// <summary>
    /// 聊天消息集合
    /// </summary>
    public ObservableCollection<ChatMessageAdapter> ChatMessages { get; } = new ObservableCollection<ChatMessageAdapter>();

    /// <summary>
    /// 用户输入内容
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private string userInput = string.Empty;


    /// <summary>
    /// 当前股票代码（用于上下文）
    /// </summary>
    [ObservableProperty]
    private string stockCode = string.Empty;

    /// <summary>
    /// 分析消息集合（用于上下文参考）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AnalysisMessage> analysisMessages = new();

    /// <summary>
    /// 是否正在处理请求
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendMessageCommand))]
    private bool isProcessing = false;

    /// <summary>
    /// 发送按钮文本
    /// </summary>
    [ObservableProperty]
    private string sendButtonText = "➤";

    /// <summary>
    /// 当前取消令牌源
    /// </summary>
    private CancellationTokenSource? _currentCancellationTokenSource;

    #endregion

    #region 命令

    public IRelayCommand SendMessageCommand { get; }

    #endregion

    #region 事件

    /// <summary>
    /// 滚动到底部事件
    /// </summary>
    public event Action? ScrollToBottom;

    #endregion

    #region 构造函数

    public ChatSidebarViewModel(
        ILogger<ChatSidebarViewModel> logger,
        MarketChatAgent chatAgent) 
        : base(logger)
    {
        _chatAgent = chatAgent;
        SendMessageCommand = new RelayCommand(SendMessage, CanSendMessage);
    }

    #endregion

    #region 命令实现

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
        // 如果正在处理，则取消当前请求
        if (IsProcessing)
        {
            _currentCancellationTokenSource?.Cancel();
            return;
        }

        if (string.IsNullOrWhiteSpace(UserInput))
            return;

        var userMessage = new ChatMessageAdapter(UserInput.Trim(), true, "用户");
        ChatMessages.Add(userMessage);
        
        // 立即滚动到用户消息
        ScrollToBottom?.Invoke();
        
        var currentInput = UserInput;
        UserInput = string.Empty;
        
        // 更新UI状态
        IsProcessing = true;
        SendButtonText = "⏹";
        
        // 创建AI消息用于流式更新
        var aiMessage = new ChatMessageAdapter("", false, "市场分析助手")
        {
            Status = MessageStatus.Sending
        };
        ChatMessages.Add(aiMessage);
        
        // 滚动到AI消息（显示"正在思考"状态）
        ScrollToBottom?.Invoke();

        try
        {
            _currentCancellationTokenSource = new CancellationTokenSource();
            var contentBuilder = new System.Text.StringBuilder();
            
            // 使用流式API
            bool firstChunk = true;
            await foreach (var chunk in _chatAgent.SendMessageStreamAsync(currentInput, _currentCancellationTokenSource.Token))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    contentBuilder.Append(chunk.Content);
                    
                    // 实时更新消息内容
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        aiMessage.Content = contentBuilder.ToString();
                        
                        // 第一个内容块时触发滚动，确保用户能看到AI开始回复
                        if (firstChunk)
                        {
                            ScrollToBottom?.Invoke();
                            firstChunk = false;
                        }
                    });
                }
            }
            
            aiMessage.Status = MessageStatus.Sent;
            
            // 触发滚动到底部
            MainThread.BeginInvokeOnMainThread(() => ScrollToBottom?.Invoke());
        }
        catch (OperationCanceledException)
        {
            aiMessage.Content = "对话已取消";
            aiMessage.Status = MessageStatus.Failed;
            Logger.LogInformation("用户取消了对话请求");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "发送消息失败");
            aiMessage.Content = "抱歉，消息发送失败，请稍后重试。";
            aiMessage.Status = MessageStatus.Failed;
        }
        finally
        {
            IsProcessing = false;
            SendButtonText = "➤";
            _currentCancellationTokenSource = null;
        }
    }

    #endregion

    #region 私有方法


    
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
        
        // 临时调试输出
        Logger?.LogInformation("已添加欢迎消息，当前消息数量: {Count}, 内容: {Content}", 
            ChatMessages.Count, content);
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 初始化分析历史记录
    /// </summary>
    public async Task InitializeWithAnalysisHistory(string stockCode, IEnumerable<AnalysisMessage> analysisMessages)
    {
        StockCode = stockCode;
        
        // 更新ChatAgent的股票上下文
        await _chatAgent.UpdateStockContextAsync(stockCode);
        
        // 清空当前消息
        ChatMessages.Clear();
        
        // 将分析历史直接显示在UI中
        bool hasVisibleMessages = false;
        foreach (var analysisMessage in analysisMessages)
        {
            if (!string.IsNullOrWhiteSpace(analysisMessage.Content))
            {
                // 添加到ChatAgent作为上下文
                _chatAgent.AddSystemMessage($"分析师观点：{analysisMessage.Content}");
                
                // 使用AnalysisMessage创建ChatMessageAdapter，保留原始时间戳和发送者信息
                var displayMessage = new ChatMessageAdapter(analysisMessage);
                ChatMessages.Add(displayMessage);
                hasVisibleMessages = true;
            }
        }
        
        // 如果没有可显示的分析历史，则显示欢迎消息
        if (!hasVisibleMessages)
        {
            AddWelcomeMessage();
        }
        else
        {
            // 添加一个系统提示，说明这是历史分析数据
            var contextMessage = new ChatMessageAdapter(
                $"以上是关于 {stockCode} 的历史分析数据。您可以基于这些信息继续提问。", 
                false, 
                "系统");
            ChatMessages.Add(contextMessage);
        }
        
        // 初始化完成后滚动到底部（延迟确保UI渲染完成）
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(300);
            ScrollToBottom?.Invoke();
        });
    }

    /// <summary>
    /// 添加系统消息
    /// </summary>
    public void AddSystemMessage(string content)
    {
        var systemMessage = new ChatMessageAdapter(content, false, "系统");
        ChatMessages.Add(systemMessage);
        
        // 同时添加到ChatAgent的历史中
        _chatAgent.AddSystemMessage(content);
    }

    /// <summary>
    /// 初始化为空白状态（显示欢迎消息）
    /// </summary>
    public void InitializeEmpty()
    {
        ChatMessages.Clear();
        AddWelcomeMessage();
        
        // 初始化完成后滚动到底部（延迟确保UI渲染完成）
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(300);
            ScrollToBottom?.Invoke();
        });
    }

    /// <summary>
    /// 清空聊天历史
    /// </summary>
    public void ClearChatHistory()
    {
        ChatMessages.Clear();
        _chatAgent.ClearHistory();
        AddWelcomeMessage();
        
        // 清空后滚动到底部（延迟确保UI渲染完成）
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(300);
            ScrollToBottom?.Invoke();
        });
    }

    /// <summary>
    /// 从ChatHistory加载聊天消息（主要用于加载用户对话历史）
    /// </summary>
    public void LoadFromChatHistory(ChatHistory history)
    {
        ChatMessages.Clear();
        
        bool hasUserMessages = false;
        foreach (var message in history)
        {
            if (string.IsNullOrWhiteSpace(message.Content))
                continue;
            
            // 跳过系统消息，只显示用户和助手的对话
            if (message.Role == AuthorRole.System)
                continue;
                
            var chatMessage = new ChatMessageAdapter(message);
            ChatMessages.Add(chatMessage);
            
            if (message.Role == AuthorRole.User || message.Role == AuthorRole.Assistant)
                hasUserMessages = true;
        }
        
        // 如果没有用户对话消息，添加欢迎消息
        if (!hasUserMessages)
        {
            AddWelcomeMessage();
        }
        
        // 加载完成后滚动到底部（延迟确保UI渲染完成）
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(300);
            ScrollToBottom?.Invoke();
        });
    }

    /// <summary>
    /// 转换为ChatHistory
    /// </summary>
    public ChatHistory ToChatHistory()
    {
        var history = new ChatHistory();
        
        foreach (var message in ChatMessages)
        {
            // 跳过系统消息和欢迎消息
            if (message.Sender == "系统" || message.Sender == "市场分析助手")
                continue;
                
            history.Add(message.ToChatMessageContent());
        }
        
        return history;
    }

    #endregion
}
