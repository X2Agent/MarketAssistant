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
    /// 发送中（用户在思考）
    /// </summary>
    Sending,

    /// <summary>
    /// 正在接收流式输出
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
/// ChatMessage 的 MVVM 适配器，支持 UI 展示
/// </summary>
public partial class ChatMessageAdapter : ObservableObject
{
    private readonly AdaptiveCardConverter? _converter;

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
    /// 创建时间
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
    /// Adaptive Card 内容
    /// </summary>
    public AdaptiveCard? AdaptiveCard { get; private set; }

    /// <summary>
    /// 是否为 Adaptive Card
    /// </summary>
    public bool IsAdaptiveCard => AdaptiveCard != null;

    public ChatMessageAdapter(ChatMessage chatMessage, AdaptiveCardConverter? converter = null)
    {
        _converter = converter;
        Content = chatMessage.Text ?? string.Empty;
        IsUser = chatMessage.Role == ChatRole.User;
        Sender = chatMessage.AuthorName ?? (IsUser ? "用户" : "助手");
        Status = MessageStatus.Sent;
        Timestamp = chatMessage.CreatedAt ?? DateTimeOffset.Now;

        TryParseAdaptiveCard(Content);
    }

    partial void OnContentChanged(string value)
    {
        // 内容变化时（如流式完成）重新尝试解析 Adaptive Card
        if (AdaptiveCard == null && IsJsonContent(value))
        {
            TryParseAdaptiveCard(value);
        }
    }

    private void TryParseAdaptiveCard(string content)
    {
        if (_converter == null || string.IsNullOrWhiteSpace(content))
            return;

        if (!IsJsonContent(content))
            return;

        var card = _converter.Convert(content);
        if (card != null)
        {
            AdaptiveCard = card;
            OnPropertyChanged(nameof(IsAdaptiveCard));
        }
    }

    private static bool IsJsonContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return false;
        content = content.Trim();

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
}
