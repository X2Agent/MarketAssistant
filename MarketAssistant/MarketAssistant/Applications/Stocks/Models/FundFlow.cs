namespace MarketAssistant.Applications.Stocks.Models;

public class FundFlow
{
    /// <summary>
    /// 主力流入 (万元)
    /// </summary>
    public float MainFundIn { get; set; }

    /// <summary>
    /// 主力流出 (万元)
    /// </summary>
    public float MainFundOut { get; set; }

    /// <summary>
    /// 主力净流入 (万元)
    /// </summary>
    public float MainFundDiff { get; set; }

    /// <summary>
    /// 超大单流入 (万元)
    /// </summary>
    public float SuperFundDiff { get; set; }

    /// <summary>
    /// 大单流入 (万元)
    /// </summary>
    public float LargeFundDiff { get; set; }

    /// <summary>
    /// 中单流入 (万元)
    /// <summary>
    public float MediumFundDiff { get; set; }

    /// <summary>
    /// 小单流入 (万元)
    /// <summary>
    public float LittleFundDiff { get; set; }

    /// <summary>
    /// 3日主力净流入 (万元)
    /// </summary>
    public float MainFund3 { get; set; }

    /// <summary>
    /// 5日主力净流入 (万元)
    /// </summary>
    public float MainFund5 { get; set; }

    /// <summary>
    /// 10日主力净流入 (万元)
    /// </summary>
    public float MainFund10 { get; set; }

    /// <summary>
    /// 20日主力净流入 (万元)
    /// </summary>
    public float MainFund20 { get; set; }

    /// <summary>
    /// 日期
    /// </summary>
    public int Date { get; set; }
}
