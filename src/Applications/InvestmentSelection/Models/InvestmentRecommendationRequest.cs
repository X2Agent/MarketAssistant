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
    /// 投资金额
    /// </summary>
    public decimal? InvestmentAmount { get; set; }

    /// <summary>
    /// 风险偏好：conservative(保守), moderate(稳健), aggressive(激进)
    /// </summary>
    public string RiskPreference { get; set; } = "moderate";

    /// <summary>
    /// 投资期限（天）
    /// </summary>
    public int? InvestmentHorizon { get; set; }

    /// <summary>
    /// 偏好行业
    /// </summary>
    public List<string> PreferredSectors { get; set; } = new();

    /// <summary>
    /// 排除行业
    /// </summary>
    public List<string> ExcludedSectors { get; set; } = new();

    /// <summary>
    /// 最大推荐数量
    /// </summary>
    public int MaxRecommendations { get; set; } = 10;
}

