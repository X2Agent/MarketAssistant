using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 财务主要指标
/// </summary>
public class FinancialRatios
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
    /// 每股经营活动现金流量
    /// </summary>
    [JsonPropertyName("mgjyhdxjl")]
    public decimal? CashFlowPerShare { get; set; }

    /// <summary>
    /// 每股净资产
    /// </summary>
    [JsonPropertyName("mgjzc")]
    public decimal? NetAssetsPerShare { get; set; }

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
    /// 每股未分配利润
    /// </summary>
    [JsonPropertyName("mgwfplr")]
    public decimal? RetainedEarningsPerShare { get; set; }

    /// <summary>
    /// 每股资本公积金
    /// </summary>
    [JsonPropertyName("mgzbgjj")]
    public decimal? CapitalReservePerShare { get; set; }

    /// <summary>
    /// 扣非每股收益
    /// </summary>
    [JsonPropertyName("kfmgsy")]
    public decimal? AdjustedEarningsPerShare { get; set; }

    /// <summary>
    /// 净资产收益率
    /// </summary>
    [JsonPropertyName("jzcsyl")]
    public decimal? ReturnOnEquity { get; set; }

    /// <summary>
    /// 加权净资产收益率
    /// </summary>
    [JsonPropertyName("jqjzcsyl")]
    public decimal? WeightedROE { get; set; }

    /// <summary>
    /// 摊薄净资产收益率
    /// </summary>
    [JsonPropertyName("tbjzcsyl")]
    public decimal? DilutedROE { get; set; }

    /// <summary>
    /// 摊薄总资产收益率
    /// </summary>
    [JsonPropertyName("tbzzcsyl")]
    public decimal? ReturnOnAssets { get; set; }

    /// <summary>
    /// 销售毛利率
    /// </summary>
    [JsonPropertyName("xsmlv")]
    public decimal? GrossMargin { get; set; }

    /// <summary>
    /// 毛利率
    /// </summary>
    [JsonPropertyName("mlv")]
    public decimal? GrossProfitMargin { get; set; }

    /// <summary>
    /// 净利率
    /// </summary>
    [JsonPropertyName("jlv")]
    public decimal? NetProfitMargin { get; set; }

    /// <summary>
    /// 实际税率
    /// </summary>
    [JsonPropertyName("sjslv")]
    public decimal? EffectiveTaxRate { get; set; }

    /// <summary>
    /// 预收款营业收入
    /// </summary>
    [JsonPropertyName("yskyysr")]
    public decimal? AdvanceReceiptsToRevenue { get; set; }

    /// <summary>
    /// 销售现金流营业收入
    /// </summary>
    [JsonPropertyName("xsxjlyysr")]
    public decimal? OperatingCashFlowToRevenue { get; set; }

    /// <summary>
    /// 资产负债比率
    /// </summary>
    [JsonPropertyName("zcfzl")]
    public decimal? AssetLiabilityRatio { get; set; }

    /// <summary>
    /// 存货周转率
    /// </summary>
    [JsonPropertyName("chzzl")]
    public decimal? InventoryTurnoverRatio { get; set; }

    /// <summary>
    /// 主营收入同比增长
    /// </summary>
    [JsonPropertyName("zyyrsrzz")]
    public decimal? RevenueGrowthYoY { get; set; }

    /// <summary>
    /// 净利润同比增长
    /// </summary>
    [JsonPropertyName("jlrzz")]
    public decimal? NetProfitGrowthYoY { get; set; }

    /// <summary>
    /// 归属于母公司所有者的净利润同比增长
    /// </summary>
    [JsonPropertyName("gsmgsyzzdjlrzz")]
    public decimal? ParentNetProfitGrowthYoY { get; set; }

    /// <summary>
    /// 扣非净利润同比增长
    /// </summary>
    [JsonPropertyName("kfjlrzz")]
    public decimal? AdjustedNetProfitGrowthYoY { get; set; }

    /// <summary>
    /// 营业总收入滚动环比增长
    /// </summary>
    [JsonPropertyName("yyzsrgdhbzz")]
    public decimal? RevenueGrowthQoQ { get; set; }

    /// <summary>
    /// 归属净利润滚动环比增长
    /// </summary>
    [JsonPropertyName("sljlrjqhbzz")]
    public decimal? NetProfitGrowthQoQ { get; set; }

    /// <summary>
    /// 扣非净利润滚动环比增长
    /// </summary>
    [JsonPropertyName("kfjlrgdhbzz")]
    public decimal? AdjustedNetProfitGrowthQoQ { get; set; }
}

