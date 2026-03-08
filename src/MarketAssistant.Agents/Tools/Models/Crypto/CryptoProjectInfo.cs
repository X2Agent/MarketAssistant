namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 虚拟币项目基本面信息模型（基�?CoinDesk API�?
/// </summary>
public class CryptoProjectInfo
{
    /// <summary>
    /// 币种代码（如 BTC、ETH�?
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 项目名称（如 Bitcoin、Ethereum�?
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL 标识�?
    /// </summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// 资产类型（如 BLOCKCHAIN�?
    /// </summary>
    public string AssetType { get; set; } = string.Empty;

    /// <summary>
    /// 其他平台�?ID（CoinMarketCap、CoinGecko 等）
    /// </summary>
    public Dictionary<string, string> AlternativeIds { get; set; } = new();

    /// <summary>
    /// 简短描�?
    /// </summary>
    public string DescriptionSnippet { get; set; } = string.Empty;

    /// <summary>
    /// 详细描述（包含用途、技术等�?
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 安全审计信息（CertiK 等）
    /// </summary>
    public SecurityMetrics? SecurityMetrics { get; set; }

    /// <summary>
    /// 最大供应量�?1 表示无上限）
    /// </summary>
    public decimal? MaxSupply { get; set; }

    /// <summary>
    /// 总供应量
    /// </summary>
    public decimal? TotalSupply { get; set; }

    /// <summary>
    /// 流通供应量
    /// </summary>
    public decimal? CirculatingSupply { get; set; }

    /// <summary>
    /// 当前价格（USD�?
    /// </summary>
    public decimal? PriceUsd { get; set; }

    /// <summary>
    /// 总市值（USD�?
    /// </summary>
    public decimal? TotalMarketCapUsd { get; set; }

    /// <summary>
    /// 流通市值（USD�?
    /// </summary>
    public decimal? CirculatingMarketCapUsd { get; set; }

    /// <summary>
    /// 24 小时现货交易额（USD�?
    /// </summary>
    public decimal? Volume24hUsd { get; set; }

    /// <summary>
    /// 7 天现货交易额（USD�?
    /// </summary>
    public decimal? Volume7dUsd { get; set; }

    /// <summary>
    /// 30 天现货交易额（USD�?
    /// </summary>
    public decimal? Volume30dUsd { get; set; }

    /// <summary>
    /// 24 小时涨跌幅（%�?
    /// </summary>
    public decimal? Change24hPercent { get; set; }

    /// <summary>
    /// 7 天涨跌幅�?�?
    /// </summary>
    public decimal? Change7dPercent { get; set; }

    /// <summary>
    /// 30 天涨跌幅�?�?
    /// </summary>
    public decimal? Change30dPercent { get; set; }

    /// <summary>
    /// 排名信息
    /// </summary>
    public RankingInfo? Rankings { get; set; }

    /// <summary>
    /// 所属行业分�?
    /// </summary>
    public List<string> Industries { get; set; } = new();
}

/// <summary>
/// 安全审计指标
/// </summary>
public class SecurityMetrics
{
    /// <summary>
    /// CertiK 审计分数
    /// </summary>
    public decimal? CertikScore { get; set; }

    /// <summary>
    /// CertiK 排名
    /// </summary>
    public int? CertikRank { get; set; }
}

/// <summary>
/// 排名信息
/// </summary>
public class RankingInfo
{
    /// <summary>
    /// 总市值排�?
    /// </summary>
    public int? MarketCapRank { get; set; }

    /// <summary>
    /// 24 小时交易额排�?
    /// </summary>
    public int? Volume24hRank { get; set; }

    /// <summary>
    /// 30 天交易额排名
    /// </summary>
    public int? Volume30dRank { get; set; }
}
