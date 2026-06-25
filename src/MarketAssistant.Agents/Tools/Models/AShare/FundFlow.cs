using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.AShare;

/// <summary>
/// 个股资金流向数据（主力 = 特大单 + 大单）
/// </summary>
[Description("个股资金流向数据，主力定义为特大单（成交额≥100万）与大单（≥20万）之和")]
public class FundFlow
{
    [Description("交易日期，格式YYYYMMDD")]
    public int Date { get; set; }

    [Description("主力流入金额：特大单+大单的主动买入成交额合计")]
    public decimal MainFundIn { get; set; }

    [Description("主力流出金额：特大单+大单的主动卖出成交额合计")]
    public decimal MainFundOut { get; set; }

    [Description("主力净流入（正值表示主力资金流入，负值表示流出）")]
    public decimal MainFundDiff { get; set; }

    [Description("超大单净流入（成交额≥100万或成交量≥5000手的订单）")]
    public decimal SuperFundDiff { get; set; }

    [Description("大单净流入（成交额≥20万或成交量≥1000手的订单）")]
    public decimal LargeFundDiff { get; set; }

    [Description("中单净流入（成交额≥4万或成交量≥200手的订单）")]
    public decimal MediumFundDiff { get; set; }

    [Description("小单净流入（中单以下的散户订单）")]
    public decimal LittleFundDiff { get; set; }

    [Description("近3个交易日主力累计净流入")]
    public decimal MainFund3 { get; set; }

    [Description("近5个交易日主力累计净流入")]
    public decimal MainFund5 { get; set; }

    [Description("近10个交易日主力累计净流入")]
    public decimal MainFund10 { get; set; }

    [Description("近20个交易日主力累计净流入")]
    public decimal MainFund20 { get; set; }
}
