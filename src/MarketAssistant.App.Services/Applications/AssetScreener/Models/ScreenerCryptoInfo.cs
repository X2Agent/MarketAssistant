namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// 虚拟币筛选结果（CoinGecko + Binance 数据源）
/// </summary>
public class ScreenerCryptoInfo : ScreenerAssetInfo
{
    /// <summary>
    /// 市值排名
    /// </summary>
    public int MarketCapRank { get; set; }

    /// <summary>
    /// 7天涨跌幅(%)
    /// </summary>
    public decimal PriceChange7d { get; set; }

    /// <summary>
    /// 30天涨跌幅(%)
    /// </summary>
    public decimal PriceChange30d { get; set; }

    /// <summary>
    /// 24h振幅(%)
    /// </summary>
    public decimal ChgPct { get; set; }

    /// <summary>
    /// 流通供应量
    /// </summary>
    public decimal CirculatingSupply { get; set; }

    /// <summary>
    /// 总供应量
    /// </summary>
    public decimal TotalSupply { get; set; }

    /// <summary>
    /// 最大供应量
    /// </summary>
    public decimal? MaxSupply { get; set; }
}
