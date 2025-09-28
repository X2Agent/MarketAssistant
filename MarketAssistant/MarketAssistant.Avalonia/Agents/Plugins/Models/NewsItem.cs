using System.ComponentModel;

namespace MarketAssistant.Plugins.Models;

/// <summary>
/// 新闻上下文条目
/// </summary>
public class NewsItem
{
    [Description("新闻标题")]
    public string Title { get; set; } = "";

    [Description("新闻来源站点或频道名称")]
    public string Source { get; set; } = "";

    [Description("新闻详情页面链接")]
    public string Url { get; set; } = "";

    [Description("精简要点摘要（concise 模式可为空，detailed 模式提供）")]
    public string Summary { get; set; } = "";
}
