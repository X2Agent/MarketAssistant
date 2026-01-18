using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// 资产筛选条件统一接口
/// </summary>
public interface IScreeningCriteria
{
    /// <summary>
    /// 返回结果数量限制
    /// </summary>
    int Limit { get; set; }

    /// <summary>
    /// 市场类型
    /// </summary>
    MarketType MarketType { get; }
}
