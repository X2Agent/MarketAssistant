using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 利润表
/// </summary>
public class IncomeStatement
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
    /// 营业收入
    /// </summary>
    [JsonPropertyName("yysr")]
    public decimal? OperatingRevenue { get; set; }

    /// <summary>
    /// 营业总收入
    /// </summary>
    [JsonPropertyName("yyzsr")]
    public decimal? TotalOperatingRevenue { get; set; }

    /// <summary>
    /// 营业成本
    /// </summary>
    [JsonPropertyName("yycb")]
    public decimal? OperatingCost { get; set; }

    /// <summary>
    /// 营业总成本
    /// </summary>
    [JsonPropertyName("yyzcb")]
    public decimal? TotalOperatingCost { get; set; }

    /// <summary>
    /// 营业税金及附加
    /// </summary>
    [JsonPropertyName("yysjjfj")]
    public decimal? BusinessTaxAndSurcharges { get; set; }

    /// <summary>
    /// 销售费用
    /// </summary>
    [JsonPropertyName("xsfy")]
    public decimal? SellingExpenses { get; set; }

    /// <summary>
    /// 管理费用
    /// </summary>
    [JsonPropertyName("glfy")]
    public decimal? AdministrativeExpenses { get; set; }

    /// <summary>
    /// 研发费用
    /// </summary>
    [JsonPropertyName("yffy")]
    public decimal? RAndDExpenses { get; set; }

    /// <summary>
    /// 财务费用
    /// </summary>
    [JsonPropertyName("cwfy")]
    public decimal? FinancialExpenses { get; set; }

    /// <summary>
    /// 利息收入
    /// </summary>
    [JsonPropertyName("lxsr")]
    public decimal? InterestIncome { get; set; }

    /// <summary>
    /// 利息支出
    /// </summary>
    [JsonPropertyName("lxzc")]
    public decimal? InterestExpense { get; set; }

    /// <summary>
    /// 资产减值损失
    /// </summary>
    [JsonPropertyName("zcjzss")]
    public decimal? AssetImpairmentLoss { get; set; }

    /// <summary>
    /// 公允价值变动收益
    /// </summary>
    [JsonPropertyName("gyjzbdsy")]
    public decimal? FairValueChangeGain { get; set; }

    /// <summary>
    /// 投资收益
    /// </summary>
    [JsonPropertyName("tzsy")]
    public decimal? InvestmentIncome { get; set; }

    /// <summary>
    /// 联营企业和合营企业的投资收益
    /// </summary>
    [JsonPropertyName("lyqyhhhqydtzsy")]
    public decimal? InvestmentIncomeFromAssociates { get; set; }

    /// <summary>
    /// 其他收益
    /// </summary>
    [JsonPropertyName("qtsy")]
    public decimal? OtherIncome { get; set; }

    /// <summary>
    /// 营业利润
    /// </summary>
    [JsonPropertyName("yylr")]
    public decimal? OperatingProfit { get; set; }

    /// <summary>
    /// 营业外收入
    /// </summary>
    [JsonPropertyName("ywsr")]
    public decimal? NonOperatingIncome { get; set; }

    /// <summary>
    /// 营业外支出
    /// </summary>
    [JsonPropertyName("ywzc")]
    public decimal? NonOperatingExpenses { get; set; }

    /// <summary>
    /// 利润总额
    /// </summary>
    [JsonPropertyName("lrze")]
    public decimal? TotalProfit { get; set; }

    /// <summary>
    /// 所得税费用
    /// </summary>
    [JsonPropertyName("sdsfy")]
    public decimal? IncomeTaxExpense { get; set; }

    /// <summary>
    /// 净利润
    /// </summary>
    [JsonPropertyName("jlr")]
    public decimal? NetProfit { get; set; }

    /// <summary>
    /// 归属于母公司所有者的净利润
    /// </summary>
    [JsonPropertyName("gsmgsyzzdjlr")]
    public decimal? NetProfitAttributableToParent { get; set; }

    /// <summary>
    /// 少数股东损益
    /// </summary>
    [JsonPropertyName("ssgdsy")]
    public decimal? MinorityInterestIncome { get; set; }

    /// <summary>
    /// 基本每股收益
    /// </summary>
    [JsonPropertyName("jbmgsy")]
    public decimal? BasicEarningsPerShare { get; set; }

    /// <summary>
    /// 稀释每股收益
    /// </summary>
    [JsonPropertyName("xsmgsy")]
    public decimal? DilutedEarningsPerShare { get; set; }

    /// <summary>
    /// 综合收益总额
    /// </summary>
    [JsonPropertyName("zhsyz")]
    public decimal? TotalComprehensiveIncome { get; set; }
}

