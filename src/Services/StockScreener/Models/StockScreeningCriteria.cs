using System.ComponentModel;

namespace MarketAssistant.Services.StockScreener.Models;

/// <summary>
/// 股票筛选条件
/// </summary>
[Description("单个股票筛选指标及其范围")]
public class StockScreeningCriteria
{
    /// <summary>
    /// 指标代码（如 "mc", "pettm", "roediluted" 等）
    /// </summary>
    [Description("指标代码，如 mc(市值)、pettm(市盈率)、roediluted(ROE)")]
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 指标显示名称
    /// </summary>
    [Description("指标中文名称，如 总市值、市盈率TTM、净资产收益率")]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 最小值
    /// </summary>
    [Description("筛选范围的最小值，无下限时为 null")]
    public decimal? MinValue { get; set; }

    /// <summary>
    /// 最大值
    /// </summary>
    [Description("筛选范围的最大值，无上限时为 null")]
    public decimal? MaxValue { get; set; }
}

