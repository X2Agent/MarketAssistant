using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 交易量分布（各交易所占比）
/// </summary>
[Description("加密货币在各个主要交易所的成交量分布情况")]
public class VolumeDistribution
{
    /// <summary>
    /// 交易所名称
    /// </summary>
    [Description("交易所名称（如 Binance）")]
    public string Exchange { get; set; } = string.Empty;

    /// <summary>
    /// 交易量（USD）
    /// </summary>
    [Description("24小时成交量（USD价值）")]
    public decimal Volume { get; set; }

    /// <summary>
    /// 占总交易量百分比
    /// </summary>
    [Description("成交量份额占比（%）")]
    public decimal Percentage { get; set; }

    /// <summary>
    /// 交易对数量
    /// </summary>
    [Description("支持交易的交易对总数")]
    public int PairCount { get; set; }
}