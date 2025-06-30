namespace MarketAssistant.Applications.Telegrams;

public class Telegram
{
    /// <summary>
    /// 新闻发布时间
    /// </summary>
    public string Time { get; set; } = "";

    /// <summary>
    /// 新闻标题
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 新闻内容
    /// </summary>
    public string Content { get; set; } = "";

    /// <summary>
    /// 新闻链接
    /// </summary>
    public string Url { get; set; } = "";

    /// <summary>
    /// 相关股票
    /// </summary>
    public List<string> Stocks { get; set; } = new();

    /// <summary>
    /// 是否为重要快讯
    /// </summary>
    public bool IsImportant { get; set; }
}
