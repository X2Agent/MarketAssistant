namespace MarketAssistant.Applications.StockSelection.Models;

/// <summary>
/// 热点新闻选股请求
/// </summary>
public class NewsBasedSelectionRequest
{
    /// <summary>
    /// 用户提供的新闻内容
    /// </summary>
    public string NewsContent { get; set; } = string.Empty;

    /// <summary>
    /// 最大推荐股票数量
    /// </summary>
    public int MaxRecommendations { get; set; } = 10;
}

