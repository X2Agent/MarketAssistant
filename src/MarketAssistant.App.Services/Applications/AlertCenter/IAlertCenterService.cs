namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// 统一告警中心：收敛价格 / 风险 / 信号三类告警的产出、抑制与触达。
/// </summary>
public interface IAlertCenterService
{
    /// <summary>新告警产生（含合并更新）时触发，UI 据此刷新历史与未读角标。</summary>
    event Action<AlertEvent>? AlertRaised;

    /// <summary>告警集合发生变化（合并、解除、已读）时触发。</summary>
    event Action? AlertsChanged;

    /// <summary>初始化数据库表结构，应在应用启动时调用。</summary>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 上报一条告警。内部执行同类合并窗口（5 分钟）、触达抑制（休市静默 / 免打扰时段 /
    /// 用户关闭通知，Critical 穿透免打扰）等抑制逻辑，
    /// 持久化后按级别经各触达通道（桌面通知、飞书等）分发，并维护确认级交易联动门（IAlertGate）。
    /// </summary>
    Task RaiseAlertAsync(AlertEvent alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// 声明某条确认级告警的触发条件已解除（如价格离开告警区间），
    /// 释放该标的的交易联动门，使后续 AI 信号恢复正常决策路径。
    /// </summary>
    Task RaiseAlertClearedAsync(
        AlertSource source, MarketType marketType, string symbol, string title,
        CancellationToken cancellationToken = default);

    /// <summary>最近的历史告警（新→旧）。</summary>
    Task<IReadOnlyList<AlertEvent>> GetRecentAlertsAsync(int limit = 200, CancellationToken cancellationToken = default);

    /// <summary>未读告警数量（导航角标用）。</summary>
    Task<int> GetUnreadCountAsync(CancellationToken cancellationToken = default);

    /// <summary>全部标记已读。</summary>
    Task MarkAllReadAsync(CancellationToken cancellationToken = default);
}