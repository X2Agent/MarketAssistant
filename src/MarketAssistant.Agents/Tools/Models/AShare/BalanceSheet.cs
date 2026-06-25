using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

[Description("资产负债表")]
public class BalanceSheet
{
    [Description("报告截止日期")]
    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = string.Empty;

    [Description("披露日期")]
    [JsonPropertyName("plrq")]
    public string DisclosureDate { get; set; } = string.Empty;

    [Description("货币资金")]
    [JsonPropertyName("hbzj")]
    public decimal? MonetaryFunds { get; set; }

    [Description("交易性金融资产")]
    [JsonPropertyName("jyxjrzc")]
    public decimal? TradingFinancialAssets { get; set; }

    [Description("应收票据")]
    [JsonPropertyName("yspj")]
    public decimal? NotesReceivable { get; set; }

    [Description("应收账款")]
    [JsonPropertyName("yszk")]
    public decimal? AccountsReceivable { get; set; }

    [Description("预付款项")]
    [JsonPropertyName("yfkx")]
    public decimal? AdvancePayments { get; set; }

    [Description("其他应收款")]
    [JsonPropertyName("qtysk")]
    public decimal? OtherReceivables { get; set; }

    [Description("存货")]
    [JsonPropertyName("ch")]
    public decimal? Inventory { get; set; }

    [Description("其他流动资产")]
    [JsonPropertyName("qtldzc")]
    public decimal? OtherCurrentAssets { get; set; }

    [Description("流动资产合计")]
    [JsonPropertyName("ldzchj")]
    public decimal? TotalCurrentAssets { get; set; }

    [Description("长期股权投资")]
    [JsonPropertyName("cqgqtz")]
    public decimal? LongTermEquityInvestment { get; set; }

    [Description("固定资产")]
    [JsonPropertyName("gdzc")]
    public decimal? FixedAssets { get; set; }

    [Description("在建工程")]
    [JsonPropertyName("zjgc")]
    public decimal? ConstructionInProgress { get; set; }

    [Description("无形资产")]
    [JsonPropertyName("wxzc")]
    public decimal? IntangibleAssets { get; set; }

    [Description("商誉")]
    [JsonPropertyName("sy")]
    public decimal? Goodwill { get; set; }

    [Description("递延所得税资产")]
    [JsonPropertyName("dysdszc")]
    public decimal? DeferredTaxAssets { get; set; }

    [Description("非流动资产合计")]
    [JsonPropertyName("fldzchj")]
    public decimal? TotalNonCurrentAssets { get; set; }

    [Description("资产总计")]
    [JsonPropertyName("zczj")]
    public decimal? TotalAssets { get; set; }

    [Description("短期借款")]
    [JsonPropertyName("dqjk")]
    public decimal? ShortTermBorrowings { get; set; }

    [Description("应付票据")]
    [JsonPropertyName("yfpj")]
    public decimal? NotesPayable { get; set; }

    [Description("应付账款")]
    [JsonPropertyName("yfzk")]
    public decimal? AccountsPayable { get; set; }

    [Description("预收账款")]
    [JsonPropertyName("ysk")]
    public decimal? AdvanceReceipts { get; set; }

    [Description("应付职工薪酬")]
    [JsonPropertyName("yfgzxc")]
    public decimal? EmployeeBenefitsPayable { get; set; }

    [Description("应交税费")]
    [JsonPropertyName("yjsf")]
    public decimal? TaxesPayable { get; set; }

    [Description("应付利息")]
    [JsonPropertyName("yflx")]
    public decimal? InterestPayable { get; set; }

    [Description("其他应付款")]
    [JsonPropertyName("qtfzk")]
    public decimal? OtherPayables { get; set; }

    [Description("一年内到期的非流动负债")]
    [JsonPropertyName("ynndqdfldfz")]
    public decimal? CurrentPortionOfNonCurrentLiabilities { get; set; }

    [Description("其他流动负债")]
    [JsonPropertyName("qtldfz")]
    public decimal? OtherCurrentLiabilities { get; set; }

    [Description("流动负债合计")]
    [JsonPropertyName("ldfzhj")]
    public decimal? TotalCurrentLiabilities { get; set; }

    [Description("长期借款")]
    [JsonPropertyName("cqjk")]
    public decimal? LongTermBorrowings { get; set; }

    [Description("应付债券")]
    [JsonPropertyName("yfzq")]
    public decimal? BondsPayable { get; set; }

    [Description("递延所得税负债")]
    [JsonPropertyName("dysdsfz")]
    public decimal? DeferredTaxLiabilities { get; set; }

    [Description("其他非流动负债")]
    [JsonPropertyName("qtfldfz")]
    public decimal? OtherNonCurrentLiabilities { get; set; }

    [Description("非流动负债合计")]
    [JsonPropertyName("fldfzhj")]
    public decimal? TotalNonCurrentLiabilities { get; set; }

    [Description("负债合计")]
    [JsonPropertyName("fzhj")]
    public decimal? TotalLiabilities { get; set; }

    [Description("实收资本（或股本）")]
    [JsonPropertyName("sszb")]
    public decimal? PaidInCapital { get; set; }

    [Description("资本公积")]
    [JsonPropertyName("zbgj")]
    public decimal? CapitalReserve { get; set; }

    [Description("盈余公积")]
    [JsonPropertyName("ylgj")]
    public decimal? SurplusReserve { get; set; }

    [Description("未分配利润")]
    [JsonPropertyName("wfplr")]
    public decimal? RetainedEarnings { get; set; }

    [Description("归属于母公司股东权益合计")]
    [JsonPropertyName("gsmgdqsyhj")]
    public decimal? TotalEquityAttributableToParent { get; set; }

    [Description("少数股东权益")]
    [JsonPropertyName("ssgdqy")]
    public decimal? MinorityInterest { get; set; }

    [Description("所有者权益合计")]
    [JsonPropertyName("syzqyhj")]
    public decimal? TotalEquity { get; set; }

    [Description("负债和股东权益总计")]
    [JsonPropertyName("fzhgdqyzj")]
    public decimal? TotalLiabilitiesAndEquity { get; set; }
}
