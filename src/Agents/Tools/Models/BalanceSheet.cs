using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 资产负债表
/// </summary>
public class BalanceSheet
{
    /// <summary>
    /// 截止日期
    /// </summary>
    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = "";

    /// <summary>
    /// 披露日期
    /// </summary>
    [JsonPropertyName("plrq")]
    public string DisclosureDate { get; set; } = "";

    /// <summary>
    /// 货币资金
    /// </summary>
    [JsonPropertyName("hbzj")]
    public decimal? MonetaryFunds { get; set; }

    /// <summary>
    /// 交易性金融资产
    /// </summary>
    [JsonPropertyName("jyxjrzc")]
    public decimal? TradingFinancialAssets { get; set; }

    /// <summary>
    /// 应收票据
    /// </summary>
    [JsonPropertyName("yspj")]
    public decimal? NotesReceivable { get; set; }

    /// <summary>
    /// 应收账款
    /// </summary>
    [JsonPropertyName("yszk")]
    public decimal? AccountsReceivable { get; set; }

    /// <summary>
    /// 预付款项
    /// </summary>
    [JsonPropertyName("yfkx")]
    public decimal? AdvancePayments { get; set; }

    /// <summary>
    /// 其他应收款
    /// </summary>
    [JsonPropertyName("qtysk")]
    public decimal? OtherReceivables { get; set; }

    /// <summary>
    /// 存货
    /// </summary>
    [JsonPropertyName("ch")]
    public decimal? Inventory { get; set; }

    /// <summary>
    /// 其他流动资产
    /// </summary>
    [JsonPropertyName("qtldzc")]
    public decimal? OtherCurrentAssets { get; set; }

    /// <summary>
    /// 流动资产合计
    /// </summary>
    [JsonPropertyName("ldzchj")]
    public decimal? TotalCurrentAssets { get; set; }

    /// <summary>
    /// 长期股权投资
    /// </summary>
    [JsonPropertyName("cqgqtz")]
    public decimal? LongTermEquityInvestment { get; set; }

    /// <summary>
    /// 固定资产
    /// </summary>
    [JsonPropertyName("gdzc")]
    public decimal? FixedAssets { get; set; }

    /// <summary>
    /// 在建工程
    /// </summary>
    [JsonPropertyName("zjgc")]
    public decimal? ConstructionInProgress { get; set; }

    /// <summary>
    /// 无形资产
    /// </summary>
    [JsonPropertyName("wxzc")]
    public decimal? IntangibleAssets { get; set; }

    /// <summary>
    /// 商誉
    /// </summary>
    [JsonPropertyName("sy")]
    public decimal? Goodwill { get; set; }

    /// <summary>
    /// 递延所得税资产
    /// </summary>
    [JsonPropertyName("dysdszc")]
    public decimal? DeferredTaxAssets { get; set; }

    /// <summary>
    /// 非流动资产合计
    /// </summary>
    [JsonPropertyName("fldzchj")]
    public decimal? TotalNonCurrentAssets { get; set; }

    /// <summary>
    /// 资产总计
    /// </summary>
    [JsonPropertyName("zczj")]
    public decimal? TotalAssets { get; set; }

    /// <summary>
    /// 短期借款
    /// </summary>
    [JsonPropertyName("dqjk")]
    public decimal? ShortTermBorrowings { get; set; }

    /// <summary>
    /// 应付票据
    /// </summary>
    [JsonPropertyName("yfpj")]
    public decimal? NotesPayable { get; set; }

    /// <summary>
    /// 应付账款
    /// </summary>
    [JsonPropertyName("yfzk")]
    public decimal? AccountsPayable { get; set; }

    /// <summary>
    /// 预收账款
    /// </summary>
    [JsonPropertyName("ysk")]
    public decimal? AdvanceReceipts { get; set; }

    /// <summary>
    /// 应付职工薪酬
    /// </summary>
    [JsonPropertyName("yfgzxc")]
    public decimal? EmployeeBenefitsPayable { get; set; }

    /// <summary>
    /// 应交税费
    /// </summary>
    [JsonPropertyName("yjsf")]
    public decimal? TaxesPayable { get; set; }

    /// <summary>
    /// 应付利息
    /// </summary>
    [JsonPropertyName("yflx")]
    public decimal? InterestPayable { get; set; }

    /// <summary>
    /// 其他应付款
    /// </summary>
    [JsonPropertyName("qtfzk")]
    public decimal? OtherPayables { get; set; }

    /// <summary>
    /// 一年内到期的非流动负债
    /// </summary>
    [JsonPropertyName("ynndqdfldfz")]
    public decimal? CurrentPortionOfNonCurrentLiabilities { get; set; }

    /// <summary>
    /// 其他流动负债
    /// </summary>
    [JsonPropertyName("qtldfz")]
    public decimal? OtherCurrentLiabilities { get; set; }

    /// <summary>
    /// 流动负债合计
    /// </summary>
    [JsonPropertyName("ldfzhj")]
    public decimal? TotalCurrentLiabilities { get; set; }

    /// <summary>
    /// 长期借款
    /// </summary>
    [JsonPropertyName("cqjk")]
    public decimal? LongTermBorrowings { get; set; }

    /// <summary>
    /// 应付债券
    /// </summary>
    [JsonPropertyName("yfzq")]
    public decimal? BondsPayable { get; set; }

    /// <summary>
    /// 递延所得税负债
    /// </summary>
    [JsonPropertyName("dysdsfz")]
    public decimal? DeferredTaxLiabilities { get; set; }

    /// <summary>
    /// 其他非流动负债
    /// </summary>
    [JsonPropertyName("qtfldfz")]
    public decimal? OtherNonCurrentLiabilities { get; set; }

    /// <summary>
    /// 非流动负债合计
    /// </summary>
    [JsonPropertyName("fldfzhj")]
    public decimal? TotalNonCurrentLiabilities { get; set; }

    /// <summary>
    /// 负债合计
    /// </summary>
    [JsonPropertyName("fzhj")]
    public decimal? TotalLiabilities { get; set; }

    /// <summary>
    /// 实收资本(或股本)
    /// </summary>
    [JsonPropertyName("sszb")]
    public decimal? PaidInCapital { get; set; }

    /// <summary>
    /// 资本公积
    /// </summary>
    [JsonPropertyName("zbgj")]
    public decimal? CapitalReserve { get; set; }

    /// <summary>
    /// 盈余公积
    /// </summary>
    [JsonPropertyName("ylgj")]
    public decimal? SurplusReserve { get; set; }

    /// <summary>
    /// 未分配利润
    /// </summary>
    [JsonPropertyName("wfplr")]
    public decimal? RetainedEarnings { get; set; }

    /// <summary>
    /// 归属于母公司股东权益合计
    /// </summary>
    [JsonPropertyName("gsmgdqsyhj")]
    public decimal? TotalEquityAttributableToParent { get; set; }

    /// <summary>
    /// 少数股东权益
    /// </summary>
    [JsonPropertyName("ssgdqy")]
    public decimal? MinorityInterest { get; set; }

    /// <summary>
    /// 所有者权益合计
    /// </summary>
    [JsonPropertyName("syzqyhj")]
    public decimal? TotalEquity { get; set; }

    /// <summary>
    /// 负债和股东权益总计
    /// </summary>
    [JsonPropertyName("fzhgdqyzj")]
    public decimal? TotalLiabilitiesAndEquity { get; set; }
}

