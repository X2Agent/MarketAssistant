using System.ComponentModel;

namespace MarketAssistant.Agents.MarketAnalysis.Models;

#region 通用枚举

/// <summary>
/// 投资评级（通用）
/// 适用于：基本面评级、技术面评级、综合评级等
/// </summary>
public enum InvestmentRating
{
    [Description("强烈买入")]
    StrongBuy,

    [Description("买入")]
    Buy,

    [Description("持有")]
    Hold,

    [Description("减持")]
    Reduce,

    [Description("卖出")]
    Sell,

    [Description("强烈卖出")]
    StrongSell
}

public enum OperationRecommendation
{
    [Description("买入")]
    Buy,

    [Description("观望")]
    Hold,

    [Description("卖出")]
    Sell
}

public enum TrendChange
{
    [Description("上升")]
    Rising,

    [Description("下降")]
    Falling,

    [Description("持平")]
    Stable
}

public enum ImpactDirection
{
    [Description("正面")]
    Positive,

    [Description("中性")]
    Neutral,

    [Description("负面")]
    Negative
}

public enum Duration
{
    [Description("短期")]
    ShortTerm,

    [Description("中期")]
    MediumTerm,

    [Description("长期")]
    LongTerm
}

public enum Level
{
    [Description("低")]
    Low,

    [Description("中")]
    Medium,

    [Description("高")]
    High
}

#endregion

#region 基本面分析枚举

public enum IndustryLifecycle
{
    [Description("导入期")]
    Introduction,

    [Description("成长期")]
    Growth,

    [Description("成熟期")]
    Maturity,

    [Description("衰退期")]
    Decline
}

public enum MarketPosition
{
    [Description("龙头")]
    Leader,

    [Description("第二梯队")]
    SecondTier,

    [Description("跟随者")]
    Follower
}

public enum ProfitabilityTrend
{
    [Description("优")]
    Excellent,

    [Description("中")]
    Average,

    [Description("差")]
    Poor
}

public enum CashFlowStatus
{
    [Description("健康")]
    Healthy,

    [Description("一般")]
    Fair,

    [Description("紧张")]
    Tight
}

#endregion

#region 财务分析枚举

public enum DebtStructureAssessment
{
    [Description("健康")]
    Healthy,

    [Description("一般")]
    Fair,

    [Description("风险")]
    Risky
}

public enum FinancialStability
{
    [Description("强")]
    Strong,

    [Description("中")]
    Medium,

    [Description("弱")]
    Weak
}

public enum ProfitTrendAssessment
{
    [Description("稳健增长")]
    SteadyGrowth,

    [Description("波动")]
    Volatile,

    [Description("下滑")]
    Declining
}

public enum FreeCashFlowStatus
{
    [Description("正值")]
    Positive,

    [Description("负值")]
    Negative
}

public enum FreeCashFlowTrend
{
    [Description("改善")]
    Improving,

    [Description("恶化")]
    Deteriorating,

    [Description("稳定")]
    Stable
}

#endregion

#region 技术分析枚举

public enum TrendDirection
{
    [Description("上升趋势")]
    Uptrend,

    [Description("下降趋势")]
    Downtrend,

    [Description("震荡区间")]
    Sideways
}

public enum TimeFrame
{
    [Description("日线")]
    Daily,

    [Description("周线")]
    Weekly,

    [Description("月线")]
    Monthly
}

public enum BreakoutDirection
{
    [Description("向上突破")]
    UpwardBreakout,

    [Description("向下突破")]
    DownwardBreakout,

    [Description("维持震荡")]
    Consolidation
}

public enum VolumeStatus
{
    [Description("放量")]
    Expanding,

    [Description("缩量")]
    Contracting
}

public enum PriceVolumeRelationship
{
    [Description("健康")]
    Healthy,

    [Description("不健康")]
    Unhealthy
}

#endregion

#region 市场情绪分析枚举

public enum DominantEmotion
{
    [Description("恐惧")]
    Fear,

    [Description("贪婪")]
    Greed,

    [Description("中性")]
    Neutral
}

public enum MarketAtmosphere
{
    [Description("极度乐观")]
    ExtremelyOptimistic,

    [Description("乐观")]
    Optimistic,

    [Description("中性")]
    Neutral,

    [Description("悲观")]
    Pessimistic,

    [Description("极度悲观")]
    ExtremelyPessimistic
}

public enum CapitalFlowDirection
{
    [Description("净流入")]
    NetInflow,

    [Description("净流出")]
    NetOutflow,

    [Description("无明显变化")]
    NoSignificantChange
}

public enum InstitutionTrend
{
    [Description("加仓")]
    Increasing,

    [Description("减仓")]
    Decreasing,

    [Description("观望")]
    Watching
}

public enum BehaviorBias
{
    [Description("锚定效应")]
    Anchoring,

    [Description("从众心理")]
    HerdMentality,

    [Description("过度自信")]
    Overconfidence,

    [Description("损失厌恶")]
    LossAversion
}

public enum RetailInvestorCharacteristics
{
    [Description("追涨")]
    ChasingRally,

    [Description("杀跌")]
    PanicSelling,

    [Description("观望")]
    Watching
}

public enum BehaviorConsistency
{
    [Description("一致")]
    Consistent,

    [Description("分歧")]
    Divergent
}

public enum RiskPreference
{
    [Description("高风险偏好")]
    HighRisk,

    [Description("低风险偏好")]
    LowRisk
}

public enum MarketRhythm
{
    [Description("快速轮动")]
    FastRotation,

    [Description("缓慢轮动")]
    SlowRotation,

    [Description("单边行情")]
    OneSidedTrend
}

public enum PositionRecommendation
{
    [Description("激进")]
    Aggressive,

    [Description("稳健")]
    Moderate,

    [Description("保守")]
    Conservative
}

#endregion

#region 新闻事件分析枚举

public enum EventType
{
    [Description("公司公告")]
    CompanyAnnouncement,

    [Description("行业政策")]
    IndustryPolicy,

    [Description("市场消息")]
    MarketNews,

    [Description("突发事件")]
    BreakingEvent,

    [Description("业绩")]
    Earnings,

    [Description("公司治理")]
    CorporateGovernance,

    [Description("其他")]
    Other
}

public enum InformationSource
{
    [Description("官方")]
    Official,

    [Description("权威媒体")]
    AuthoritativeMedia,

    [Description("市场传闻")]
    MarketRumor
}

public enum EventNature
{
    [Description("重大利好")]
    MajorPositive,

    [Description("利好")]
    Positive,

    [Description("中性")]
    Neutral,

    [Description("利空")]
    Negative,

    [Description("重大利空")]
    MajorNegative
}

public enum ImpactScope
{
    [Description("公司特定")]
    CompanySpecific,

    [Description("行业性")]
    IndustryWide,

    [Description("市场性")]
    MarketWide
}

public enum MarketReactionExpectation
{
    [Description("过度反应")]
    Overreaction,

    [Description("理性反应")]
    RationalReaction,

    [Description("反应不足")]
    Underreaction
}

public enum PriceChangeExpectation
{
    [Description("上涨")]
    Rise,

    [Description("下跌")]
    Fall,

    [Description("震荡")]
    Fluctuation
}

public enum InvestmentImpactAssessment
{
    [Description("机遇")]
    Opportunity,

    [Description("风险")]
    Risk,

    [Description("中性")]
    Neutral
}

#endregion
