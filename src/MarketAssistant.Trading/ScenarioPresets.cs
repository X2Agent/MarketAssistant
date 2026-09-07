using System.Text.Json;

namespace MarketAssistant.Trading.Models;

/// <summary>
/// 三张场景卡片（UI 向导入口），每张卡片映射到一种引擎策略类型：
/// 智能策略（AISignal）/ 省心定投（DCA）/ 区间网格（GridTrading）。
/// </summary>
public enum ScenarioKind
{
    AISmart,
    DCA,
    Grid
}

/// <summary>
/// 智能策略（AI 信号）场景的自定义参数，序列化后存于 TradingStrategy.CustomParams。
/// 止损/止盈价由 AI 决策动态给出并回写到策略护栏字段（StopLossPrice/TakeProfitPrice），
/// 此处保存的是风险档案预设的兜底百分比与决策约束。
/// </summary>
public class AISignalParams
{
    /// <summary>风险档案（Conservative/Balanced/Aggressive）。</summary>
    public string RiskProfile { get; set; } = "Balanced";

    /// <summary>AI 分析评估间隔（秒）。间隔过短会显著增加 LLM 调用成本。</summary>
    public int AnalysisIntervalSeconds { get; set; } = 600;

    /// <summary>置信度门槛（0-100）：AI 决策置信度低于该值时强制 HOLD。</summary>
    public int ConfidenceThreshold { get; set; } = 65;

    /// <summary>单次开仓仓位上限（占账户总值的百分比）。本地校验强制执行，AI 无法突破。</summary>
    public decimal MaxPositionPercent { get; set; } = 10;

    /// <summary>单次开仓预算（USDT）。实际下单数量 = 预算 × 置信度系数 ÷ 当前价，上限受 MaxPositionPercent 约束。</summary>
    public decimal BudgetUsdt { get; set; }

    /// <summary>兜底止损百分比（%）：AI 未给出止损价时按当前价 × (1 - 百分比) 生成护栏。</summary>
    public decimal StopLossPercent { get; set; } = 8;

    /// <summary>兜底止盈百分比（%）：AI 未给出止盈价时按当前价 × (1 + 百分比) 生成护栏。</summary>
    public decimal TakeProfitPercent { get; set; } = 15;

    /// <summary>出场方式：FixedStop（固定止盈止损）/ TrailingStop（追踪止损）。</summary>
    public string ExitStyle { get; set; } = "FixedStop";

    /// <summary>追踪止损回调百分比（%），仅 TrailingStop 出场方式生效。</summary>
    public decimal TrailingPercent { get; set; } = 5;

    /// <summary>影子模式：AI 决策仅记录日志与统计，不实际下单。</summary>
    public bool ShadowMode { get; set; }

    public ExitStyle ParsedExitStyle =>
        Enum.TryParse<ExitStyle>(ExitStyle, true, out var style) ? style : Models.ExitStyle.FixedStop;

    public RiskProfile ParsedRiskProfile => RiskProfileExtensions.ParseOrDefault(RiskProfile);

    /// <summary>从 CustomParams JSON 解析；为空或格式错误时返回 null（引擎按默认间隔处理）。</summary>
    public static AISignalParams? FromJson(string? customParams)
    {
        if (string.IsNullOrEmpty(customParams))
            return null;

        try
        {
            return JsonSerializer.Deserialize<AISignalParams>(customParams);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// 场景参数预设库：按风险档案给出三张场景卡片的默认参数。
/// UI 创建策略时按档案预填；引擎在策略参数缺失时（如旧版追踪止损）也按档案兜底，
/// 保证安全护栏永不静默失效。
/// </summary>
public static class ScenarioPresets
{
    /// <summary>智能策略（AI 信号）预设参数。</summary>
    public sealed record AISignalPreset(
        int AnalysisIntervalSeconds,
        int ConfidenceThreshold,
        decimal MaxPositionPercent,
        decimal StopLossPercent,
        decimal TakeProfitPercent,
        ExitStyle ExitStyle,
        decimal TrailingPercent);

    /// <summary>区间网格预设参数：区间宽度、格数与破网护栏百分比。</summary>
    public sealed record GridPreset(
        decimal RangePercent,
        int GridCount,
        decimal BreakoutStopLossPercent,
        decimal BreakoutTakeProfitPercent);

    /// <summary>省心定投预设参数：定投间隔与出场护栏。</summary>
    public sealed record DcaPreset(
        int IntervalSeconds,
        decimal DoubleBuyBelowPercent,
        decimal TakeProfitPercent,
        decimal StopLossPercent,
        bool StopLossSellOut);

    /// <summary>默认追踪止损回调百分比（旧版策略参数缺失时的引擎兜底值）。</summary>
    public static decimal GetTrailingPercent(RiskProfile profile) => profile switch
    {
        RiskProfile.Conservative => 3,
        RiskProfile.Aggressive => 8,
        _ => 5
    };

    public static AISignalPreset GetAISignalPreset(RiskProfile profile) => profile switch
    {
        RiskProfile.Conservative => new AISignalPreset(1800, 75, 5, 5, 10, ExitStyle.FixedStop, 3),
        RiskProfile.Aggressive => new AISignalPreset(300, 55, 20, 12, 25, ExitStyle.TrailingStop, 8),
        _ => new AISignalPreset(600, 65, 10, 8, 15, ExitStyle.FixedStop, 5)
    };

    public static GridPreset GetGridPreset(RiskProfile profile) => profile switch
    {
        RiskProfile.Conservative => new GridPreset(6, 10, 3, 5),
        RiskProfile.Aggressive => new GridPreset(25, 15, 8, 12),
        _ => new GridPreset(12, 12, 5, 8)
    };

    public static DcaPreset GetDcaPreset(RiskProfile profile) => profile switch
    {
        RiskProfile.Conservative => new DcaPreset(604800, 10, 10, 15, false),
        RiskProfile.Aggressive => new DcaPreset(86400, 15, 25, 25, true),
        _ => new DcaPreset(86400, 10, 15, 20, false)
    };

    /// <summary>
    /// 由风险档案生成网格参数：围绕参考价（通常为当前价）对称展开区间，
    /// 破网止损/止盈位按档案百分比布置在下界下方/上界上方。
    /// </summary>
    public static GridTradingParams CreateGridParams(
        RiskProfile profile, decimal referencePrice, decimal quantityPerGrid)
    {
        var preset = GetGridPreset(profile);
        var halfRange = referencePrice * preset.RangePercent / 100m / 2m;
        var upper = referencePrice + halfRange;
        var lower = referencePrice - halfRange;

        return new GridTradingParams
        {
            UpperPrice = Math.Round(upper, 8),
            LowerPrice = Math.Round(lower, 8),
            GridCount = preset.GridCount,
            QuantityPerGrid = quantityPerGrid,
            StopLossPrice = Math.Round(lower * (1 - preset.BreakoutStopLossPercent / 100m), 8),
            TakeProfitPrice = Math.Round(upper * (1 + preset.BreakoutTakeProfitPercent / 100m), 8)
        };
    }
}