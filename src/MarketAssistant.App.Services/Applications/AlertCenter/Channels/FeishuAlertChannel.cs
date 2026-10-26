using System.Text;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.AlertCenter.Channels;

/// <summary>
/// 飞书告警通道：把通过抑制判定的告警推送到飞书自定义机器人。
/// 未配置 / 未启用时不投递（静默跳过），不影响桌面通知路径。
/// </summary>
public sealed class FeishuAlertChannel(
    FeishuWebhookClient client,
    IUserSettingService userSettingService,
    ILogger<FeishuAlertChannel> logger) : IAlertChannel
{
    /// <summary>通道名称。</summary>
    public const string ChannelName = "飞书";

    public string Name => ChannelName;

    /// <summary>用户可配置最低推送级别（默认仅警告及以上，避免提示类告警刷屏）。</summary>
    public AlertLevel MinimumLevel => userSettingService.CurrentSetting.FeishuMinAlertLevel;

    public async Task SendAsync(AlertEvent alert, CancellationToken cancellationToken = default)
    {
        var settings = userSettingService.CurrentSetting;
        if (!settings.FeishuWebhookEnabled || string.IsNullOrWhiteSpace(settings.FeishuWebhookUrl))
            return;

        var delivered = await client
            .SendAsync(settings.FeishuWebhookUrl, settings.FeishuWebhookSecret, BuildMessage(alert), cancellationToken)
            .ConfigureAwait(false);

        if (!delivered)
            logger.LogWarning("飞书告警投递失败: {AlertId} {Title}", alert.Id, alert.Title);
    }

    /// <summary>告警文本：级别 + 标题 + 内容 + 标的/市场/时间/合并次数。</summary>
    internal static string BuildMessage(AlertEvent alert)
    {
        var builder = new StringBuilder();
        builder.Append('【').Append(LevelText(alert.Level)).Append("】").AppendLine(alert.Title);

        if (!string.IsNullOrWhiteSpace(alert.Content))
            builder.AppendLine(alert.Content);

        builder.Append("标的：").Append(alert.SymbolText)
            .Append("（").Append(alert.MarketTypeText).Append('）');
        builder.Append(" · 时间：").Append(alert.LocalTimeText);

        if (alert.OccurrenceCount > 1)
            builder.Append(" · 已发生 ").Append(alert.OccurrenceCount).Append(" 次");

        return builder.ToString().TrimEnd();
    }

    private static string LevelText(AlertLevel level) => level switch
    {
        AlertLevel.Critical => "紧急",
        AlertLevel.Warning => "警告",
        _ => "提示"
    };
}
