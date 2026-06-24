using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 多空比历史数据
/// </summary>
[Description("加密货币永续合约的多空持仓比率历史趋势")]
public class LongShortRatioHistory
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    [Description("交易对符号")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 当前最新多头占比
    /// </summary>
    [Description("当前最新多头仓位账户占比（%）")]
    public decimal CurrentLongRatio { get; set; }

    /// <summary>
    /// 当前最新空头占比
    /// </summary>
    [Description("当前最新空头仓位账户占比（%）")]
    public decimal CurrentShortRatio { get; set; }

    /// <summary>
    /// 当前多空比率（Long/Short）
    /// </summary>
    [Description("当前多空人数或持仓比率")]
    public decimal CurrentRatio { get; set; }

    /// <summary>
    /// 平均多空比率
    /// </summary>
    [Description("周期平均多空比率")]
    public decimal AverageRatio { get; set; }

    /// <summary>
    /// 历史多空比数据点（按时间倒序，最新的在前）
    /// </summary>
    [Description("历史多空比变化明细数据列表")]
    public List<LongShortRatioPoint> History { get; set; } = [];
}

/// <summary>
/// 单个多空比数据点
/// </summary>
[Description("单笔历史多空比数据点")]
public class LongShortRatioPoint
{
    /// <summary>
    /// 多头占比
    /// </summary>
    [Description("多头占比（%）")]
    public decimal LongRatio { get; set; }

    /// <summary>
    /// 空头占比
    /// </summary>
    [Description("空头占比（%）")]
    public decimal ShortRatio { get; set; }

    /// <summary>
    /// 多空比率（Long/Short）
    /// </summary>
    [Description("多空比率")]
    public decimal Ratio { get; set; }

    /// <summary>
    /// 数据时间戳（Unix 毫秒）
    /// </summary>
    [Description("数据记录时间戳（ms）")]
    public long Timestamp { get; set; }
}