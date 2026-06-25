using System.ComponentModel;
using System.Text.Json.Serialization;

namespace MarketAssistant.Agents.Tools.Models.AShare;

[Description("现金流量表")]
public class CashFlowStatement
{
    [Description("报告截止日期")]
    [JsonPropertyName("jzrq")]
    public string EndDate { get; set; } = "";

    [Description("披露日期")]
    [JsonPropertyName("plrq")]
    public string DisclosureDate { get; set; } = "";

    [Description("销售商品、提供劳务收到的现金")]
    [JsonPropertyName("xssptglwsddxj")]
    public decimal? CashFromSalesAndServices { get; set; }

    [Description("收到的税费返还")]
    [JsonPropertyName("sddsfyfh")]
    public decimal? TaxRefundsReceived { get; set; }

    [Description("收到的其他与经营活动有关的现金")]
    [JsonPropertyName("sdqtyjyghdxj")]
    public decimal? OtherCashFromOperating { get; set; }

    [Description("经营活动现金流入小计")]
    [JsonPropertyName("jyhdxjlrxj")]
    public decimal? TotalCashInflowsFromOperating { get; set; }

    [Description("购买商品、接受劳务支付的现金")]
    [JsonPropertyName("gmspjslwzfdxj")]
    public decimal? CashPaidForGoodsAndServices { get; set; }

    [Description("支付给职工以及为职工支付的现金")]
    [JsonPropertyName("zfgzyjwzgzfdxj")]
    public decimal? CashPaidToEmployees { get; set; }

    [Description("支付的各项税费")]
    [JsonPropertyName("zfdgxsf")]
    public decimal? TaxesPaid { get; set; }

    [Description("支付其他与经营活动有关的现金")]
    [JsonPropertyName("zfqtyjyghdxj")]
    public decimal? OtherCashPaidForOperating { get; set; }

    [Description("经营活动现金流出小计")]
    [JsonPropertyName("jyhdxjlcxj")]
    public decimal? TotalCashOutflowsFromOperating { get; set; }

    [Description("经营活动产生的现金流量净额")]
    [JsonPropertyName("jyhdcsdxjlxj")]
    public decimal? NetCashFlowFromOperating { get; set; }

    [Description("收回投资所收到的现金")]
    [JsonPropertyName("shtzssddxj")]
    public decimal? CashFromInvestmentRecovery { get; set; }

    [Description("取得投资收益所收到的现金")]
    [JsonPropertyName("qdtzsysddxj")]
    public decimal? CashFromInvestmentIncome { get; set; }

    [Description("处置固定资产、无形资产和其他长期资产收到的现金")]
    [JsonPropertyName("czgdzcwxzhqtqctzssddxj")]
    public decimal? CashFromDisposalOfAssets { get; set; }

    [Description("收到的其他与投资活动有关的现金")]
    [JsonPropertyName("sdqtytzghdxj")]
    public decimal? OtherCashFromInvesting { get; set; }

    [Description("投资活动现金流入小计")]
    [JsonPropertyName("tzhdxjlrxj")]
    public decimal? TotalCashInflowsFromInvesting { get; set; }

    [Description("购建固定资产、无形资产和其他长期资产支付的现金")]
    [JsonPropertyName("gjgdzcwxzhqtqctzzfdxj")]
    public decimal? CashPaidForAssets { get; set; }

    [Description("投资支付的现金")]
    [JsonPropertyName("tzzfdxj")]
    public decimal? CashPaidForInvestments { get; set; }

    [Description("投资活动现金流出小计")]
    [JsonPropertyName("tzhdxjlcxj")]
    public decimal? TotalCashOutflowsFromInvesting { get; set; }

    [Description("投资活动产生的现金流量净额")]
    [JsonPropertyName("tzhdcsdxjlxj")]
    public decimal? NetCashFlowFromInvesting { get; set; }

    [Description("吸收投资收到的现金")]
    [JsonPropertyName("xstzsdj")]
    public decimal? CashFromEquityIssuance { get; set; }

    [Description("取得借款收到的现金")]
    [JsonPropertyName("qdjkjddxj")]
    public decimal? CashFromBorrowings { get; set; }

    [Description("发行债券收到的现金")]
    [JsonPropertyName("fxzjsddxj")]
    public decimal? CashFromBondIssuance { get; set; }

    [Description("收到其他与筹资活动有关的现金")]
    [JsonPropertyName("sdqtczghdxj")]
    public decimal? OtherCashFromFinancing { get; set; }

    [Description("筹资活动现金流入小计")]
    [JsonPropertyName("czhdxjlrxj")]
    public decimal? TotalCashInflowsFromFinancing { get; set; }

    [Description("偿还债务支付的现金")]
    [JsonPropertyName("chzwzfxj")]
    public decimal? CashPaidForDebtRepayment { get; set; }

    [Description("分配股利、利润或偿付利息支付的现金")]
    [JsonPropertyName("fpglrlhcllxzfdxj")]
    public decimal? CashPaidForDividendsAndInterest { get; set; }

    [Description("支付其他与筹资活动有关的现金")]
    [JsonPropertyName("zfqtczdxj")]
    public decimal? OtherCashPaidForFinancing { get; set; }

    [Description("筹资活动现金流出小计")]
    [JsonPropertyName("czhdxjlcxj")]
    public decimal? TotalCashOutflowsFromFinancing { get; set; }

    [Description("筹资活动产生的现金流量净额")]
    [JsonPropertyName("czhdcsdxjlxj")]
    public decimal? NetCashFlowFromFinancing { get; set; }

    [Description("汇率变动对现金的影响")]
    [JsonPropertyName("hlbddxjdxy")]
    public decimal? ExchangeRateEffect { get; set; }

    [Description("现金及现金等价物净增加额")]
    [JsonPropertyName("xjxjdhwjzje")]
    public decimal? NetIncreaseInCash { get; set; }

    [Description("期初现金及现金等价物余额")]
    [JsonPropertyName("qcxjjxjdhwye")]
    public decimal? BeginningCashBalance { get; set; }

    [Description("期末现金及现金等价物余额")]
    [JsonPropertyName("qmxjjxjdhwye")]
    public decimal? EndingCashBalance { get; set; }
}
