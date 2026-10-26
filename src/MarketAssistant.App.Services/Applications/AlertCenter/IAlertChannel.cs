namespace MarketAssistant.Applications.AlertCenter;

/// <summary>
/// 告警触达通道（桌面通知之外的进程外出口，如飞书自定义机器人）。
/// </summary>
/// <remarks>
/// 通道只负责"已决定触达"的投递：合并窗口、免打扰时段、关闭通知等抑制逻辑由
/// <see cref="AlertSuppressionPolicy"/> 与 <see cref="AlertCenterService"/> 统一判定，
/// 通道不重复实现，也不得绕过它们直接推送。
/// 实现必须自行隔离异常——单通道失败只记录日志，不得影响告警主链与其它通道。
/// </summary>
public interface IAlertChannel
{
    /// <summary>通道名称（日志与测试推送用）。</summary>
    string Name { get; }

    /// <summary>该通道接收的最低告警级别（低于此级别的告警不投递）。</summary>
    AlertLevel MinimumLevel { get; }

    /// <summary>投递一条告警。</summary>
    Task SendAsync(AlertEvent alert, CancellationToken cancellationToken = default);
}
