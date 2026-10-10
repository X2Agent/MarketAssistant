using MarketAssistant.Infrastructure.Core;

namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// 告警抑制纯逻辑（不依赖 IO，便于单元测试）：
/// - 同类告警（同去重键）在合并窗口内只产生一条记录并累计次数，不重复弹窗；
/// - 调用方传入触达抑制标志（休市静默 / 免打扰 / 关闭通知）时，告警只落库不弹窗。
/// </summary>
public sealed class AlertSuppressionPolicy
{
    /// <summary>同类告警合并窗口：窗口内的重复触发并入最近一条记录。</summary>
    public static readonly TimeSpan MergeWindow = TimeSpan.FromMinutes(5);

    /// <summary>单个去重键的合并状态。</summary>
    public sealed record MergeState(DateTime? LastRaisedAtUtc = null)
    {
        public static readonly MergeState Empty = new();
    }

    /// <param name="NewMergeState">该去重键更新后的合并状态（调用方需按 key 保存）。</param>
    /// <param name="IsMerge">是否命中合并窗口（应并入最近一条记录而非新增）。</param>
    /// <param name="ShouldNotify">是否允许弹窗触达。</param>
    public sealed record Decision(
        MergeState NewMergeState,
        bool IsMerge,
        bool ShouldNotify);

    /// <summary>
    /// 评估一条告警是否允许触达。调用方需线程安全地保存 <see cref="Decision.NewMergeState"/>（按去重键）。
    /// </summary>
    /// <param name="alert">待评估的告警。</param>
    /// <param name="mergeState">该去重键当前的合并状态。</param>
    /// <param name="utcNow">当前 UTC 时间（合并窗口判定基准）。</param>
    /// <param name="suppressNotification">
    /// 调用方计算的触达抑制标志（休市静默 / 免打扰时段 / 用户关闭通知）。
    /// 被抑制的告警仍正常落库与合并，但不弹窗。
    /// </param>
    public Decision Evaluate(
        AlertEvent alert, MergeState mergeState, DateTime utcNow, bool suppressNotification = false)
    {
        ArgumentNullException.ThrowIfNull(alert);

        var isMerge = mergeState.LastRaisedAtUtc.HasValue &&
                      utcNow - mergeState.LastRaisedAtUtc.Value < MergeWindow;

        // 合并触发与被抑制的告警都不弹窗
        var shouldNotify = !isMerge && !suppressNotification;

        return new Decision(new MergeState(utcNow), isMerge, shouldNotify);
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