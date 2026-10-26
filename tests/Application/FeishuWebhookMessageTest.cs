using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MarketAssistant.Applications.AlertCenter.Channels;

namespace TestMarketAssistant.Application;

/// <summary>
/// 飞书 webhook 报文与签名纯计算测试。
/// </summary>
[TestClass]
public sealed class FeishuWebhookMessageTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void ComputeSign_ShouldMatchFeishuSpecification()
    {
        const string Timestamp = "1770000000";
        const string Secret = "secret-key";

        var sign = FeishuWebhookMessage.ComputeSign(Timestamp, Secret);

        // 飞书约定：HMAC-SHA256(key=secret, data=timestamp + "\n" + secret) 后 Base64
        var expected = Convert.ToBase64String(
            HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes($"{Timestamp}\n{Secret}")));

        Assert.AreEqual(expected, sign);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ComputeSign_WithoutSecret_ShouldReturnNull()
    {
        Assert.IsNull(FeishuWebhookMessage.ComputeSign("1770000000", null));
        Assert.IsNull(FeishuWebhookMessage.ComputeSign("1770000000", string.Empty));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildTextPayload_WithSignature_ShouldContainTimestampAndSign()
    {
        var payload = FeishuWebhookMessage.BuildTextPayload("测试消息", "1770000000", "signature");

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.AreEqual("text", root.GetProperty("msg_type").GetString());
        Assert.AreEqual("测试消息", root.GetProperty("content").GetProperty("text").GetString());
        Assert.AreEqual("1770000000", root.GetProperty("timestamp").GetString());
        Assert.AreEqual("signature", root.GetProperty("sign").GetString());
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildTextPayload_WithoutSignature_ShouldOmitTimestampAndSign()
    {
        var payload = FeishuWebhookMessage.BuildTextPayload("测试消息");

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        Assert.IsFalse(root.TryGetProperty("timestamp", out _), "未配置密钥时不应携带签名字段");
        Assert.IsFalse(root.TryGetProperty("sign", out _));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void BuildTimestamp_ShouldBeUnixSeconds()
    {
        var timestamp = FeishuWebhookMessage.BuildTimestamp(new DateTime(2026, 10, 11, 12, 0, 0, DateTimeKind.Utc));

        Assert.AreEqual("1791720000", timestamp);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsValidWebhookUrl_ShouldEnforceHttpsAndFeishuHostAllowlist()
    {
        Assert.IsTrue(FeishuWebhookClient.IsValidWebhookUrl(
            "https://open.feishu.cn/open-apis/bot/v2/hook/12345", out _));
        Assert.IsTrue(FeishuWebhookClient.IsValidWebhookUrl(
            "https://open.larksuite.com/open-apis/bot/v2/hook/12345", out _));
        Assert.IsTrue(FeishuWebhookClient.IsValidWebhookUrl(
            "https://open.larkoffice.com/open-apis/bot/v2/hook/12345", out _));

        Assert.IsFalse(FeishuWebhookClient.IsValidWebhookUrl(
            "http://open.feishu.cn/open-apis/bot/v2/hook/12345", out _), "非 HTTPS 应拒绝");
        Assert.IsFalse(FeishuWebhookClient.IsValidWebhookUrl(
            "https://127.0.0.1/hook", out _), "本地回环地址应拒绝");
        Assert.IsFalse(FeishuWebhookClient.IsValidWebhookUrl(
            "https://evil.example.com/open.feishu.cn", out _), "非飞书域名应拒绝");
    }
}
