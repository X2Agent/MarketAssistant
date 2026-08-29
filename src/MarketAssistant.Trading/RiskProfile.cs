namespace MarketAssistant.Trading.Models;

/// <summary>
/// 风险档案：为三张场景卡片（智能策略 / 省心定投 / 区间网格）提供统一的参数激进程度分层。
/// 档案值随策略持久化在 CustomParams（riskProfile 字段），引擎与 AI 决策均据此取默认护栏参数。
/// </summary>
public enum RiskProfile
{
    Conservative,
    Balanced,
    Aggressive
}

/// <summary>
/// 智能策略（AI 信号）的出场方式：固定止盈止损位，或追踪止损（回落/反弹即离场）。
/// </summary>
public enum ExitStyle
{
    FixedStop,
    TrailingStop
}

/// <summary>
/// 风险档案扩展方法：显示名称、描述与解析回退。
/// </summary>
public static class RiskProfileExtensions
{
    public static string GetDisplayName(this RiskProfile profile) => profile switch
    {
        RiskProfile.Conservative => "保守",
        RiskProfile.Aggressive => "进取",
        _ => "稳健"
    };

    public static string GetDescription(this RiskProfile profile) => profile switch
    {
        RiskProfile.Conservative => "仓位小、护栏收紧、信号要求更严格，适合初次接触自动交易",
        RiskProfile.Aggressive => "仓位更大、容忍更深回撤，追求更高收益弹性",
        _ => "仓位与护栏均衡，适合大多数用户"
    };

    /// <summary>
    /// 从 CustomParams 的 riskProfile 字符串解析档案，无法识别时回退稳健档。
    /// </summary>
    public static RiskProfile ParseOrDefault(string? value) => value?.ToLowerInvariant() switch
    {
        "conservative" => RiskProfile.Conservative,
        "aggressive" => RiskProfile.Aggressive,
        _ => RiskProfile.Balanced
    };
}