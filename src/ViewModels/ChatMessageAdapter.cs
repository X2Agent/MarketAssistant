using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 消息状态枚举
/// </summary>
public enum MessageStatus
{
    /// <summary>
    /// 发送中（正在思考）
    /// </summary>
    Sending,

    /// <summary>
    /// 正在接收流式内容
    /// </summary>
    Streaming,

    /// <summary>
    /// 已发送
    /// </summary>
    Sent,

    /// <summary>
    /// 发送失败
    /// </summary>
    Failed
}

/// <summary>
/// ChatMessageContent的MVVM适配器，支持UI绑定
/// </summary>
public partial class ChatMessageAdapter : ObservableObject
{
    private readonly ChatMessageContent? _originalMessage;

    /// <summary>
    /// 消息唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 消息内容
    /// </summary>
    [ObservableProperty]
    private string _content = string.Empty;

    /// <summary>
    /// 是否为用户消息
    /// </summary>
    [ObservableProperty]
    private bool _isUser;

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 格式化的时间字符串
    /// </summary>
    public string FormattedTime => Timestamp.ToString("HH:mm");

    /// <summary>
    /// 发送者名称
    /// </summary>
    [ObservableProperty]
    private string _sender = string.Empty;

    /// <summary>
    /// 消息状态
    /// </summary>
    [ObservableProperty]
    private MessageStatus _status = MessageStatus.Sent;

    /// <summary>
    /// 原始的SK ChatMessageContent对象
    /// </summary>
    public ChatMessageContent? OriginalMessage => _originalMessage;

    /// <summary>
    /// 从ChatMessageContent创建适配器
    /// </summary>
    public ChatMessageAdapter(ChatMessageContent messageContent)
    {
        _originalMessage = messageContent;
        Content = messageContent.Content ?? string.Empty;
        IsUser = messageContent.Role == AuthorRole.User;
        Sender = messageContent.AuthorName ?? GetDefaultSenderName(messageContent.Role);
        Status = MessageStatus.Sent;
    }

    /// <summary>
    /// 创建用户消息适配器
    /// </summary>
    public ChatMessageAdapter(string content, bool isUser, string? sender = null)
    {
        Content = content;
        IsUser = isUser;
        Sender = sender ?? (isUser ? "用户" : "助手");
        Status = MessageStatus.Sent;
    }

    /// <summary>
    /// 从 Microsoft.Extensions.AI.ChatMessage 创建适配器
    /// </summary>
    public ChatMessageAdapter(Microsoft.Extensions.AI.ChatMessage chatMessage)
    {
        Content = chatMessage.Text ?? string.Empty;
        IsUser = chatMessage.Role == Microsoft.Extensions.AI.ChatRole.User;
        Sender = chatMessage.AuthorName ?? (IsUser ? "用户" : "助手");
        Status = MessageStatus.Sent;
        Timestamp = chatMessage.CreatedAt ?? DateTimeOffset.Now;
    }

    /// <summary>
    /// 创建系统消息适配器
    /// </summary>
    public static ChatMessageAdapter CreateSystemMessage(string content, string? sender = null)
    {
        return new ChatMessageAdapter(content, false, sender ?? "系统");
    }

    /// <summary>
    /// 根据角色获取默认发送者名称
    /// </summary>
    private static string GetDefaultSenderName(AuthorRole role)
    {
        if (role == AuthorRole.User) return "用户";
        if (role == AuthorRole.Assistant) return "助手";
        if (role == AuthorRole.System) return "系统";
        if (role == AuthorRole.Tool) return "工具";
        return "未知";
    }
}

