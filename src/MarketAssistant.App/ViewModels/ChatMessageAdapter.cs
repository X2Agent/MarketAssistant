using AdaptiveCards;
using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Agents.Analysts;
using MarketAssistant.Views.Controls;
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
    [NotifyPropertyChangedFor(nameof(IsSystemNotice))]
    [NotifyPropertyChangedFor(nameof(ShowAssistantBubble))]
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
    [NotifyPropertyChangedFor(nameof(DisplaySender))]
    [NotifyPropertyChangedFor(nameof(IsSystemNotice))]
    [NotifyPropertyChangedFor(nameof(ShowAssistantBubble))]
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
    /// 是否显示富文本内容：Adaptive Card、失败占位卡片均未命中时回退为富文本展示
    /// </summary>
    public bool ShowRichText => !IsAdaptiveCard && !IsAnalystFailure;

    /// <summary>
    /// 是否用纯文本渲染器（可在流式期间做模糊揭示）：
    /// 流式期间一律用纯文本揭示——对不断增长的串反复跑 Markdown 正则是 O(n²)，
    /// 且流到一半时检测到 Markdown 会让渲染器中途切换、气泡闪烁；
    /// 终态时按内容是否含 Markdown 决定走纯文本还是富文本。
    /// </summary>
    public bool ShowPlainText
        => ShowRichText
           && (Status is MessageStatus.Streaming or MessageStatus.Sending
               || ContentFormatDetector.IsPlainText(Content));

    /// <summary>
    /// 是否可用富文本渲染器（兜底）：走富文本展示但不适合逐字揭示的内容。
    /// </summary>
    public bool ShowRichTextFallback => ShowRichText && !ShowPlainText;

    /// <summary>
    /// 该消息是否可复制：流式进行中内容仍会变化，此时不提供复制入口。
    /// 仅在终态且内容非空时允许复制——结构化卡片同样可复制其原始产物，便于外部留档；
    /// 分析师失败占位只是内部标记，复制出去没有意义，不提供入口。
    /// </summary>
    public bool CanCopy
        => !IsUser
           && Status is MessageStatus.Sent or MessageStatus.Failed
           && !string.IsNullOrWhiteSpace(Content)
           && !IsAnalystFailure;

    /// <summary>
    /// 复制反馈触发令牌：每次复制成功递增一次，
    /// 气泡内的 CopyFeedback 监听该值变化并播放对勾（2.5s 后自动消失）。
    /// </summary>
    [ObservableProperty]
    private int _copyFeedbackTrigger;

    /// <summary>
    /// 发送者显示名（ASCII Agent Name 翻译为中文显示名）
    /// </summary>
    public string DisplaySender => AnalystDisplayNameMapper.GetDisplayName(Sender);

    /// <summary>
    /// 系统提示消息（如"以上是关于 xxx 的分析数据"）。
    /// 它承载的是对话边界而不是一轮发言，渲染为居中的分隔行而非气泡。
    /// </summary>
    public bool IsSystemNotice => !IsUser && string.Equals(Sender, "系统", StringComparison.Ordinal);

    /// <summary>AI 消息且不是系统提示——决定左侧头像 + 气泡布局是否出现</summary>
    public bool ShowAssistantBubble => !IsUser && !IsSystemNotice;

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
    }

    partial void OnContentChanged(string value)
    {
        // Content 是流式期间最高频变化的属性，派生的渲染分支必须同步失效，
        // 否则消息从"纯文本"变为"含 Markdown"、或从流式转为终态时 UI 不切换渲染器
        OnPropertyChanged(nameof(ShowPlainText));
        OnPropertyChanged(nameof(ShowRichTextFallback));
        OnPropertyChanged(nameof(CanCopy));

        // 流式期间每个 chunk 都会触发本回调，对不断增长的累积串做全量 JSON 解析是 O(n²)，
        // 且未完成的 JSON 必然解析失败——只在消息终态时解析一次
        if (Status is MessageStatus.Streaming or MessageStatus.Sending)
            return;

        if (AdaptiveCard == null && IsJsonContent(value))
        {
            TryParseAdaptiveCard(value);
        }
    }

    partial void OnStatusChanged(MessageStatus value)
    {
        // 终态决定能否复制、以及用哪个渲染器收尾
        OnPropertyChanged(nameof(CanCopy));
        OnPropertyChanged(nameof(ShowRichText));
        OnPropertyChanged(nameof(ShowPlainText));
        OnPropertyChanged(nameof(ShowRichTextFallback));

        // 流结束置为终态时，对最终内容统一解析一次 Adaptive Card
        if (value is not (MessageStatus.Sent or MessageStatus.Failed))
            return;

        if (AdaptiveCard == null && IsJsonContent(Content))
        {
            TryParseAdaptiveCard(Content);
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
            OnPropertyChanged(nameof(ShowPlainText));
            OnPropertyChanged(nameof(ShowRichTextFallback));
            OnPropertyChanged(nameof(CanCopy));
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

        if (!((content.StartsWith('{') && content.EndsWith('}')) ||
              (content.StartsWith('[') && content.EndsWith(']'))))
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
