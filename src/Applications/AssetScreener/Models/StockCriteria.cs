using System.ComponentModel;
using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// 股票筛选参数
/// </summary>
[Description("包含筛选条件、市场、行业和数量限制的股票筛选参数")]
public class StockCriteria : IScreeningCriteria
{
    /// <summary>
    /// 市场类型
    /// </summary>
    public MarketType MarketType => MarketType.AShare;
    /// <summary>
    /// 筛选条件列表
    /// </summary>
    [Description("股票筛选条件列表，每个条件包含指标代码、名称和范围")]
    public List<StockScreeningCriteria> Criteria { get; set; } = new();

    /// <summary>
    /// A股市场细分类型
    /// </summary>
    [Description("A股市场细分类型")]
    public AShareType Market { get; set; } = AShareType.AllAShares;

    /// <summary>
    /// 行业分类
    /// </summary>
    [Description("行业分类")]
    public IndustryType Industry { get; set; } = IndustryType.All;

    /// <summary>
    /// 返回数量限制
    /// </summary>
    [Description("推荐股票数量上限")]
    public int Limit { get; set; } = 20;
}

