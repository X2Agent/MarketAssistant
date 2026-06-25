using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

[Description("利润表")]
public class IncomeStatement
{
    [Description("报告截止日期")]
    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = "";

    [Description("披露日期")]
    [JsonPropertyName("plrq")]
    public string DisclosureDate { get; set; } = "";

    [Description("营业收入")]
    [JsonPropertyName("yysr")]
    public decimal? OperatingRevenue { get; set; }

    [Description("营业总收入")]
    [JsonPropertyName("yyzsr")]
    public decimal? TotalOperatingRevenue { get; set; }

    [Description("营业成本")]
    [JsonPropertyName("yycb")]
    public decimal? OperatingCost { get; set; }

    [Description("营业总成本")]
    [JsonPropertyName("yyzcb")]
    public decimal? TotalOperatingCost { get; set; }

    [Description("营业税金及附加")]
    [JsonPropertyName("yysjjfj")]
    public decimal? BusinessTaxAndSurcharges { get; set; }

    [Description("销售费用")]
    [JsonPropertyName("xsfy")]
    public decimal? SellingExpenses { get; set; }

    [Description("管理费用")]
    [JsonPropertyName("glfy")]
    public decimal? AdministrativeExpenses { get; set; }

    [Description("研发费用")]
    [JsonPropertyName("yffy")]
    public decimal? RAndDExpenses { get; set; }

    [Description("财务费用")]
    [JsonPropertyName("cwfy")]
    public decimal? FinancialExpenses { get; set; }

    [Description("利息收入")]
    [JsonPropertyName("lxsr")]
    public decimal? InterestIncome { get; set; }

    [Description("利息支出")]
    [JsonPropertyName("lxzc")]
    public decimal? InterestExpense { get; set; }

    [Description("资产减值损失")]
    [JsonPropertyName("zcjzss")]
    public decimal? AssetImpairmentLoss { get; set; }

    [Description("公允价值变动收益")]
    [JsonPropertyName("gyjzbdsy")]
    public decimal? FairValueChangeGain { get; set; }

    [Description("投资收益")]
    [JsonPropertyName("tzsy")]
    public decimal? InvestmentIncome { get; set; }

    [Description("联营企业和合营企业的投资收益")]
    [JsonPropertyName("lyqyhhhqydtzsy")]
    public decimal? InvestmentIncomeFromAssociates { get; set; }

    [Description("其他收益")]
    [JsonPropertyName("qtsy")]
    public decimal? OtherIncome { get; set; }

    [Description("营业利润")]
    [JsonPropertyName("yylr")]
    public decimal? OperatingProfit { get; set; }

    [Description("营业外收入")]
    [JsonPropertyName("ywsr")]
    public decimal? NonOperatingIncome { get; set; }

    [Description("营业外支出")]
    [JsonPropertyName("ywzc")]
    public decimal? NonOperatingExpenses { get; set; }

    [Description("利润总额")]
    [JsonPropertyName("lrze")]
    public decimal? TotalProfit { get; set; }

    [Description("所得税费用")]
    [JsonPropertyName("sdsfy")]
    public decimal? IncomeTaxExpense { get; set; }

    [Description("净利润")]
    [JsonPropertyName("jlr")]
    public decimal? NetProfit { get; set; }

    [Description("归属于母公司所有者的净利润")]
    [JsonPropertyName("gsmgsyzzdjlr")]
    public decimal? NetProfitAttributableToParent { get; set; }

    [Description("少数股东损益")]
    [JsonPropertyName("ssgdsy")]
    public decimal? MinorityInterestIncome { get; set; }

    [Description("基本每股收益")]
    [JsonPropertyName("jbmgsy")]
    public decimal? BasicEarningsPerShare { get; set; }

    [Description("稀释每股收益")]
    [JsonPropertyName("xsmgsy")]
    public decimal? DilutedEarningsPerShare { get; set; }

    [Description("综合收益总额")]
    [JsonPropertyName("zhsyz")]
    public decimal? TotalComprehensiveIncome { get; set; }
}