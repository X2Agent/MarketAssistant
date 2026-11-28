using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// 资产筛选条件统一接口
/// </summary>
public interface IScreeningCriteria
{
    /// <summary>
    /// 候选池固定大小：筛选阶段统一取这么多只，由第 3 步从中挑选不超过 MaxRecommendations 只。
    /// 与推荐数量解耦，否则推荐数较小时（如新闻模式的 5）LLM 无从挑选。
    /// </summary>
    public const int CandidatePoolSize = 30;

    /// <summary>
    /// 候选池大小（由系统固定为 <see cref="CandidatePoolSize"/>，不取模型输出）
    /// </summary>
    int Limit { get; set; }

    /// <summary>
    /// 市场类型
    /// </summary>
    MarketType MarketType { get; }
}
