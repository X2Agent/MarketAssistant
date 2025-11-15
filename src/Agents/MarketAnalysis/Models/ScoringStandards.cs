namespace MarketAssistant.Agents.MarketAnalysis.Models;

/// <summary>
/// 统一的评分标准定义（1-10分制）
/// 所有评分标准在此集中维护，确保一致性
/// </summary>
public static class ScoringStandards
{
    /// <summary>
    /// 质量/能力类评分标准
    /// 适用于：业务质量、财务稳健性、利润质量、现金流质量等
    /// </summary>
    public const string Quality = "1-2极差 3-4较差 5-6一般 7-8良好 9-10优秀";

    /// <summary>
    /// 强度类评分标准
    /// 适用于：偿债能力、竞争力强度、增长持续性、支撑/阻力强度等
    /// </summary>
    public const string Strength = "1-2很弱 3-4较弱 5-6中等 7-8较强 9-10很强";

    /// <summary>
    /// 可靠性/确信度/概率/程度类评分标准
    /// 适用于：确信度、可靠性、概率、重要性、影响程度、活跃度等
    /// </summary>
    public const string Reliability = "1-2很低 3-4较低 5-6中等 7-8较高 9-10很高";

    /// <summary>
    /// 情绪/氛围强度类评分标准
    /// 适用于：情绪强度、氛围强度等
    /// </summary>
    public const string EmotionIntensity = "1-2微弱 3-4较弱 5-6中等 7-8强烈 9-10极端";

    /// <summary>
    /// 趋势强度类评分标准
    /// 适用于：趋势强度评分
    /// </summary>
    public const string TrendStrength = "1-2极弱 3-4弱 5-6中等 7-8强 9-10极强";

    /// <summary>
    /// 风险类评分标准（分数越高风险越高）
    /// 适用于：财务造假风险等
    /// </summary>
    public const string Risk = "1-2低风险 3-4较低 5-6中等 7-8较高 9-10高风险";

    /// <summary>
    /// 信息可信度评分标准（基于来源分级）
    /// </summary>
    public const string Credibility = "官方来源9-10分，权威媒体7-8分，市场传闻1-4分";
}

