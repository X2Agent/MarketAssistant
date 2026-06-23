namespace MarketAssistant.Agents.Tools.Models.AShare;

public class FundFlow
{
    /// <summary>
    /// 主力流入 (万元)
    /// </summary>
    public decimal MainFundIn { get; set; }

    /// <summary>
    /// 主力流出 (万元)
    /// </summary>
    public decimal MainFundOut { get; set; }

    /// <summary>
    /// 主力净流入 (万元)
    /// </summary>
    public decimal MainFundDiff { get; set; }

    /// <summary>
    /// 超大单流入 (万元)
    /// </summary>
    public decimal SuperFundDiff { get; set; }

    /// <summary>
    /// 大单流入 (万元)
    /// </summary>
    public decimal LargeFundDiff { get; set; }

    /// <summary>
    /// 中单流入 (万元)
    /// </summary>
    public decimal MediumFundDiff { get; set; }

    /// <summary>
    /// 小单流入 (万元)
    /// </summary>
    public decimal LittleFundDiff { get; set; }

    /// <summary>
    /// 3日主力净流入 (万元)
    /// </summary>
    public decimal MainFund3 { get; set; }

    /// <summary>
    /// 5日主力净流入 (万元)
    /// </summary>
    public decimal MainFund5 { get; set; }

    /// <summary>
    /// 10日主力净流入 (万元)
    /// </summary>
    public decimal MainFund10 { get; set; }

    /// <summary>
    /// 20日主力净流入 (万元)
    /// </summary>
    public decimal MainFund20 { get; set; }

    /// <summary>
    /// 日期
    /// </summary>
    public int Date { get; set; }
}
