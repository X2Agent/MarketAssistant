using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Plugins.Models;

/// <summary>
/// 现金流量表
/// </summary>
public class CashFlowStatement
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
    /// 销售商品、提供劳务收到的现金
    /// </summary>
    [JsonPropertyName("xssptglwsddxj")]
    public decimal? CashFromSalesAndServices { get; set; }

    /// <summary>
    /// 收到的税费与返还
    /// </summary>
    [JsonPropertyName("sddsfyfh")]
    public decimal? TaxRefundsReceived { get; set; }

    /// <summary>
    /// 收到的其他与经营活动有关的现金
    /// </summary>
    [JsonPropertyName("sdqtyjyghdxj")]
    public decimal? OtherCashFromOperating { get; set; }

    /// <summary>
    /// 经营活动现金流入小计
    /// </summary>
    [JsonPropertyName("jyhdxjlrxj")]
    public decimal? TotalCashInflowsFromOperating { get; set; }

    /// <summary>
    /// 购买商品、接受劳务支付的现金
    /// </summary>
    [JsonPropertyName("gmspjslwzfdxj")]
    public decimal? CashPaidForGoodsAndServices { get; set; }

    /// <summary>
    /// 支付给职工以及为职工支付的现金
    /// </summary>
    [JsonPropertyName("zfgzyjwzgzfdxj")]
    public decimal? CashPaidToEmployees { get; set; }

    /// <summary>
    /// 支付的各项税费
    /// </summary>
    [JsonPropertyName("zfdgxsf")]
    public decimal? TaxesPaid { get; set; }

    /// <summary>
    /// 支付其他与经营活动有关的现金
    /// </summary>
    [JsonPropertyName("zfqtyjyghdxj")]
    public decimal? OtherCashPaidForOperating { get; set; }

    /// <summary>
    /// 经营活动现金流出小计
    /// </summary>
    [JsonPropertyName("jyhdxjlcxj")]
    public decimal? TotalCashOutflowsFromOperating { get; set; }

    /// <summary>
    /// 经营活动产生的现金流量净额
    /// </summary>
    [JsonPropertyName("jyhdcsdxjlje")]
    public decimal? NetCashFlowFromOperating { get; set; }

    /// <summary>
    /// 收回投资所收到的现金
    /// </summary>
    [JsonPropertyName("shtzssddxj")]
    public decimal? CashFromInvestmentRecovery { get; set; }

    /// <summary>
    /// 取得投资收益所收到的现金
    /// </summary>
    [JsonPropertyName("qdtzsysddxj")]
    public decimal? CashFromInvestmentIncome { get; set; }

    /// <summary>
    /// 处置固定资产、无形资产和其他长期投资收到的现金
    /// </summary>
    [JsonPropertyName("czgdzcwxzhqtqctzssddxj")]
    public decimal? CashFromDisposalOfAssets { get; set; }

    /// <summary>
    /// 收到的其他与投资活动有关的现金
    /// </summary>
    [JsonPropertyName("sdqtytzghdxj")]
    public decimal? OtherCashFromInvesting { get; set; }

    /// <summary>
    /// 投资活动现金流入小计
    /// </summary>
    [JsonPropertyName("tzhdxjlrxj")]
    public decimal? TotalCashInflowsFromInvesting { get; set; }

    /// <summary>
    /// 购建固定资产、无形资产和其他长期投资支付的现金
    /// </summary>
    [JsonPropertyName("gjgdzcwxzhqtqctzzfdxj")]
    public decimal? CashPaidForAssets { get; set; }

    /// <summary>
    /// 投资支付的现金
    /// </summary>
    [JsonPropertyName("tzzfdxj")]
    public decimal? CashPaidForInvestments { get; set; }

    /// <summary>
    /// 投资活动现金流出小计
    /// </summary>
    [JsonPropertyName("tzhdxjlcxj")]
    public decimal? TotalCashOutflowsFromInvesting { get; set; }

    /// <summary>
    /// 投资活动产生的现金流量净额
    /// </summary>
    [JsonPropertyName("tzhdcsdxjlxj")]
    public decimal? NetCashFlowFromInvesting { get; set; }

    /// <summary>
    /// 吸收投资收到的现金
    /// </summary>
    [JsonPropertyName("xstzsdj")]
    public decimal? CashFromEquityIssuance { get; set; }

    /// <summary>
    /// 取得借款收到的现金
    /// </summary>
    [JsonPropertyName("qdjkjddxj")]
    public decimal? CashFromBorrowings { get; set; }

    /// <summary>
    /// 发行债券收到的现金
    /// </summary>
    [JsonPropertyName("fxzjsddxj")]
    public decimal? CashFromBondIssuance { get; set; }

    /// <summary>
    /// 收到其他与筹资活动有关的现金
    /// </summary>
    [JsonPropertyName("sdqtczghdxj")]
    public decimal? OtherCashFromFinancing { get; set; }

    /// <summary>
    /// 筹资活动现金流入小计
    /// </summary>
    [JsonPropertyName("czhdxjlrxj")]
    public decimal? TotalCashInflowsFromFinancing { get; set; }

    /// <summary>
    /// 偿还债务支付现金
    /// </summary>
    [JsonPropertyName("chzwzfxj")]
    public decimal? CashPaidForDebtRepayment { get; set; }

    /// <summary>
    /// 分配股利、利润或偿付利息支付的现金
    /// </summary>
    [JsonPropertyName("fpglrlhcllxzfdxj")]
    public decimal? CashPaidForDividendsAndInterest { get; set; }

    /// <summary>
    /// 支付其他与筹资的现金
    /// </summary>
    [JsonPropertyName("zfqtczdxj")]
    public decimal? OtherCashPaidForFinancing { get; set; }

    /// <summary>
    /// 筹资活动现金流出小计
    /// </summary>
    [JsonPropertyName("czhdxjlcxj")]
    public decimal? TotalCashOutflowsFromFinancing { get; set; }

    /// <summary>
    /// 筹资活动产生的现金流量净额
    /// </summary>
    [JsonPropertyName("czhdcsdxjlxj")]
    public decimal? NetCashFlowFromFinancing { get; set; }

    /// <summary>
    /// 汇率变动对现金的影响
    /// </summary>
    [JsonPropertyName("hlbddxjdxy")]
    public decimal? ExchangeRateEffect { get; set; }

    /// <summary>
    /// 现金及现金等价物净增加额
    /// </summary>
    [JsonPropertyName("xjxjdhwjzje")]
    public decimal? NetIncreaseInCash { get; set; }

    /// <summary>
    /// 期初现金及现金等价物余额
    /// </summary>
    [JsonPropertyName("qcxjjxjdhwye")]
    public decimal? BeginningCashBalance { get; set; }

    /// <summary>
    /// 期末现金及现金等价物余额
    /// </summary>
    [JsonPropertyName("qmxjjxjdhwye")]
    public decimal? EndingCashBalance { get; set; }
}

