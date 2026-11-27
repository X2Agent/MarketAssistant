namespace MarketAssistant.Services.Trading;

/// <summary>
/// 合约持仓强平风险快照（UI 展示与告警判定共用）。
/// <see cref="DistancePercent"/> 为标记价距强平价的百分比距离（越大越安全）；
/// <see cref="Level"/> 为风险级别：None=安全 / Warning=接近 / Critical=紧急。
/// </summary>
public sealed record LiquidationRiskInfo(
    string Symbol,
    bool IsShort,
    decimal MarkPrice,
    decimal LiquidationPrice,
    decimal DistancePercent,
    LiquidationRiskLevel Level)
{
    /// <summary>持仓方向展示文本。</summary>
    public string DirectionText => IsShort ? "空头" : "多头";

    /// <summary>级别展示文本；安全级为空，供标签按文本是否存在决定显隐。</summary>
    public string LevelText => Level switch
    {
        LiquidationRiskLevel.Critical => "紧急",
        LiquidationRiskLevel.Warning => "接近",
        _ => string.Empty
    };

    /// <summary>是否紧急级（标签由警告色升级为危险色）。</summary>
    public bool IsCritical => Level == LiquidationRiskLevel.Critical;
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
