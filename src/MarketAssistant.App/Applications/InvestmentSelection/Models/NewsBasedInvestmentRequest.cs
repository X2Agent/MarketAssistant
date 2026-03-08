using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.InvestmentSelection.Models;

/// <summary>
/// 热点新闻投资选择请求
/// </summary>
public class NewsBasedInvestmentRequest
{
    /// <summary>
    /// 市场类型
    /// </summary>
    public MarketType MarketType { get; set; } = MarketType.AShare;

    /// <summary>
    /// 用户提供的新闻内容
    /// </summary>
    public string NewsContent { get; set; } = string.Empty;

    /// <summary>
    /// 最大推荐数量
    /// </summary>
    public int MaxRecommendations { get; set; } = 10;
}

