using CommunityToolkit.Mvvm.ComponentModel;
using MarketAssistant.Services;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 历史会话列表项：封装 <see cref="ChatSessionSummary"/> 的展示字段。
/// </summary>
public partial class ChatSessionSummaryItem : ObservableObject
{
    public ChatSessionSummaryItem(ChatSessionSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);

        Id = summary.Id;
        Title = summary.Title;
        StockCode = summary.StockCode;
        UpdatedAtText = summary.UpdatedAt.ToLocalTime().ToString("MM-dd HH:mm");
    }

    /// <summary>会话 ID（恢复/删除的目标键）。</summary>
    public string Id { get; }

    /// <summary>会话标题（由会话侧根据首条用户消息生成）。</summary>
    public string Title { get; }

    /// <summary>会话关联的标的代码。</summary>
    public string StockCode { get; }

    /// <summary>最近活跃时间的本地展示文本（持久化写入侧为 UTC 往返格式）。</summary>
    public string UpdatedAtText { get; }

    /// <summary>是否为当前已打开的会话（列表置顶高亮，删除时需特殊处理）。</summary>
    [ObservableProperty]
    private bool _isActive;
}
