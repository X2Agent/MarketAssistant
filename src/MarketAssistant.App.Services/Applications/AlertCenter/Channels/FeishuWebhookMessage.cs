using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace MarketAssistant.Applications.AlertCenter.Channels;

/// <summary>
/// 飞书自定义机器人报文与签名的纯计算（无 IO，便于单测）。
/// </summary>
/// <remarks>
/// 签名算法（飞书开放平台约定）：
/// <c>string_to_sign = timestamp + "\n" + secret</c>，
/// 以 <c>secret</c> 为密钥做 HMAC-SHA256 后取 Base64；
/// 未配置签名校验时 timestamp 与 sign 均不随报文发送。
/// </remarks>
public static class FeishuWebhookMessage
{
    /// <summary>生成秒级时间戳（飞书签名要求的时间戳单位为秒）。</summary>
    public static string BuildTimestamp(DateTime utcNow)
        => new DateTimeOffset(utcNow.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(utcNow, DateTimeKind.Utc)
            : utcNow).ToUnixTimeSeconds().ToString();

    /// <summary>计算签名；secret 为空时返回 null（表示不启用签名校验）。</summary>
    public static string? ComputeSign(string timestamp, string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return null;

        var stringToSign = $"{timestamp}\n{secret}";
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes(stringToSign));
        return Convert.ToBase64String(hash);
    }

    /// <summary>构建文本类型消息的请求体 JSON。</summary>
    public static string BuildTextPayload(string text, string? timestamp = null, string? sign = null)
    {
        var payload = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["msg_type"] = "text",
            ["content"] = new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = text }
        };

        if (!string.IsNullOrEmpty(timestamp) && !string.IsNullOrEmpty(sign))
        {
            payload["timestamp"] = timestamp;
            payload["sign"] = sign;
        }

        return JsonSerializer.Serialize(payload);
    }
}
