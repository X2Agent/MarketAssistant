using MarketAssistant.Agents;

namespace MarketAssistant.Agents;

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