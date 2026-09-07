using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 链上工具返回模型：DEX 热门交易对、代币安全审计、蜜罐检测与大额转账信号。
/// </summary>

[Description("DEX 热门交易对信息")]
public class HotTokenPair
{
    [Description("链 ID（如 eth、bsc、solana）")]
    public string ChainId { get; set; } = string.Empty;

    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("代币全名")]
    public string TokenName { get; set; } = string.Empty;

    [Description("代币合约地址")]
    public string TokenAddress { get; set; } = string.Empty;

    [Description("当前价格（USD）")]
    public decimal? PriceUsd { get; set; }

    [Description("24 小时涨跌幅（%）")]
    public decimal? Change24hPercent { get; set; }

    [Description("24 小时成交额（USD）")]
    public decimal? Volume24hUsd { get; set; }

    [Description("流动性池规模（USD）")]
    public decimal? LiquidityUsd { get; set; }

    [Description("市值（USD）")]
    public decimal? MarketCapUsd { get; set; }

    [Description("交易对创建时间")]
    public DateTime? PairCreatedAt { get; set; }

    [Description("所属交易所/DEX")]
    public string DexId { get; set; } = string.Empty;
}

[Description("代币安全审计结果")]
public class TokenSecurityReport
{
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("代币合约地址")]
    public string TokenAddress { get; set; } = string.Empty;

    [Description("所在链 ID")]
    public string ChainId { get; set; } = string.Empty;

    [Description("综合风险等级：safe（安全）/ warning（警告）/ danger（高危）")]
    public string RiskLevel { get; set; } = string.Empty;

    [Description("风险原因说明列表")]
    public List<string> RiskReasons { get; set; } = new();

    [Description("是否蜜罐代币（买入后无法卖出）")]
    public bool IsHoneypot { get; set; }

    [Description("买入税率（%）")]
    public decimal? BuyTaxPercent { get; set; }

    [Description("卖出税率（%）")]
    public decimal? SellTaxPercent { get; set; }

    [Description("合约是否可增发")]
    public bool IsMintable { get; set; }

    [Description("合约所有者是否可随时暂停交易")]
    public bool IsTradableRestricted { get; set; }

    [Description("合约是否可修改税率")]
    public bool IsFeeChangable { get; set; }

    [Description("合约是否可黑名单地址")]
    public bool IsBlacklistable { get; set; }

    [Description("合约是否可白名单限制")]
    public bool IsWhitelistable { get; set; }

    [Description("合约是否已放弃所有权（放弃后无法再作恶）")]
    public bool IsOwnershipRenounced { get; set; }

    [Description("合约是否开源可验证")]
    public bool IsOpenSource { get; set; }

    [Description("是否存在隐藏的代理合约升级风险")]
    public bool IsProxyContract { get; set; }

    [Description("是否超过 10 个持有者（false 通常为假币）")]
    public bool HasEnoughHolders { get; set; }

    [Description("是否存在非流动性风险（无法卖出换回资产）")]
    public bool IsAntiWhaleModified { get; set; }

    [Description("安全建议说明")]
    public string SecurityAdvice { get; set; } = string.Empty;
}

[Description("链上大额转账信号")]
public class LargeTransferSignal
{
    [Description("链 ID")]
    public string ChainId { get; set; } = string.Empty;

    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("代币合约地址")]
    public string TokenAddress { get; set; } = string.Empty;

    [Description("转账笔数")]
    public int TransferCount { get; set; }

    [Description("转账总额（代币数量）")]
    public decimal? TotalAmount { get; set; }

    [Description("转账总价值（USD）")]
    public decimal? TotalValueUsd { get; set; }

    [Description("接盘地址数量")]
    public int UniqueBuyerCount { get; set; }

    [Description("信号说明")]
    public string Signal { get; set; } = string.Empty;
}