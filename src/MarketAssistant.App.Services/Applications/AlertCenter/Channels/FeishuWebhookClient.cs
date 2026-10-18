using System.Text;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Applications.AlertCenter.Channels;

/// <summary>
/// 飞书自定义机器人 webhook 客户端。经命名 HttpClient "Feishu" 消费宿主层的
/// 超时与弹性策略，不自行包裹重试框架。
/// </summary>
public sealed class FeishuWebhookClient(
    IHttpClientFactory httpClientFactory,
    ILogger<FeishuWebhookClient> logger)
{
    /// <summary>命名 HttpClient 名称（弹性管线在宿主 AddHttpClientsCore 注册）。</summary>
    public const string HttpClientName = "Feishu";

    private static readonly string[] AllowedHostSuffixes =
    [
        ".feishu.cn",
        ".larksuite.com",
        ".larkoffice.com"
    ];

    /// <summary>
    /// 校验飞书/Lark 自定义机器人 Webhook 地址是否合法：
    /// 仅允许 HTTPS 协议且主机名属于官方飞书/Lark 域名，防止 SSRF 或敏感交易告警外泄至非预期端点。
    /// </summary>
    public static bool IsValidWebhookUrl(string? webhookUrl, out Uri? validatedUri)
    {
        validatedUri = null;
        if (string.IsNullOrWhiteSpace(webhookUrl))
            return false;

        if (!Uri.TryCreate(webhookUrl.Trim(), UriKind.Absolute, out var uri))
            return false;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (uri.HostNameType != UriHostNameType.Dns || uri.IsLoopback)
            return false;

        var host = uri.Host;
        foreach (var suffix in AllowedHostSuffixes)
        {
            if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && host.Length > suffix.Length)
            {
                validatedUri = uri;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 推送一条文本消息。返回 false 表示投递失败（已记录日志）——
    /// 调用方据此决定是否需要额外提示，但告警主链不得因此中断。
    /// </summary>
    public async Task<bool> SendAsync(
        string webhookUrl, string? secret, string text, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!IsValidWebhookUrl(webhookUrl, out var validatedUri))
            {
                logger.LogWarning("拒绝向非法的飞书 webhook 地址推送告警");
                return false;
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            var timestamp = FeishuWebhookMessage.BuildTimestamp(DateTime.UtcNow);
            var sign = FeishuWebhookMessage.ComputeSign(timestamp, secret);
            var payload = FeishuWebhookMessage.BuildTextPayload(text, timestamp, sign);

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await client
                .PostAsync(validatedUri, content, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
                return true;

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            logger.LogWarning(
                "飞书 webhook 返回非成功状态: {Status} {Body}",
                (int)response.StatusCode,
                body.Length > 200 ? body[..200] : body);
            return false;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "飞书 webhook 投递异常");
            return false;
        }
    }
}
