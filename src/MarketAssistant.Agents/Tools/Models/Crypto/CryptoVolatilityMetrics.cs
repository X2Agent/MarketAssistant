using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

/// <summary>
/// 加密货币波动性指标
/// </summary>
[Description("加密货币价格波动性与风险评估指标")]
public class CryptoVolatilityMetrics
{
    /// <summary>
    /// 交易对符号
    /// </summary>
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    /// <summary>
    /// 历史波动率（年化，%）
    /// </summary>
    [Description("年化历史波动率（%）")]
    public decimal AnnualizedVolatility { get; set; }

    /// <summary>
    /// 日波动率（%）
    /// </summary>
    [Description("日化波动率（%）")]
    public decimal DailyVolatility { get; set; }

    /// <summary>
    /// 平均真实波幅（ATR）
    /// </summary>
    [Description("平均真实波幅（ATR）")]
    public decimal AverageTrueRange { get; set; }

    /// <summary>
    /// 最大回撤（%）
    /// </summary>
    [Description("最大回撤百分比（%）")]
    public decimal MaxDrawdown { get; set; }

    /// <summary>
    /// 最大回撤起始时间
    /// </summary>
    [Description("最大回撤起始时间戳（ms）")]
    public long MaxDrawdownStartTime { get; set; }

    /// <summary>
    /// 最大回撤结束时间
    /// </summary>
    [Description("最大回撤结束/最低点时间戳（ms）")]
    public long MaxDrawdownEndTime { get; set; }

    /// <summary>
    /// 夏普比率（Sharpe Ratio）
    /// </summary>
    /// <remarks>
    /// 衡量风险调整后收益，数值越高越好。
    /// </remarks>
    [Description("夏普比率（Sharpe Ratio，衡量风险调整后的收益，数值越高越好）")]
    public decimal? SharpeRatio { get; set; }

    /// <summary>
    /// 统计周期（天数）
    /// </summary>
    [Description("统计分析周期（天数）")]
    public int PeriodDays { get; set; }

    /// <summary>
    /// 价格标准差
    /// </summary>
    [Description("价格标准差")]
    public decimal StandardDeviation { get; set; }

    /// <summary>
    /// 平均收益率（%）
    /// </summary>
    [Description("日均收益率（%）")]
    public decimal AverageReturn { get; set; }
}