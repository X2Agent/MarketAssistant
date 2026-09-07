using AdaptiveCards;
using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Agents.Analysts;
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

    /// <summary>
    /// 分析师结构化结果卡片（评级徽标 + 评分条 + 核心观点 + 风险）
    /// </summary>
    public AnalystCardViewModel? AnalystCard { get; private set; }

    /// <summary>
    /// 是否为分析师结果卡片
    /// </summary>
    public bool IsAnalystCard => AnalystCard != null;

    /// <summary>
    /// 是否显示富文本内容：分析师卡片、Adaptive Card、失败占位卡片均未命中时回退为富文本展示
    /// </summary>
    public bool ShowRichText => !IsAnalystCard && !IsAdaptiveCard && !IsAnalystFailure;

    /// <summary>
    /// 发送者显示名（ASCII Agent Name 翻译为中文显示名）
    /// </summary>
    public string DisplaySender => AnalystDisplayNameMapper.GetDisplayName(Sender);

    public ChatMessageAdapter(ChatMessage chatMessage, AdaptiveCardConverter? converter = null)
    {
        _converter = converter;
        Content = chatMessage.Text ?? string.Empty;
        IsUser = chatMessage.Role == ChatRole.User;
        Sender = chatMessage.AuthorName ?? (IsUser ? "用户" : "助手");
        Status = MessageStatus.Sent;

        // LLM 服务返回消息的 CreatedAt 通常为 UTC（Offset 为 0），直接格式化会把本地 22:02 显示成 14:02；
        // 统一归一化为本地时区后再展示
        var createdAt = chatMessage.CreatedAt;
        if (createdAt.HasValue && createdAt.Value.Offset == TimeSpan.Zero)
            createdAt = createdAt.Value.ToLocalTime();
        Timestamp = createdAt ?? DateTimeOffset.Now;

        TryParseAdaptiveCard(Content);
        TryParseAnalystCard(Content);
    }

    partial void OnContentChanged(string value)
    {
        // 流式期间每个 chunk 都会触发本回调，对不断增长的累积串做全量 JSON 解析是 O(n²)，
        // 且未完成的 JSON 必然解析失败——只在消息终态时解析一次
        if (Status is MessageStatus.Streaming or MessageStatus.Sending)
            return;

        if (AdaptiveCard == null && IsJsonContent(value))
        {
            TryParseAdaptiveCard(value);
        }

        if (AnalystCard == null)
        {
            TryParseAnalystCard(value);
        }
    }

    partial void OnStatusChanged(MessageStatus value)
    {
        // 流结束置为终态时，对最终内容统一解析一次 Adaptive Card / 分析师卡片
        if (value is not (MessageStatus.Sent or MessageStatus.Failed))
            return;

        if (AdaptiveCard == null && IsJsonContent(Content))
        {
            TryParseAdaptiveCard(Content);
        }

        if (AnalystCard == null)
        {
            TryParseAnalystCard(Content);
        }
    }

    /// <summary>
    /// 尝试把分析师 JSON 产物解析为结构化卡片。
    /// 仅对已知分析师作者名生效，且优先级低于 Adaptive Card；
    /// 解析失败时保持 null，由 UI 回退为 RichTextBlock 展示。
    /// </summary>
    private void TryParseAnalystCard(string content)
    {
        if (IsUser || IsAdaptiveCard || IsAnalystFailure)
            return;

        // 已知分析师的 ASCII 名才尝试结构化解析，避免把普通助手回复误判为卡片
        if (!AnalystDisplayNameMapper.IsKnownAnalystName(Sender))
            return;

        if (AnalystCardViewModel.TryCreate(Sender, content, out var card))
        {
            AnalystCard = card;
            OnPropertyChanged(nameof(AnalystCard));
            OnPropertyChanged(nameof(IsAnalystCard));
            OnPropertyChanged(nameof(ShowRichText));
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
            OnPropertyChanged(nameof(AdaptiveCard));
            OnPropertyChanged(nameof(IsAdaptiveCard));
            OnPropertyChanged(nameof(ShowRichText));
        }
    }

    /// <summary>
    /// 是否为分析师失败标记消息（供侧边栏渲染灰色失败占位卡片）
    /// </summary>
    public bool IsAnalystFailure => AnalystFailureMessages.IsFailureMarker(Content);

    /// <summary>
    /// 失败占位卡片展示文本：「分析师显示名 本次执行失败：原因」
    /// </summary>
    public string FailureNoticeText
    {
        get
        {
            var agentName = AnalystFailureMessages.ExtractAgentName(Content);
            var displayName = string.IsNullOrEmpty(agentName)
                ? DisplaySender
                : AnalystDisplayNameMapper.GetDisplayName(agentName);
            return $"{displayName} 本次执行失败：{AnalystFailureMessages.ExtractFailureReason(Content)}";
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
