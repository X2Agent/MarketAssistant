using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 虚拟币项目基本面信息模型（基于 CoinDesk API）
/// </summary>
[Description("加密货币项目基本面及市场概览信息")]
public class CryptoProjectInfo
{
    /// <summary>
    /// 币种代码（如 BTC、ETH）
    /// </summary>
    [Description("代币符号（如 BTC）")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 项目名称（如 Bitcoin、Ethereum）
    /// </summary>
    [Description("项目名称")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// URL 标识符
    /// </summary>
    [Description("项目链接/详情URL")]
    public string Uri { get; set; } = string.Empty;

    /// <summary>
    /// 资产类型（如 BLOCKCHAIN）
    /// </summary>
    [Description("资产类型分类")]
    public string AssetType { get; set; } = string.Empty;

    /// <summary>
    /// 其他平台的 ID（CoinMarketCap、CoinGecko 等）
    /// </summary>
    [Description("其他多平台标识ID")]
    public Dictionary<string, string> AlternativeIds { get; set; } = new();

    /// <summary>
    /// 简短描述
    /// </summary>
    [Description("简短项目描述")]
    public string DescriptionSnippet { get; set; } = string.Empty;

    /// <summary>
    /// 详细描述（包含用途、技术等）
    /// </summary>
    [Description("详细项目介绍（包含用途、技术等）")]
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 安全审计信息（CertiK 等）
    /// </summary>
    [Description("安全审计指标（如CertiK）")]
    public SecurityMetrics? SecurityMetrics { get; set; }

    /// <summary>
    /// 最大供应量（-1 表示无上限）
    /// </summary>
    [Description("最大代币供应量")]
    public decimal? MaxSupply { get; set; }

    /// <summary>
    /// 总供应量
    /// </summary>
    [Description("总代币供应量")]
    public decimal? TotalSupply { get; set; }

    /// <summary>
    /// 流通供应量
    /// </summary>
    [Description("当前流通代币供应量")]
    public decimal? CirculatingSupply { get; set; }

    /// <summary>
    /// 当前价格（USD）
    /// </summary>
    [Description("当前价格（USD）")]
    public decimal? PriceUsd { get; set; }

    /// <summary>
    /// 总市值（USD）
    /// </summary>
    [Description("总市值（USD）")]
    public decimal? TotalMarketCapUsd { get; set; }

    /// <summary>
    /// 流通市值（USD）
    /// </summary>
    [Description("流通市值（USD）")]
    public decimal? CirculatingMarketCapUsd { get; set; }

    /// <summary>
    /// 24 小时现货交易额（USD）
    /// </summary>
    [Description("24小时现货交易额（USD）")]
    public decimal? Volume24hUsd { get; set; }

    /// <summary>
    /// 7 天现货交易额（USD）
    /// </summary>
    [Description("7天现货交易额（USD）")]
    public decimal? Volume7dUsd { get; set; }

    /// <summary>
    /// 30 天现货交易额（USD）
    /// </summary>
    [Description("30天现货交易额（USD）")]
    public decimal? Volume30dUsd { get; set; }

    /// <summary>
    /// 24 小时涨跌幅（%）
    /// </summary>
    [Description("24小时价格涨跌幅（%）")]
    public decimal? Change24hPercent { get; set; }

    /// <summary>
    /// 7 天涨跌幅（%）
    /// </summary>
    [Description("7天价格涨跌幅（%）")]
    public decimal? Change7dPercent { get; set; }

    /// <summary>
    /// 30 天涨跌幅（%）
    /// </summary>
    [Description("30天价格涨跌幅（%）")]
    public decimal? Change30dPercent { get; set; }

    /// <summary>
    /// 排名信息
    /// </summary>
    [Description("各项指标排名信息")]
    public RankingInfo? Rankings { get; set; }

    /// <summary>
    /// 所属行业分类
    /// </summary>
    [Description("行业板块/概念标签列表")]
    public List<string> Industries { get; set; } = new();
}

/// <summary>
/// 安全审计指标
/// </summary>
[Description("安全审计指标（如 CertiK）")]
public class SecurityMetrics
{
    /// <summary>
    /// CertiK 审计分数
    /// </summary>
    [Description("CertiK 审计得分")]
    public decimal? CertikScore { get; set; }

    /// <summary>
    /// CertiK 排名
    /// </summary>
    [Description("CertiK 安全排名")]
    public int? CertikRank { get; set; }
}

/// <summary>
/// 排名信息
/// </summary>
[Description("市场各项排名指标")]
public class RankingInfo
{
    /// <summary>
    /// 总市值排名
    /// </summary>
    [Description("市值规模排名")]
    public int? MarketCapRank { get; set; }

    /// <summary>
    /// 24 小时交易额排名
    /// </summary>
    [Description("24小时交易额排名")]
    public int? Volume24hRank { get; set; }

    /// <summary>
    /// 30 天交易额排名
    /// </summary>
    [Description("30天交易额排名")]
    public int? Volume30dRank { get; set; }
}