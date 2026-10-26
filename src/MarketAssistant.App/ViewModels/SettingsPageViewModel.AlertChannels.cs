using CommunityToolkit.Mvvm.Input;
using MarketAssistant.Applications.AlertCenter;
using MarketAssistant.Applications.AlertCenter.Channels;

namespace MarketAssistant.ViewModels;

/// <summary>
/// 设置页 - 告警推送通道部分（飞书自定义机器人的级别选项与测试推送）。
/// 与主页视图模型拆分为 partial 类，避免设置页单文件继续膨胀。
/// </summary>
public partial class SettingsPageViewModel
{
    /// <summary>告警级别选项（飞书最低推送级别下拉用）。</summary>
    public List<AlertLevel> AlertLevelOptions { get; } =
        [AlertLevel.Info, AlertLevel.Warning, AlertLevel.Critical];

    /// <summary>
    /// 发送飞书测试消息：直接使用页面草稿中的地址与密钥，便于保存前验证配置。
    /// </summary>
    [RelayCommand]
    private async Task TestFeishuWebhookAsync()
    {
        var url = UserSetting.FeishuWebhookUrl?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(url))
        {
            _notificationService.ShowWarning("请先填写飞书 Webhook 地址");
            return;
        }

        if (!FeishuWebhookClient.IsValidWebhookUrl(url, out _))
        {
            _notificationService.ShowWarning("仅支持官方飞书/Lark HTTPS Webhook 地址（如 https://open.feishu.cn/...）");
            return;
        }

        await SafeExecuteAsync(async () =>
        {
            var delivered = await _feishuWebhookClient.SendAsync(
                url,
                UserSetting.FeishuWebhookSecret,
                "【测试】MarketAssistant 告警通道已连通，后续告警将推送到此群。");

            if (delivered)
                _notificationService.ShowSuccess("测试消息已发送，请查看飞书群");
            else
                _notificationService.ShowWarning("测试消息发送失败，请检查地址/密钥与网络");
        }, "发送飞书测试消息");
    }
}
