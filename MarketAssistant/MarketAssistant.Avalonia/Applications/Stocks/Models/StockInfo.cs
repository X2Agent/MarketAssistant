namespace MarketAssistant.Applications.Stocks.Models;

/// <summary>
/// 股票基本信息类
/// </summary>
public class StockInfo
{
    /// <summary>
    /// 股票代码
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 股票名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 市场代码
    /// </summary>
    public string Market { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格
    /// </summary>
    public string CurrentPrice { get; set; } = string.Empty;

    /// <summary>
    /// 涨跌幅
    /// </summary>
    public string ChangePercentage { get; set; } = string.Empty;

    /// <summary>
    /// 所属板块名称
    /// </summary>
    public string SectorName { get; set; } = string.Empty;

    /// <summary>
    /// 获取完整股票代码（市场+代码）
    /// </summary>
    public string FullCode => $"{Market}{Code}".ToLower();
}