using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 资产导航参数
/// </summary>
public class AssetNavigationParameter
{
    /// <summary>
    /// 资产代码（必填，非空）
    /// </summary>
    public string Code { get; set; }

    /// <summary>
    /// 资产名称（允许为空字符串，但不允许 null）
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// 当前价格（可选）- 用于立即显示基本信息
    /// </summary>
    public decimal? CurrentPrice { get; set; }

    /// <summary>
    /// 涨跌幅（可选）- 用于立即显示基本信息
    /// </summary>
    public decimal? ChangePercent { get; set; }

    /// <summary>
    /// 资产所属市场（可选）。未指定时，接收方回退到 MarketContext.CurrentMarket。
    /// 显式传入可避免导航期间切换市场导致的竞态。
    /// </summary>
    public MarketType? MarketType { get; set; }

    public AssetNavigationParameter(
        string code,
        string? name,
        decimal? currentPrice = null,
        decimal? changePercent = null,
        MarketType? marketType = null)
    {
        if (string.IsNullOrEmpty(code))
            throw new ArgumentException("资产代码不能为空", nameof(code));

        Code = code;
        Name = name ?? string.Empty;
        CurrentPrice = currentPrice;
        ChangePercent = changePercent;
        MarketType = marketType;
    }
}
