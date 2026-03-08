namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 加密货币综合市场指标
/// </summary>
public class CryptoMarketMetrics
{
    /// <summary>
    /// 交易对符�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格（USD�?
    /// </summary>
    public decimal CurrentPriceUsd { get; set; }

    /// <summary>
    /// 市值（USD�?
    /// </summary>
    public decimal MarketCapUsd { get; set; }

    /// <summary>
    /// 完全稀释估值（USD�?
    /// </summary>
    public decimal? FullyDilutedValuationUsd { get; set; }

    /// <summary>
    /// 流通供应量
    /// </summary>
    public decimal CirculatingSupply { get; set; }

    /// <summary>
    /// 总供应量
    /// </summary>
    public decimal? TotalSupply { get; set; }

    /// <summary>
    /// 最大供应量（null表示无上限）
    /// </summary>
    public decimal? MaxSupply { get; set; }

    /// <summary>
    /// 24小时交易量（USD�?
    /// </summary>
    public decimal Volume24hUsd { get; set; }

    /// <summary>
    /// 交易�?市值比�?
    /// </summary>
    public decimal VolumeToMarketCapRatio => MarketCapUsd > 0 ? Volume24hUsd / MarketCapUsd : 0;

    /// <summary>
    /// 24小时价格变动�?�?
    /// </summary>
    public decimal PriceChange24hPercent { get; set; }

    /// <summary>
    /// 7天价格变动（%�?
    /// </summary>
    public decimal? PriceChange7dPercent { get; set; }

    /// <summary>
    /// 30天价格变动（%�?
    /// </summary>
    public decimal? PriceChange30dPercent { get; set; }

    /// <summary>
    /// 市值排�?
    /// </summary>
    public int? MarketCapRank { get; set; }

    /// <summary>
    /// 历史最高价（USD�?
    /// </summary>
    public decimal? AllTimeHighUsd { get; set; }

    /// <summary>
    /// 距离历史最高价跌幅�?�?
    /// </summary>
    public decimal? AthChangePercent { get; set; }

    /// <summary>
    /// 历史最低价（USD�?
    /// </summary>
    public decimal? AllTimeLowUsd { get; set; }

    /// <summary>
    /// 距离历史最低价涨幅�?�?
    /// </summary>
    public decimal? AtlChangePercent { get; set; }

    /// <summary>
    /// 流通率（流通供�?最大供应，%�?
    /// </summary>
    public decimal? CirculationRate => MaxSupply.HasValue && MaxSupply > 0 
        ? CirculatingSupply / MaxSupply.Value * 100 
        : null;

    /// <summary>
    /// 数据更新时间
    /// </summary>
    public DateTime LastUpdated { get; set; }
}
