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
    private string userInput = string.Empty;

    /// <summary>
    /// 是否连接到AI服务
    /// </summary>
    [ObservableProperty]
    private bool isConnected = true;

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

    #endregion

    #region 命令

    public ICommand SendMessageCommand { get; }

    #endregion

    #region 构造函数

    public ChatSidebarViewModel(
        Microsoft.Extensions.Logging.ILogger<ChatSidebarViewModel> logger,
        MarketChatAgent chatAgent) 
        : base(logger)
    {
        _chatAgent = chatAgent;
        SendMessageCommand = new RelayCommand(SendMessage, CanSendMessage);
        
        // 添加初始欢迎消息
        AddWelcomeMessage();
    }

    #endregion

    #region 命令实现

    /// <summary>
    /// 是否可以发送消息
    /// </summary>
    private bool CanSendMessage()
    {
        return !string.IsNullOrWhiteSpace(UserInput) && IsConnected;
    }

    /// <summary>
    /// 发送消息
    /// </summary>
    private async void SendMessage()
    {
        if (string.IsNullOrWhiteSpace(UserInput))
            return;

        var userMessage = new ChatMessageAdapter(UserInput.Trim(), true, "用户");

        ChatMessages.Add(userMessage);
        var currentInput = UserInput;
        UserInput = string.Empty;

        try
        {
            // 显示AI正在思考的消息
            var thinkingMessage = new ChatMessageAdapter("正在分析中...", false, "市场分析助手")
            {
                Status = MessageStatus.Sending
            };

            ChatMessages.Add(thinkingMessage);

            // 调用真实的AI服务
            var aiResponse = await _chatAgent.SendMessageAsync(currentInput);

            // 更新AI消息
            thinkingMessage.Content = aiResponse.Content ?? "抱歉，无法生成回复";
            thinkingMessage.Status = MessageStatus.Sent;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "发送消息失败");
            
            // 更新思考消息为失败状态
            var thinkingMessage = ChatMessages.LastOrDefault(m => m.Status == MessageStatus.Sending);
            if (thinkingMessage != null)
            {
                thinkingMessage.Content = "抱歉，消息发送失败，请稍后重试。";
                thinkingMessage.Status = MessageStatus.Failed;
            }
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
    public async Task InitializeWithAnalysisHistory(string stockCode, ChatHistory analysisHistory)
    {
        StockCode = stockCode;
        
        // 更新ChatAgent的股票上下文
        await _chatAgent.UpdateStockContextAsync(stockCode);
        
        // 添加分析历史作为上下文
        foreach (var message in analysisHistory)
        {
            if (message.Role == AuthorRole.Assistant && !string.IsNullOrWhiteSpace(message.Content))
            {
                _chatAgent.AddSystemMessage($"分析师观点：{message.Content}");
            }
        }
        
        // 从ChatAgent的历史记录加载聊天消息
        var history = new ChatHistory();
        foreach (var message in _chatAgent.ConversationHistory)
        {
            history.Add(message);
        }
        LoadFromChatHistory(history);
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
    /// 清空聊天历史
    /// </summary>
    public void ClearChatHistory()
    {
        ChatMessages.Clear();
        _chatAgent.ClearHistory();
        AddWelcomeMessage();
    }

    /// <summary>
    /// 从ChatHistory加载聊天消息
    /// </summary>
    public void LoadFromChatHistory(ChatHistory history)
    {
        ChatMessages.Clear();
        
        foreach (var message in history)
        {
            if (string.IsNullOrWhiteSpace(message.Content))
                continue;
                
            var chatMessage = new ChatMessageAdapter(message);
            ChatMessages.Add(chatMessage);
        }
        
        // 如果没有消息，添加欢迎消息
        if (!ChatMessages.Any())
        {
            AddWelcomeMessage();
        }
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
