namespace MarketAssistant.Agents.Tools.Models.AShare;

/// <summary>
/// 市值与市场排名信息
/// </summary>
public class MarketCapInfo
{
    /// <summary>
    /// 代币符号
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 代币名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格（USD�?
    /// </summary>
    public decimal CurrentPrice { get; set; }

    /// <summary>
    /// 市值（USD�?
    /// </summary>
    public decimal MarketCap { get; set; }

    /// <summary>
    /// 完全稀释市值（USD�?
    /// </summary>
    public decimal? FullyDilutedValuation { get; set; }

    /// <summary>
    /// 市场排名
    /// </summary>
    public int MarketCapRank { get; set; }

    /// <summary>
    /// 24 小时交易量（USD�?
    /// </summary>
    public decimal Volume24h { get; set; }

    /// <summary>
    /// 24 小时价格变化�?�?
    /// </summary>
    public decimal? PriceChange24h { get; set; }

    /// <summary>
    /// 数据更新时间
    /// </summary>
    public string UpdatedAt { get; set; } = string.Empty;
}
