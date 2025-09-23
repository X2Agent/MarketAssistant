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
    
    /// <summary>
    /// 消息Token数量（如果是AI生成的消息，表示该消息的Token数量）
    /// </summary>
    public int TokenCount { get; set; }
    
    /// <summary>
    /// 是否包含Token使用信息
    /// </summary>
    public bool HasTokenUsage => TokenCount > 0;
    
    /// <summary>
    /// 格式化的Token数量
    /// </summary>
    public string FormattedTokenCount => $"{TokenCount:#,##0}";
}