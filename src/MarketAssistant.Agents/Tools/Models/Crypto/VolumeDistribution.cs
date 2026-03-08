namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 交易量分布（各交易所占比�?
/// </summary>
public class VolumeDistribution
{
    /// <summary>
    /// 交易所名称
    /// </summary>
    public string Exchange { get; set; } = string.Empty;

    /// <summary>
    /// 交易量（USD�?
    /// </summary>
    public decimal Volume { get; set; }

    /// <summary>
    /// 占总交易量百分�?
    /// </summary>
    public decimal Percentage { get; set; }

    /// <summary>
    /// 交易对数�?
    /// </summary>
    public int PairCount { get; set; }
}
