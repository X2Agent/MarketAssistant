using AdaptiveCards;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.AI;
using AdaptiveCardConverter = MarketAssistant.Infrastructure.AdaptiveCards.AdaptiveCardConverter;

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
/// ChatMessage的MVVM适配器，支持UI绑定
/// </summary>
public partial class ChatMessageAdapter : ObservableObject
{
    private static readonly AdaptiveCardConverter _converter = new();

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
    [NotifyPropertyChangedFor(nameof(IsThinking))]
    private MessageStatus _status = MessageStatus.Sent;

    /// <summary>
    /// 是否正在思考（发送中）
    /// </summary>
    public bool IsThinking => Status == MessageStatus.Sending;

    /// <summary>
    /// Adaptive Card 对象
    /// </summary>
    public AdaptiveCard? AdaptiveCard { get; private set; }

    /// <summary>
    /// 是否为 Adaptive Card
    /// </summary>
    public bool IsAdaptiveCard => AdaptiveCard != null;

    partial void OnContentChanged(string value)
    {
        // Re-evaluate Adaptive Card if content changes (e.g. streaming complete)
        if (AdaptiveCard == null && IsJsonContent(value))
        {
            var legacyCard = _converter.Convert(value);
            if (legacyCard != null)
            {
                AdaptiveCard = legacyCard;
                OnPropertyChanged(nameof(IsAdaptiveCard));
            }
        }
    }

    private bool IsJsonContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        content = content.Trim();

        // Fast check for brackets
        if (!((content.StartsWith("{") && content.EndsWith("}")) ||
              (content.StartsWith("[") && content.EndsWith("]"))))
        {
            return false;
        }

        try
        {
            JsonDocument.Parse(content);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// 从 Microsoft.Extensions.AI.ChatMessage 创建适配器
    /// </summary>
    public ChatMessageAdapter(ChatMessage chatMessage)
    {
        Content = chatMessage.Text ?? string.Empty;
        IsUser = chatMessage.Role == ChatRole.User;
        Sender = chatMessage.AuthorName ?? (IsUser ? "用户" : "助手");
        Status = MessageStatus.Sent;
        Timestamp = chatMessage.CreatedAt ?? DateTimeOffset.Now;

        // Fallback: Try converting legacy analysis JSON to card
        if (AdaptiveCard == null && IsJsonContent(Content))
        {
            AdaptiveCard = _converter.Convert(Content);
        }
    }
}

