namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// 虚拟币筛选条件
/// </summary>
public class CryptoCriteria
{
    /// <summary>
    /// 筛选条件列表
    /// </summary>
    public List<CryptoScreeningCondition> Criteria { get; set; } = new();

    /// <summary>
    /// 返回结果数量限制
    /// </summary>
    public int Limit { get; set; } = 100;

    /// <summary>
    /// 交易所（如 Binance、Coinbase 等）
    /// </summary>
    public string? Exchange { get; set; }

    /// <summary>
    /// 交易对基准货币（如 USDT、BTC 等）
    /// </summary>
    public string? QuoteCurrency { get; set; } = "USDT";
}

/// <summary>
/// 虚拟币筛选条件项
/// </summary>
public class CryptoScreeningCondition
{
    /// <summary>
    /// 指标代码（如 market_cap、volume_24h、price_change_24h 等）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 最小值
    /// </summary>
    public decimal? MinValue { get; set; }

    /// <summary>
    /// 最大值
    /// </summary>
    public decimal? MaxValue { get; set; }
}

