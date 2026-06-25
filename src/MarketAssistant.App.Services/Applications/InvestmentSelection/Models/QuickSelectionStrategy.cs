namespace MarketAssistant.Applications.InvestmentSelection.Models;

/// <summary>
/// 快速选择策略枚举
/// </summary>
public enum QuickSelectionStrategy
{
    /// <summary>
    /// 价值股/币
    /// </summary>
    ValueInvestment,

    /// <summary>
    /// 成长股/币
    /// </summary>
    GrowthInvestment,

    /// <summary>
    /// 活跃标的
    /// </summary>
    ActiveTrading,

    /// <summary>
    /// 大盘标的
    /// </summary>
    LargeCap,

    /// <summary>
    /// 小盘标的
    /// </summary>
    SmallCap,

    /// <summary>
    /// 高股息/高收益
    /// </summary>
    HighYield
}

/// <summary>
/// 快速选择策略信息
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

