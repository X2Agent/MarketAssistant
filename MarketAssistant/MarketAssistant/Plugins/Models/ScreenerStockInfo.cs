namespace MarketAssistant.Plugins.Models;

/// <summary>
/// 雪球网股票筛选结果实体
/// </summary>
public class ScreenerStockInfo
{
    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 股票代码（如：SZ300316）
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前价
    /// </summary>
    public decimal Current { get; set; }

    /// <summary>
    /// 当日涨跌幅(%)
    /// </summary>
    public decimal Pct { get; set; }

    /// <summary>
    /// 当日成交额
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 总市值（元）
    /// </summary>
    public decimal Mc { get; set; }

    /// <summary>
    /// 流通市值（元）
    /// </summary>
    public decimal Fmc { get; set; }

    /// <summary>
    /// 本日成交量(万)
    /// </summary>
    public decimal Volume { get; set; }

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

    /// <summary>
    /// 动态数据字典，存储其他指标
    /// </summary>
    public Dictionary<string, string> ExtraData { get; set; } = new();
}
