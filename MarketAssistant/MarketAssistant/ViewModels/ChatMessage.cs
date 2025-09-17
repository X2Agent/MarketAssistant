using CommunityToolkit.Mvvm.ComponentModel;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 对话消息模型
/// </summary>
public partial class ChatMessage : ObservableObject
{
    /// <summary>
    /// 消息唯一标识
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();
    
    /// <summary>
    /// 消息内容
    /// </summary>
    [ObservableProperty]
    private string content = string.Empty;
    
    /// <summary>
    /// 是否为用户消息
    /// </summary>
    [ObservableProperty]
    private bool isUser;
    
    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 格式化的时间字符串
    /// </summary>
    public string FormattedTime => Timestamp.ToString("HH:mm");
    
    /// <summary>
    /// 发送者名称
    /// </summary>
    [ObservableProperty]
    private string sender = string.Empty;
    
    /// <summary>
    /// 消息状态（发送中、已发送、失败等）
    /// </summary>
    [ObservableProperty]
    private MessageStatus status = MessageStatus.Sent;
}

/// <summary>
/// 消息状态枚举
/// </summary>
public enum MessageStatus
{
    /// <summary>
    /// 发送中
    /// </summary>
    Sending,
    
    /// <summary>
    /// 已发送
    /// </summary>
    Sent,
    
    /// <summary>
    /// 发送失败
    /// </summary>
    Failed
}

