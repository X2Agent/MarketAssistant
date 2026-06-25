using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

[Description("财务主要指标")]
public class FinancialRatios
{
    [Description("报告截止日期")]
    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = "";

    [Description("披露日期")]
    [JsonPropertyName("plrq")]
    public string DisclosureDate { get; set; } = "";

    [Description("每股经营活动现金流量")]
    [JsonPropertyName("mgjyhdxjl")]
    public decimal? CashFlowPerShare { get; set; }

    [Description("每股净资产")]
    [JsonPropertyName("mgjzc")]
    public decimal? NetAssetsPerShare { get; set; }

    [Description("基本每股收益")]
    [JsonPropertyName("jbmgsy")]
    public decimal? BasicEarningsPerShare { get; set; }

    [Description("稀释每股收益")]
    [JsonPropertyName("xsmgsy")]
    public decimal? DilutedEarningsPerShare { get; set; }

    [Description("每股未分配利润")]
    [JsonPropertyName("mgwfplr")]
    public decimal? RetainedEarningsPerShare { get; set; }

    [Description("每股资本公积金")]
    [JsonPropertyName("mgzbgjj")]
    public decimal? CapitalReservePerShare { get; set; }

    [Description("扣非每股收益")]
    [JsonPropertyName("kfmgsy")]
    public decimal? AdjustedEarningsPerShare { get; set; }

    [Description("净资产收益率（%）")]
    [JsonPropertyName("jzcsyl")]
    public decimal? ReturnOnEquity { get; set; }

    [Description("加权净资产收益率（%）")]
    [JsonPropertyName("jqjzcsyl")]
    public decimal? WeightedROE { get; set; }

    [Description("摊薄净资产收益率（%）")]
    [JsonPropertyName("tbjzcsyl")]
    public decimal? DilutedROE { get; set; }

    [Description("摊薄总资产收益率（%）")]
    [JsonPropertyName("tbzzcsyl")]
    public decimal? ReturnOnAssets { get; set; }

    [Description("销售毛利率（%）")]
    [JsonPropertyName("xsmlv")]
    public decimal? GrossMargin { get; set; }

    [Description("毛利率（%）")]
    [JsonPropertyName("mlv")]
    public decimal? GrossProfitMargin { get; set; }

    [Description("净利率（%）")]
    [JsonPropertyName("jlv")]
    public decimal? NetProfitMargin { get; set; }

    [Description("实际税率（%）")]
    [JsonPropertyName("sjslv")]
    public decimal? EffectiveTaxRate { get; set; }

    [Description("预收账款占营业收入比（%）")]
    [JsonPropertyName("yskyysr")]
    public decimal? AdvanceReceiptsToRevenue { get; set; }

    [Description("销售现金流占营业收入比（%）")]
    [JsonPropertyName("xsxjlyysr")]
    public decimal? OperatingCashFlowToRevenue { get; set; }

    [Description("资产负债率（%）")]
    [JsonPropertyName("zcfzl")]
    public decimal? AssetLiabilityRatio { get; set; }

    [Description("存货周转率")]
    [JsonPropertyName("chzzl")]
    public decimal? InventoryTurnoverRatio { get; set; }

    [Description("主营收入同比增长（%）")]
    [JsonPropertyName("zyyrsrzz")]
    public decimal? RevenueGrowthYoY { get; set; }

    [Description("净利润同比增长（%）")]
    [JsonPropertyName("jlrzz")]
    public decimal? NetProfitGrowthYoY { get; set; }

    [Description("归母净利润同比增长（%）")]
    [JsonPropertyName("gsmgsyzzdjlrzz")]
    public decimal? ParentNetProfitGrowthYoY { get; set; }

    [Description("扣非净利润同比增长（%）")]
    [JsonPropertyName("kfjlrzz")]
    public decimal? AdjustedNetProfitGrowthYoY { get; set; }

    [Description("营业总收入滚动环比增长（%）")]
    [JsonPropertyName("yyzsrgdhbzz")]
    public decimal? RevenueGrowthQoQ { get; set; }

    [Description("归属净利润滚动环比增长（%）")]
    [JsonPropertyName("sljlrjqhbzz")]
    public decimal? NetProfitGrowthQoQ { get; set; }

    [Description("扣非净利润滚动环比增长（%）")]
    [JsonPropertyName("kfjlrgdhbzz")]
    public decimal? AdjustedNetProfitGrowthQoQ { get; set; }
}