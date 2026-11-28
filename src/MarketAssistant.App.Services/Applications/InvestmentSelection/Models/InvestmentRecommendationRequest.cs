using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.InvestmentSelection.Models;

/// <summary>
/// 投资推荐请求
/// </summary>
public class InvestmentRecommendationRequest
{
    /// <summary>
    /// 市场类型
    /// </summary>
    public MarketType MarketType { get; set; } = MarketType.AShare;

    /// <summary>
    /// 用户需求描述
    /// </summary>
    public string UserRequirements { get; set; } = string.Empty;

    /// <summary>
    /// 风险偏好：conservative(保守), moderate(稳健), aggressive(激进)
    /// </summary>
    public string RiskPreference { get; set; } = "moderate";

    /// <summary>
    /// 最大推荐数量
    /// </summary>
    public int MaxRecommendations { get; set; } = 10;
}
