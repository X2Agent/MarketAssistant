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
    public ICommand ClearChatCommand { get; }
    public ICommand ExportChatCommand { get; }

    #endregion

    #region 构造函数

    public ChatSidebarViewModel(Microsoft.Extensions.Logging.ILogger<ChatSidebarViewModel> logger) 
        : base(logger)
    {
        SendMessageCommand = new RelayCommand(SendMessage, CanSendMessage);
        ClearChatCommand = new RelayCommand(ClearChat);
        ExportChatCommand = new RelayCommand(ExportChat);
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

    /// <summary>
    /// 清空聊天记录
    /// </summary>
    private async void ClearChat()
    {
        var result = await Shell.Current.DisplayAlert("确认", "确定要清空所有对话记录吗？", "确定", "取消");
        if (result)
        {
            ChatMessages.Clear();
        }
    }

    /// <summary>
    /// 导出聊天记录
    /// </summary>
    private async void ExportChat()
    {
        try
        {
            if (!ChatMessages.Any())
            {
                await Shell.Current.DisplayAlert("提示", "暂无对话记录可导出", "确定");
                return;
            }

            var chatContent = string.Join("\n\n", ChatMessages.Select(m => 
                $"[{m.FormattedTime}] {m.Sender}: {m.Content}"));

            var fileName = $"聊天记录_{StockCode}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            
            // 这里应该实现文件保存逻辑
            await Shell.Current.DisplayAlert("导出成功", $"聊天记录已保存为 {fileName}", "确定");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "导出聊天记录失败");
            await Shell.Current.DisplayAlert("错误", "导出失败，请稍后重试", "确定");
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

    #endregion

    #region 公共方法

    /// <summary>
    /// 更新分析上下文
    /// </summary>
    public void UpdateAnalysisContext(string stockCode, ObservableCollection<AnalysisMessage> analysisMessages)
    {
        StockCode = stockCode;
        AnalysisMessages = analysisMessages;
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
