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

/// <summary>
/// 操作建议
/// </summary>
public enum OperationRecommendation
{
    [Description("买入")]
    Buy,
    
    [Description("观望")]
    Hold,
    
    [Description("卖出")]
    Sell
}

/// <summary>
/// 趋势变化
/// </summary>
public enum TrendChange
{
    [Description("上升")]
    Rising,
    
    [Description("下降")]
    Falling,
    
    [Description("持平")]
    Stable
}

/// <summary>
/// 影响方向
/// </summary>
public enum ImpactDirection
{
    [Description("正面")]
    Positive,
    
    [Description("中性")]
    Neutral,
    
    [Description("负面")]
    Negative
}

/// <summary>
/// 持续时长
/// </summary>
public enum Duration
{
    [Description("短期")]
    ShortTerm,
    
    [Description("中期")]
    MediumTerm,
    
    [Description("长期")]
    LongTerm
}

/// <summary>
/// 水平等级（高中低）
/// </summary>
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

/// <summary>
/// 行业生命周期
/// </summary>
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

/// <summary>
/// 市场地位
/// </summary>
public enum MarketPosition
{
    [Description("龙头")]
    Leader,
    
    [Description("第二梯队")]
    SecondTier,
    
    [Description("跟随者")]
    Follower
}

/// <summary>
/// 盈利趋势
/// </summary>
public enum ProfitabilityTrend
{
    [Description("优")]
    Excellent,
    
    [Description("中")]
    Average,
    
    [Description("差")]
    Poor
}

/// <summary>
/// 现金流状况
/// </summary>
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

/// <summary>
/// 债务结构评估
/// </summary>
public enum DebtStructureAssessment
{
    [Description("健康")]
    Healthy,
    
    [Description("一般")]
    Fair,
    
    [Description("风险")]
    Risky
}

/// <summary>
/// 财务稳健性
/// </summary>
public enum FinancialStability
{
    [Description("强")]
    Strong,
    
    [Description("中")]
    Medium,
    
    [Description("弱")]
    Weak
}

/// <summary>
/// 盈利趋势评估（财务分析）
/// </summary>
public enum ProfitTrendAssessment
{
    [Description("稳健增长")]
    SteadyGrowth,
    
    [Description("波动")]
    Volatile,
    
    [Description("下滑")]
    Declining
}


/// <summary>
/// 自由现金流状态
/// </summary>
public enum FreeCashFlowStatus
{
    [Description("正值")]
    Positive,
    
    [Description("负值")]
    Negative
}

/// <summary>
/// 自由现金流趋势
/// </summary>
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

/// <summary>
/// 趋势方向
/// </summary>
public enum TrendDirection
{
    [Description("上升趋势")]
    Uptrend,
    
    [Description("下降趋势")]
    Downtrend,
    
    [Description("震荡区间")]
    Sideways
}

/// <summary>
/// 时间框架
/// </summary>
public enum TimeFrame
{
    [Description("日线")]
    Daily,
    
    [Description("周线")]
    Weekly,
    
    [Description("月线")]
    Monthly
}

/// <summary>
/// 突破方向
/// </summary>
public enum BreakoutDirection
{
    [Description("向上突破")]
    UpwardBreakout,
    
    [Description("向下突破")]
    DownwardBreakout,
    
    [Description("维持震荡")]
    Consolidation
}

/// <summary>
/// 成交量状态
/// </summary>
public enum VolumeStatus
{
    [Description("放量")]
    Expanding,
    
    [Description("缩量")]
    Contracting
}

/// <summary>
/// 量价关系
/// </summary>
public enum PriceVolumeRelationship
{
    [Description("健康")]
    Healthy,
    
    [Description("不健康")]
    Unhealthy
}



#endregion

#region 市场情绪分析枚举

/// <summary>
/// 主导情绪
/// </summary>
public enum DominantEmotion
{
    [Description("恐惧")]
    Fear,
    
    [Description("贪婪")]
    Greed,
    
    [Description("中性")]
    Neutral
}


/// <summary>
/// 市场氛围
/// </summary>
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

/// <summary>
/// 资金流向
/// </summary>
public enum CapitalFlowDirection
{
    [Description("净流入")]
    NetInflow,
    
    [Description("净流出")]
    NetOutflow,
    
    [Description("无明显变化")]
    NoSignificantChange
}

/// <summary>
/// 机构动向
/// </summary>
public enum InstitutionTrend
{
    [Description("加仓")]
    Increasing,
    
    [Description("减仓")]
    Decreasing,
    
    [Description("观望")]
    Watching
}

/// <summary>
/// 行为偏差类型
/// </summary>
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

/// <summary>
/// 散户特征
/// </summary>
public enum RetailInvestorCharacteristics
{
    [Description("追涨")]
    ChasingRally,
    
    [Description("杀跌")]
    PanicSelling,
    
    [Description("观望")]
    Watching
}

/// <summary>
/// 行为一致性
/// </summary>
public enum BehaviorConsistency
{
    [Description("一致")]
    Consistent,
    
    [Description("分歧")]
    Divergent
}

/// <summary>
/// 风险偏好
/// </summary>
public enum RiskPreference
{
    [Description("高风险偏好")]
    HighRisk,
    
    [Description("低风险偏好")]
    LowRisk
}

/// <summary>
/// 市场节奏
/// </summary>
public enum MarketRhythm
{
    [Description("快速轮动")]
    FastRotation,
    
    [Description("缓慢轮动")]
    SlowRotation,
    
    [Description("单边行情")]
    OneSidedTrend
}

/// <summary>
/// 仓位建议
/// </summary>
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

/// <summary>
/// 事件类型
/// </summary>
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

/// <summary>
/// 信息来源
/// </summary>
public enum InformationSource
{
    [Description("官方")]
    Official,
    
    [Description("权威媒体")]
    AuthoritativeMedia,
    
    [Description("市场传闻")]
    MarketRumor
}

/// <summary>
/// 事件性质
/// </summary>
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

/// <summary>
/// 影响范围
/// </summary>
public enum ImpactScope
{
    [Description("公司特定")]
    CompanySpecific,
    
    [Description("行业性")]
    IndustryWide,
    
    [Description("市场性")]
    MarketWide
}

/// <summary>
/// 市场反应预期
/// </summary>
public enum MarketReactionExpectation
{
    [Description("过度反应")]
    Overreaction,
    
    [Description("理性反应")]
    RationalReaction,
    
    [Description("反应不足")]
    Underreaction
}

/// <summary>
/// 价格变化预期
/// </summary>
public enum PriceChangeExpectation
{
    [Description("上涨")]
    Rise,
    
    [Description("下跌")]
    Fall,
    
    [Description("震荡")]
    Fluctuation
}

/// <summary>
/// 投资影响评估
/// </summary>
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

