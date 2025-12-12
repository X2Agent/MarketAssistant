namespace MarketAssistant.Applications.StockSelection.Models;

/// <summary>
/// 快速选股策略枚举
/// </summary>
public enum QuickSelectionStrategy
{
    /// <summary>
    /// 价值股
    /// </summary>
    ValueStocks,

    /// <summary>
    /// 成长股
    /// </summary>
    GrowthStocks,

    /// <summary>
    /// 活跃股
    /// </summary>
    ActiveStocks,

    /// <summary>
    /// 大盘股
    /// </summary>
    LargeCap,

    /// <summary>
    /// 小盘股
    /// </summary>
    SmallCap,

    /// <summary>
    /// 高股息股
    /// </summary>
    Dividend
}

/// <summary>
/// 快速选股策略信息
/// </summary>
public class QuickSelectionStrategyInfo
{
    /// <summary>
    /// 策略类型
    /// </summary>
    public QuickSelectionStrategy Strategy { get; set; }

    /// <summary>
    /// 策略名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 策略图标
    /// </summary>
    public string Icon { get; set; } = string.Empty;

    /// <summary>
    /// 策略描述
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 适用场景
    /// </summary>
    public string Scenario { get; set; } = string.Empty;

    /// <summary>
    /// 风险等级
    /// </summary>
    public string RiskLevel { get; set; } = string.Empty;
}

