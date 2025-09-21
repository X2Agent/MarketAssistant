using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 聊天侧边栏视图模型
/// </summary>
public partial class ChatSidebarViewModel : ViewModelBase
{
    #region 属性

    /// <summary>
    /// 聊天消息集合
    /// </summary>
    public ObservableCollection<ChatMessage> ChatMessages { get; } = new ObservableCollection<ChatMessage>();

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

    public ChatSidebarViewModel(Microsoft.Extensions.Logging.ILogger<ChatSidebarViewModel> logger) 
        : base(logger)
    {
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

        var userMessage = new ChatMessage
        {
            Content = UserInput.Trim(),
            IsUser = true,
            Sender = "用户",
            Timestamp = DateTime.Now,
            Status = MessageStatus.Sent
        };

        ChatMessages.Add(userMessage);
        var currentInput = UserInput;
        UserInput = string.Empty;

        try
        {
            // 显示AI正在思考的消息
            var thinkingMessage = new ChatMessage
            {
                Content = "正在分析中...",
                IsUser = false,
                Sender = "市场分析助手",
                Timestamp = DateTime.Now,
                Status = MessageStatus.Sending
            };

            ChatMessages.Add(thinkingMessage);

            // 模拟AI回复（这里应该集成真实的AI服务）
            await Task.Delay(2000);

            // 更新AI消息
            thinkingMessage.Content = GenerateAIResponse(currentInput);
            thinkingMessage.Status = MessageStatus.Sent;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "发送消息失败");
            
            // 显示错误消息
            var errorMessage = new ChatMessage
            {
                Content = "抱歉，消息发送失败，请稍后重试。",
                IsUser = false,
                Sender = "系统",
                Timestamp = DateTime.Now,
                Status = MessageStatus.Failed
            };

            ChatMessages.Add(errorMessage);
        }
    }


    #endregion

    #region 私有方法

    /// <summary>
    /// 生成AI回复（临时实现）
    /// </summary>
    private string GenerateAIResponse(string userInput)
    {
        // 这里应该集成真实的AI服务
        if (string.IsNullOrEmpty(StockCode))
        {
            return "请先选择要分析的股票，然后我可以为您提供更精准的分析和建议。";
        }

        return $"感谢您的提问。关于{StockCode}的详细分析，您可以查看完整的分析报告，或者提出更具体的问题，我会尽力为您解答。";
    }

    /// <summary>
    /// 将分析历史加载为聊天消息
    /// </summary>
    private void LoadAnalysisHistoryAsChat()
    {
        // 清空现有聊天消息
        ChatMessages.Clear();
        
        if (AnalysisMessages == null || !AnalysisMessages.Any())
        {
            // 如果没有分析历史，添加欢迎消息
            AddWelcomeMessage();
            return;
        }
        
        // 添加分析开始的系统消息
        var startMessage = new ChatMessage
        {
            Content = $"开始分析股票 {StockCode}，以下是各位分析师的观点：",
            IsUser = false,
            Sender = "系统",
            Timestamp = AnalysisMessages.First().Timestamp,
            Status = MessageStatus.Sent
        };
        ChatMessages.Add(startMessage);
        
        // 将每个分析消息转换为聊天消息
        foreach (var analysisMessage in AnalysisMessages)
        {
            if (string.IsNullOrWhiteSpace(analysisMessage.Content))
                continue;
                
            var chatMessage = new ChatMessage
            {
                Content = analysisMessage.Content,
                IsUser = false,
                Sender = analysisMessage.Sender,
                Timestamp = analysisMessage.Timestamp,
                Status = MessageStatus.Sent
            };
            
            ChatMessages.Add(chatMessage);
        }
        
        // 添加分析完成的系统消息
        if (AnalysisMessages.Any())
        {
            var endMessage = new ChatMessage
            {
                Content = "分析完成！您可以针对以上分析内容提出问题。",
                IsUser = false,
                Sender = "系统",
                Timestamp = AnalysisMessages.Last().Timestamp.AddSeconds(1),
                Status = MessageStatus.Sent
            };
            ChatMessages.Add(endMessage);
        }
    }
    
    /// <summary>
    /// 添加欢迎消息
    /// </summary>
    private void AddWelcomeMessage()
    {
        var welcomeMessage = new ChatMessage
        {
            Content = string.IsNullOrEmpty(StockCode) 
                ? "欢迎使用智能对话功能！请先选择要分析的股票。" 
                : $"欢迎使用智能对话功能！当前股票：{StockCode}。请开始分析后查看历史对话。",
            IsUser = false,
            Sender = "市场分析助手",
            Timestamp = DateTime.Now,
            Status = MessageStatus.Sent
        };
        
        ChatMessages.Add(welcomeMessage);
    }

    #endregion

    #region 公共方法

    /// <summary>
    /// 更新分析上下文
    /// </summary>
    public void UpdateAnalysisContext(string stockCode, ObservableCollection<AnalysisMessage> analysisMessages)
    {
        StockCode = stockCode;
        AnalysisMessages = analysisMessages;
        
        // 将分析历史转换为聊天消息并显示
        LoadAnalysisHistoryAsChat();
    }

    /// <summary>
    /// 添加系统消息
    /// </summary>
    public void AddSystemMessage(string content)
    {
        var systemMessage = new ChatMessage
        {
            Content = content,
            IsUser = false,
            Sender = "系统",
            Timestamp = DateTime.Now,
            Status = MessageStatus.Sent
        };

        ChatMessages.Add(systemMessage);
    }

    #endregion
}
