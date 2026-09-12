using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// 告警抑制纯逻辑（不依赖 IO，便于单元测试）：
/// - 同类告警（同去重键）在合并窗口内只产生一条记录并累计次数，不重复弹窗；
/// - 非 Critical 告警受全局每小时配额限制（配额状态全局共享，而非按去重键独立），
///   超限只落库不弹窗；Critical 不受配额限制；
/// - 调用方传入触达抑制标志（休市静默/免打扰/关闭通知）时，告警只落库不弹窗且不消耗配额，
///   避免静默期噪声挤占交易时段配额。
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
    /// <param name="alert">待评估的告警。</param>
    /// <param name="mergeState">该去重键当前的合并状态。</param>
    /// <param name="quotaState">当前全局配额状态。</param>
    /// <param name="hourlyQuota">每小时弹窗配额上限。</param>
    /// <param name="utcNow">当前 UTC 时间（固定窗口与合并窗口判定基准）。</param>
    /// <param name="suppressNotification">
    /// 调用方计算的触达抑制标志（休市静默 / 免打扰时段 / 用户关闭通知）。
    /// 被抑制的告警仍正常落库与合并，但不消耗每小时配额——弹窗本就不会发生，
    /// 不应挤占后续交易时段的配额。
    /// </param>
    public Decision Evaluate(
        AlertEvent alert, MergeState mergeState, QuotaState quotaState, int hourlyQuota,
        DateTime utcNow, bool suppressNotification = false)
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

        var canNotify = !suppressNotification &&
                        (alert.Level == AlertLevel.Critical || count < hourlyQuota);

        // 合并触发与静默告警都不弹窗，因此不消耗配额；可触达的新告警才计数
        if (!isMerge && canNotify)
            count++;

        return new Decision(
            new MergeState(utcNow),
            new QuotaState(windowStart, count),
            isMerge,
            isMerge ? false : canNotify);
    }

    /// <summary>
    /// A 股价格类告警的休市静默判定：非交易时段只落库不弹窗（休市行情无变化，弹窗无行动价值）。
    /// Critical 级别例外——确认级价格告警需即时触达并驱动交易联动门。
    /// </summary>
    public static bool IsSilencedByTradingSession(AlertEvent alert, bool isTradingSession)
    {
        ArgumentNullException.ThrowIfNull(alert);

        return !isTradingSession
               && alert.MarketType == MarketType.AShare
               && alert.Source == AlertSource.PriceAlert
               && alert.Level != AlertLevel.Critical;
    }
}