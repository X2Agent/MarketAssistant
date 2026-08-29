using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币在各个主要交易所的成交量分布情况")]
public class VolumeDistribution
{
    [Description("交易所名称（如 Binance）")]
    public string Exchange { get; set; } = string.Empty;

    [Description("24小时成交量（USD价值）")]
    public decimal Volume { get; set; }

    [Description("成交量份额占比（%）")]
    public decimal Percentage { get; set; }

    [Description("支持交易的交易对总数")]
    public int PairCount { get; set; }
}
