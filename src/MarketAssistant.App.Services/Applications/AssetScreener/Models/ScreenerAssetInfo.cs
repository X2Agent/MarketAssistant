namespace MarketAssistant.Applications.AssetScreener.Models;

/// <summary>
/// 资产筛选结果基类，包含所有市场共有的字段
/// </summary>
public class ScreenerAssetInfo
{
    /// <summary>
    /// 资产名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 资产代码
    /// </summary>
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前价格
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
    /// 总市值
    /// </summary>
    public decimal Mc { get; set; }

    /// <summary>
    /// 流通/完全稀释市值
    /// </summary>
    public decimal Fmc { get; set; }

    /// <summary>
    /// 成交量
    /// </summary>
    public decimal Volume { get; set; }
}
