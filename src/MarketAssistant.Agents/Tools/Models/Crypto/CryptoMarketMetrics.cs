using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币综合市值与供应量指标")]
public class CryptoMarketMetrics
{
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("当前美元价格")]
    public decimal CurrentPriceUsd { get; set; }

    [Description("总市值（USD）")]
    public decimal MarketCapUsd { get; set; }

    [Description("完全稀释估值 FDV（USD）")]
    public decimal? FullyDilutedValuationUsd { get; set; }

    [Description("当前代币流通量")]
    public decimal CirculatingSupply { get; set; }

    [Description("代币总供应量")]
    public decimal? TotalSupply { get; set; }

    [Description("代币最大供应量上限（null表示无上限）")]
    public decimal? MaxSupply { get; set; }

    [Description("24小时交易额（USD）")]
    public decimal Volume24hUsd { get; set; }

    [Description("交易量/市值比率")]
    public decimal VolumeToMarketCapRatio => MarketCapUsd > 0 ? Volume24hUsd / MarketCapUsd : 0;

    [Description("24小时价格涨跌幅（%）")]
    public decimal PriceChange24hPercent { get; set; }

    [Description("7天价格涨跌幅（%）")]
    public decimal? PriceChange7dPercent { get; set; }

    [Description("30天价格涨跌幅（%）")]
    public decimal? PriceChange30dPercent { get; set; }

    [Description("市值规模全网排名")]
    public int? MarketCapRank { get; set; }

    [Description("历史最高价 ATH（USD）")]
    public decimal? AllTimeHighUsd { get; set; }

    [Description("距离历史最高价的跌幅（%）")]
    public decimal? AthChangePercent { get; set; }

    [Description("历史最低价 ATL（USD）")]
    public decimal? AllTimeLowUsd { get; set; }

    [Description("距离历史最低价的涨幅（%）")]
    public decimal? AtlChangePercent { get; set; }

    [Description("代币流通比率（%）")]
    public decimal? CirculationRate => MaxSupply.HasValue && MaxSupply > 0
        ? CirculatingSupply / MaxSupply.Value * 100
        : null;

    [Description("数据最后更新时间")]
    public DateTime LastUpdated { get; set; }
}
