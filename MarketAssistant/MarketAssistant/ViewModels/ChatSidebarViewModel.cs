using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Agents;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Collections.ObjectModel;

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

        try
        {
            _currentCancellationTokenSource = new CancellationTokenSource();
            var contentBuilder = new System.Text.StringBuilder();
            bool hasReceivedContent = false;

            // 使用流式API
            await foreach (var chunk in _chatAgent.SendMessageStreamAsync(currentInput, _currentCancellationTokenSource.Token))
            {
                if (!string.IsNullOrEmpty(chunk.Content))
                {
                    contentBuilder.Append(chunk.Content);
                    
                    // 标记已接收到内容，切换状态
                    if (!hasReceivedContent)
                    {
                        hasReceivedContent = true;
                        // 第一次接收到内容时，切换状态为流式接收
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            aiMessage.Status = MessageStatus.Streaming; // 开始流式接收内容
                            aiMessage.Content = chunk.Content;
                        });
                    }
                    else
                    {
                        // 后续内容直接更新
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            aiMessage.Content = contentBuilder.ToString();
                        });
                    }
                }
            }

            // 确保最终状态为已发送
            MainThread.BeginInvokeOnMainThread(() =>
            {
                aiMessage.Status = MessageStatus.Sent;
            });
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


    #endregion
}
