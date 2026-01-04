using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.Assets.Models;

/// <summary>
/// 热门资产
/// </summary>
public class HotAsset
{
    /// <summary>
    /// 资产名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 涨跌幅
    /// </summary>
    public string ChangePercentage { get; set; } = string.Empty;

    /// <summary>
    /// 资产代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格
    /// </summary>
    public string CurrentPrice { get; set; } = string.Empty;

    /// <summary>
    /// 市场标识
    /// </summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>
    /// 市场类型
    /// </summary>
    public MarketType MarketType { get; set; }

    /// <summary>
    /// 排名变化
    /// </summary>
    public string RankChange { get; set; } = string.Empty;

    /// <summary>
    /// 综合热度
    /// </summary>
    public string HeatIndex { get; set; } = string.Empty;

    // A股特有字段
    /// <summary>
    /// 所属板块名称（A股特有）
    /// </summary>
    public string? SectorName { get; set; }

    // 虚拟币特有字段
    /// <summary>
    /// 市值（虚拟币特有）
    /// </summary>
    public string? MarketCap { get; set; }

    /// <summary>
    /// 24小时交易量（虚拟币特有）
    /// </summary>
    public string? Volume24h { get; set; }
}






