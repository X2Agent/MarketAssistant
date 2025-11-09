namespace MarketAssistant.Services.StockScreener.Models;

/// <summary>
/// 股票筛选条件
/// </summary>
public class StockScreeningCriteria
{
    /// <summary>
    /// 指标代码（如 "mc", "pettm", "roediluted" 等）
    /// </summary>
    public string Code { get; set; } = string.Empty;

    /// <summary>
    /// 指标显示名称
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 最小值
    /// </summary>
    public decimal? MinValue { get; set; }

    /// <summary>
    /// 最大值
    /// </summary>
    public decimal? MaxValue { get; set; }
}

