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
    /// 所属板块或分类（A股为板块名称，加密货币暂不使用）
    /// </summary>
    public string? SectorName { get; set; }

    /// <summary>
    /// 市值（仅加密货币使用）
    /// </summary>
    public string? MarketCap { get; set; }

    /// <summary>
    /// 核心指标值，由各市场服务格式化后直接展示（A股为主力净流入，如 1.23亿；加密货币为24小时成交额，如 2.41B）
    /// </summary>
    public string? MetricValue { get; set; }

    /// <summary>
    /// 指标标签（根据市场类型动态返回）
    /// </summary>
    public string MetricLabel { get; set; } = "热度";
}






