namespace MarketAssistant.Applications.Stocks.Models;

public class HotStock
{
    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 涨跌幅
    /// </summary>
    public string ChangePercentage { get; set; } = string.Empty;

    /// <summary>
    /// 所属板块名称
    /// </summary>
    public string SectorName { get; set; } = string.Empty;

    /// <summary>
    /// 市场代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 现价
    /// </summary>
    public string CurrentPrice { get; set; } = string.Empty;

    /// <summary>
    /// 市场缩写
    /// </summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>
    /// 排名变化
    /// </summary>
    public string RankChange { get; set; } = string.Empty;

    /// <summary>
    /// 市场类型
    /// </summary>
    public string MarketType { get; set; } = string.Empty;

    /// <summary>
    /// 综合热度
    /// </summary>
    public string HeatIndex { get; set; } = string.Empty;
}