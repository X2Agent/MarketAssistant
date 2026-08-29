using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币项目基本面及市场概览信息")]
public class CryptoProjectInfo
{
    [Description("代币符号（如 BTC）")]
    public string Symbol { get; set; } = string.Empty;

    [Description("项目名称")]
    public string Name { get; set; } = string.Empty;

    [Description("项目链接/详情URL")]
    public string Uri { get; set; } = string.Empty;

    [Description("资产类型分类")]
    public string AssetType { get; set; } = string.Empty;

    [Description("其他多平台标识ID")]
    public Dictionary<string, string> AlternativeIds { get; set; } = new();

    [Description("简短项目描述")]
    public string DescriptionSnippet { get; set; } = string.Empty;

    [Description("详细项目介绍（包含用途、技术等）")]
    public string Description { get; set; } = string.Empty;

    [Description("安全审计指标（如CertiK）")]
    public SecurityMetrics? SecurityMetrics { get; set; }

    [Description("最大代币供应量")]
    public decimal? MaxSupply { get; set; }

    [Description("总代币供应量")]
    public decimal? TotalSupply { get; set; }

    [Description("当前流通代币供应量")]
    public decimal? CirculatingSupply { get; set; }

    [Description("当前价格（USD）")]
    public decimal? PriceUsd { get; set; }

    [Description("总市值（USD）")]
    public decimal? TotalMarketCapUsd { get; set; }

    [Description("流通市值（USD）")]
    public decimal? CirculatingMarketCapUsd { get; set; }

    [Description("24小时现货交易额（USD）")]
    public decimal? Volume24hUsd { get; set; }

    [Description("7天现货交易额（USD）")]
    public decimal? Volume7dUsd { get; set; }

    [Description("30天现货交易额（USD）")]
    public decimal? Volume30dUsd { get; set; }

    [Description("24小时价格涨跌幅（%）")]
    public decimal? Change24hPercent { get; set; }

    [Description("7天价格涨跌幅（%）")]
    public decimal? Change7dPercent { get; set; }

    [Description("30天价格涨跌幅（%）")]
    public decimal? Change30dPercent { get; set; }

    [Description("各项指标排名信息")]
    public RankingInfo? Rankings { get; set; }

    [Description("行业板块/概念标签列表")]
    public List<string> Industries { get; set; } = new();
}

[Description("安全审计指标（如 CertiK）")]
public class SecurityMetrics
{
    [Description("CertiK 审计得分")]
    public decimal? CertikScore { get; set; }

    [Description("CertiK 安全排名")]
    public int? CertikRank { get; set; }
}

[Description("市场各项排名指标")]
public class RankingInfo
{
    [Description("市值规模排名")]
    public int? MarketCapRank { get; set; }

    [Description("24小时交易额排名")]
    public int? Volume24hRank { get; set; }

    [Description("30天交易额排名")]
    public int? Volume30dRank { get; set; }
}
