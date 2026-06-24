using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 加密货币综合市场指标
/// </summary>
[Description("加密货币综合市值与供应量指标")]
public class CryptoMarketMetrics
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格（USD）
    /// </summary>
    [Description("当前美元价格")]
    public decimal CurrentPriceUsd { get; set; }

    /// <summary>
    /// 市值（USD）
    /// </summary>
    [Description("总市值（USD）")]
    public decimal MarketCapUsd { get; set; }

    /// <summary>
    /// 完全稀释估值（USD）
    /// </summary>
    [Description("完全稀释估值 FDV（USD）")]
    public decimal? FullyDilutedValuationUsd { get; set; }

    /// <summary>
    /// 流通供应量
    /// </summary>
    [Description("当前代币流通量")]
    public decimal CirculatingSupply { get; set; }

    /// <summary>
    /// 总供应量
    /// </summary>
    [Description("代币总供应量")]
    public decimal? TotalSupply { get; set; }

    /// <summary>
    /// 最大供应量（null表示无上限）
    /// </summary>
    [Description("代币最大供应量上限（null表示无上限）")]
    public decimal? MaxSupply { get; set; }

    /// <summary>
    /// 24小时交易量（USD）
    /// </summary>
    [Description("24小时交易额（USD）")]
    public decimal Volume24hUsd { get; set; }

    /// <summary>
    /// 交易量/市值比率
    /// </summary>
    [Description("交易量/市值比率")]
    public decimal VolumeToMarketCapRatio => MarketCapUsd > 0 ? Volume24hUsd / MarketCapUsd : 0;

    /// <summary>
    /// 24小时价格变动（%）
    /// </summary>
    [Description("24小时价格涨跌幅（%）")]
    public decimal PriceChange24hPercent { get; set; }

    /// <summary>
    /// 7天价格变动（%）
    /// </summary>
    [Description("7天价格涨跌幅（%）")]
    public decimal? PriceChange7dPercent { get; set; }

    /// <summary>
    /// 30天价格变动（%）
    /// </summary>
    [Description("30天价格涨跌幅（%）")]
    public decimal? PriceChange30dPercent { get; set; }

    /// <summary>
    /// 市值排名
    /// </summary>
    [Description("市值规模全网排名")]
    public int? MarketCapRank { get; set; }

    /// <summary>
    /// 历史最高价（USD）
    /// </summary>
    [Description("历史最高价 ATH（USD）")]
    public decimal? AllTimeHighUsd { get; set; }

    /// <summary>
    /// 距离历史最高价跌幅（%）
    /// </summary>
    [Description("距离历史最高价的跌幅（%）")]
    public decimal? AthChangePercent { get; set; }

    /// <summary>
    /// 历史最低价（USD）
    /// </summary>
    [Description("历史最低价 ATL（USD）")]
    public decimal? AllTimeLowUsd { get; set; }

    /// <summary>
    /// 距离历史最低价涨幅（%）
    /// </summary>
    [Description("距离历史最低价的涨幅（%）")]
    public decimal? AtlChangePercent { get; set; }

    /// <summary>
    /// 流通率（流通量/最大供应，%）
    /// </summary>
    [Description("代币流通比率（%）")]
    public decimal? CirculationRate => MaxSupply.HasValue && MaxSupply > 0
        ? CirculatingSupply / MaxSupply.Value * 100
        : null;

    /// <summary>
    /// 数据更新时间
    /// </summary>
    [Description("数据最后更新时间")]
    public DateTime LastUpdated { get; set; }
}