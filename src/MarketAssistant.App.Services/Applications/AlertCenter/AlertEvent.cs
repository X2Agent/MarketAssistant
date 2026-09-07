using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// 告警级别：Info（信号/状态变化）、Warning（风险预警）、Critical（紧急，需立即关注）。
/// </summary>
public enum AlertLevel
{
    Info,

    Warning,

    Critical
}

/// <summary>
/// 告警来源：价格告警规则、风险评估器、AI 信号、系统内部。
/// </summary>
public enum AlertSource
{
    PriceAlert,

    Risk,

    Signal,

    System
}

/// <summary>
/// 告警对交易的影响强度。RequireConfirmation 表示该标的的 AI 交易信号在告警触发期间
/// 升级为需人工确认（告警事件 ≠ 交易指令，不自动下单）。
/// </summary>
public enum AlertTradingImpact
{
    None,

    RequireConfirmation
}

/// <summary>
/// 统一告警事件。除 IsRead/OccurrenceCount 外均为不可变快照。
/// </summary>
public sealed class AlertEvent
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public MarketType MarketType { get; init; } = MarketType.AShare;

    /// <summary>标的代码（系统级告警可为空）。</summary>
    public string Symbol { get; init; } = string.Empty;

    public AlertLevel Level { get; init; } = AlertLevel.Warning;

    public AlertSource Source { get; init; }

    public string Title { get; init; } = string.Empty;

    public string Content { get; init; } = string.Empty;

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public bool IsRead { get; set; }

    public AlertTradingImpact TradingImpact { get; init; } = AlertTradingImpact.None;

    /// <summary>合并窗口内重复触发的累计次数（首次为 1）。</summary>
    public int OccurrenceCount { get; set; } = 1;

    /// <summary>
    /// 去重键：同类告警（同来源/市场/标的/标题）在合并窗口内只产生一条记录。
    /// </summary>
    public string DedupeKey => BuildDedupeKey(Source, MarketType, Symbol, Title);

    public static string BuildDedupeKey(AlertSource source, MarketType marketType, string symbol, string title) =>
        $"{source}:{marketType}:{NormalizeSymbol(symbol)}:{title}";

    public static string NormalizeSymbol(string symbol) =>
        symbol?.Trim().ToUpperInvariant() ?? string.Empty;

    // ---- 展示辅助（UI 历史列表绑定用）----

    /// <summary>本地时间展示文本。</summary>
    public string LocalTimeText => CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>级别文本。</summary>
    public string LevelText => Level switch
    {
        AlertLevel.Critical => "紧急",
        AlertLevel.Warning => "警告",
        _ => "提示"
    };

    /// <summary>合并次数展示文本（首次触发不显示）。</summary>
    public string OccurrenceText => OccurrenceCount > 1 ? $"已发生 {OccurrenceCount} 次" : string.Empty;

    /// <summary>标的展示文本（系统级告警无标的）。</summary>
    public string SymbolText => string.IsNullOrWhiteSpace(Symbol) ? "系统" : Symbol;

    public bool IsLevelInfo => Level == AlertLevel.Info;

    public bool IsLevelWarning => Level == AlertLevel.Warning;

    public bool IsLevelCritical => Level == AlertLevel.Critical;
}