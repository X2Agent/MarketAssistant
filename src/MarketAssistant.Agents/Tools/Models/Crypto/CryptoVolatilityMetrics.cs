using System.ComponentModel;

namespace MarketAssistant.Agents.Tools.Models.Crypto;

[Description("加密货币价格波动性与风险评估指标")]
public class CryptoVolatilityMetrics
{
    [Description("代币符号")]
    public string Symbol { get; set; } = string.Empty;

    [Description("年化历史波动率（%）")]
    public decimal AnnualizedVolatility { get; set; }

    [Description("日化波动率（%）")]
    public decimal DailyVolatility { get; set; }

    [Description("平均真实波幅（ATR）")]
    public decimal AverageTrueRange { get; set; }

    [Description("最大回撤百分比（%）")]
    public decimal MaxDrawdown { get; set; }

    [Description("最大回撤起始时间戳（ms）")]
    public long MaxDrawdownStartTime { get; set; }

    [Description("最大回撤结束/最低点时间戳（ms）")]
    public long MaxDrawdownEndTime { get; set; }

    [Description("夏普比率（Sharpe Ratio，衡量风险调整后的收益，数值越高越好）")]
    public decimal? SharpeRatio { get; set; }

    [Description("统计分析周期（天数）")]
    public int PeriodDays { get; set; }

    [Description("价格标准差")]
    public decimal StandardDeviation { get; set; }

    [Description("日均收益率（%）")]
    public decimal AverageReturn { get; set; }
}
