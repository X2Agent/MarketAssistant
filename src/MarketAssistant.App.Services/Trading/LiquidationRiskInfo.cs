namespace MarketAssistant.Services.Trading;

/// <summary>
/// 合约持仓强平风险快照（UI 展示与告警判定共用）。
/// </summary>
public sealed record LiquidationRiskInfo(
    string Symbol,
    bool IsShort,
    decimal MarkPrice,
    decimal LiquidationPrice,
    /// <summary>标记价距强平价的百分比距离（越大越安全）。</summary>
    decimal DistancePercent,
    /// <summary>风险级别：None=安全 / Warning=接近 / Critical=紧急。</summary>
    LiquidationRiskLevel Level)
{
    /// <summary>持仓方向展示文本。</summary>
    public string DirectionText => IsShort ? "空头" : "多头";
}

/// <summary>
/// 强平风险级别。
/// </summary>
public enum LiquidationRiskLevel
{
    None,

    Warning,

    Critical
}
