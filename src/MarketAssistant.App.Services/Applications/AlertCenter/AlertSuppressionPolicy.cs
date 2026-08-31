namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// 告警抑制纯逻辑（不依赖 IO，便于单元测试）：
/// - 同类告警（同去重键）在合并窗口内只产生一条记录并累计次数，不重复弹窗；
/// - 非 Critical 告警受全局每小时配额限制（配额状态全局共享，而非按去重键独立），
///   超限只落库不弹窗；Critical 不受配额限制。
/// </summary>
public sealed class AlertSuppressionPolicy
{
    /// <summary>同类告警合并窗口：窗口内的重复触发并入最近一条记录。</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan QuotaWindow = TimeSpan.FromHours(1);

    /// <summary>单个去重键的合并状态。</summary>
    public sealed record MergeState(DateTime? LastRaisedAtUtc = null)
    {
        public static readonly MergeState Empty = new();
    }

    /// <summary>全局每小时弹窗配额状态（固定窗口，随时间滚动重置）。</summary>
    public sealed record QuotaState(DateTime WindowStartUtc = default, int Count = 0)
    {
        public static readonly QuotaState Empty = new();
    }

    /// <param name="NewMergeState">该去重键更新后的合并状态（调用方需按 key 保存）。</param>
    /// <param name="NewQuotaState">更新后的全局配额状态（调用方需保存）。</param>
    /// <param name="IsMerge">是否命中合并窗口（应并入最近一条记录而非新增）。</param>
    /// <param name="ShouldNotify">是否允许弹窗触达。</param>
    public sealed record Decision(
        MergeState NewMergeState,
        QuotaState NewQuotaState,
        bool IsMerge,
        bool ShouldNotify);

    /// <summary>
    /// 评估一条告警是否允许触达。调用方需线程安全地保存 <see cref="Decision.NewMergeState"/>
    /// （按去重键）与 <see cref="Decision.NewQuotaState"/>（全局）。
    /// </summary>
    public Decision Evaluate(
        AlertEvent alert, MergeState mergeState, QuotaState quotaState, int hourlyQuota, DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var isMerge = mergeState.LastRaisedAtUtc.HasValue &&
                      utcNow - mergeState.LastRaisedAtUtc.Value < MergeWindow;

        // 配额固定窗口：距窗口起点超过 1 小时则重新计数
        var windowStart = quotaState.WindowStartUtc;
        var count = quotaState.Count;
        if (windowStart == default || utcNow - windowStart >= QuotaWindow)
        {
            windowStart = utcNow;
            count = 0;
        }

        var canNotify = alert.Level == AlertLevel.Critical || count < hourlyQuota;

        // 合并触发不新增记录也不弹窗，因此不消耗配额；可触达的新告警才计数
        if (!isMerge && canNotify)
            count++;

        return new Decision(
            new MergeState(utcNow),
            new QuotaState(windowStart, count),
            isMerge,
            isMerge ? false : canNotify);
    }
}