namespace MarketAssistant.ViewModels;

/// <summary>
/// 分析过程消息模型
/// </summary>
public class AnalysisMessage
{
    /// <summary>
    /// 消息发送者（分析师名称）
    /// </summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>
    /// 消息内容
    /// </summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>
    /// 消息时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>
    /// 格式化的时间字符串
    /// </summary>
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");

    public int OutputTokenCount { get; set; }

    public int InputTokenCount { get; set; }

    public int TotalTokenCount => OutputTokenCount + InputTokenCount;

    /// <summary>
    /// 是否包含Token使用信息
    /// </summary>
    public bool HasTokenUsage => TotalTokenCount > 0;

    /// <summary>
    /// 格式化的Token数量
    /// </summary>
    public string FormattedTokenCount => $"{TotalTokenCount:#,##0}";
}