using System.ComponentModel;

namespace MarketAssistant.Plugins.Models;

public class NewsItem
{
    [Description("新闻详情Url")]
    public string Url { get; set; } = "";

    [Description("新闻标题")]
    public string Title { get; set; } = "";

    [Description("新闻来源")]
    public string Source { get; set; } = "";
}
