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
    /// 输入Token数量
    /// </summary>
    public int InputTokenCount { get; set; }
    
    /// <summary>
    /// 输出Token数量
    /// </summary>
    public int OutputTokenCount { get; set; }
    
    /// <summary>
    /// Token总数
    /// </summary>
    public int TotalTokenCount { get; set; }
    
    /// <summary>
    /// 是否包含Token使用信息
    /// </summary>
    public bool HasTokenUsage => InputTokenCount > 0 || OutputTokenCount > 0 || TotalTokenCount > 0;
    
    /// <summary>
    /// 格式化的输入Token数量
    /// </summary>
    public string FormattedInputTokens => $"{InputTokenCount:#,##0}";
    
    /// <summary>
    /// 格式化的输出Token数量
    /// </summary>
    public string FormattedOutputTokens => $"{OutputTokenCount:#,##0}";
    
    /// <summary>
    /// 格式化的总Token数量
    /// </summary>
    public string FormattedTotalTokens => $"{TotalTokenCount:#,##0}";
}