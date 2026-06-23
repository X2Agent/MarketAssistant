namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// A股筛选结果（雪球 HTTP API 数据源，支持全部 38 个筛选指标）
/// </summary>
public class ScreenerStockInfo : ScreenerAssetInfo
{
    /// <summary>
    /// 当日量比
    /// </summary>
    public decimal VolumeRatio { get; set; }

    /// <summary>
    /// 当日换手率(%)
    /// </summary>
    public decimal Tr { get; set; }

    /// <summary>
    /// 市盈率TTM
    /// </summary>
    public decimal PeTtm { get; set; }

    /// <summary>
    /// 市盈率LYR
    /// </summary>
    public decimal PeLyr { get; set; }

    /// <summary>
    /// 市净率MRQ
    /// </summary>
    public decimal Pb { get; set; }

    /// <summary>
    /// 市销率(倍)
    /// </summary>
    public decimal Psr { get; set; }

    /// <summary>
    /// 净资产收益率(%)
    /// </summary>
    public decimal RoeDiluted { get; set; }

    /// <summary>
    /// 每股净资产
    /// </summary>
    public decimal Bps { get; set; }

    /// <summary>
    /// 每股收益
    /// </summary>
    public decimal Eps { get; set; }

    /// <summary>
    /// 净利润（元）
    /// </summary>
    public decimal NetProfit { get; set; }

    /// <summary>
    /// 营业收入（元）
    /// </summary>
    public decimal TotalRevenue { get; set; }

    /// <summary>
    /// 股息收益率(%)
    /// </summary>
    public decimal DyL { get; set; }

    /// <summary>
    /// 净利润同比增长(%)
    /// </summary>
    public decimal Npay { get; set; }

    /// <summary>
    /// 营业收入同比增长(%)
    /// </summary>
    public decimal Oiy { get; set; }

    /// <summary>
    /// 总资产报酬率(%)
    /// </summary>
    public decimal Niota { get; set; }

    /// <summary>
    /// 累计关注人数
    /// </summary>
    public decimal Follow { get; set; }

    /// <summary>
    /// 累计讨论次数
    /// </summary>
    public decimal Tweet { get; set; }

    /// <summary>
    /// 累计交易分享数
    /// </summary>
    public decimal Deal { get; set; }

    /// <summary>
    /// 一周新增关注
    /// </summary>
    public decimal Follow7d { get; set; }

    /// <summary>
    /// 一周新增讨论数
    /// </summary>
    public decimal Tweet7d { get; set; }

    /// <summary>
    /// 一周新增交易分享数
    /// </summary>
    public decimal Deal7d { get; set; }

    /// <summary>
    /// 一周关注增长率(%)
    /// </summary>
    public decimal Follow7dPct { get; set; }

    /// <summary>
    /// 一周讨论增长率(%)
    /// </summary>
    public decimal Tweet7dPct { get; set; }

    /// <summary>
    /// 一周交易分享增长率(%)
    /// </summary>
    public decimal Deal7dPct { get; set; }

    /// <summary>
    /// 近5日涨跌幅(%)
    /// </summary>
    public decimal Pct5 { get; set; }

    /// <summary>
    /// 近10日涨跌幅(%)
    /// </summary>
    public decimal Pct10 { get; set; }

    /// <summary>
    /// 近20日涨跌幅(%)
    /// </summary>
    public decimal Pct20 { get; set; }

    /// <summary>
    /// 近60日涨跌幅(%)
    /// </summary>
    public decimal Pct60 { get; set; }

    /// <summary>
    /// 近120日涨跌幅(%)
    /// </summary>
    public decimal Pct120 { get; set; }

    /// <summary>
    /// 近250日涨跌幅(%)
    /// </summary>
    public decimal Pct250 { get; set; }

    /// <summary>
    /// 年初至今涨跌幅(%)
    /// </summary>
    public decimal PctCurrentYear { get; set; }

    /// <summary>
    /// 当日振幅(%)
    /// </summary>
    public decimal ChgPct { get; set; }
}
