using MarketAssistant.Infrastructure.Core;

namespace TestMarketAssistant.Infrastructure;

/// <summary>
/// 错误消息映射：确保可自助修复的问题（模型 slug 下线）能透出可读原因，
/// 而不是退化成"操作失败"让用户只能去翻日志。
/// </summary>
[TestClass]
public sealed class ErrorMessageMapperTest
{
    /// <summary>
    /// 模拟 System.ClientModel.ClientResultException：类型名必须为该名才会被反射命中，
    /// Message 承载服务端原文。此处用同名子类复现真实异常形态，
    /// 以免 Core 为一条错误文案引入 LLM SDK 依赖。
    /// </summary>
    private class ClientResultException(string message) : Exception(message);

    /// <summary>Message 不可读的极端形态（仅可能来自 mock）：不得让映射器自身抛异常。</summary>
    private sealed class NullMessageClientResultException() : ClientResultException("placeholder")
    {
        public override string Message => null!;
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProviderError_ShouldSurfaceServiceMessage()
    {
        // 实测 404 原文：模型 slug 已下线，服务端直接给出替代 slug
        var raw = "HTTP 404 (: )\nThis model is unavailable for free. The paid version is available now - use this slug instead: minimax/minimax-m3";
        var mapped = ErrorMessageMapper.GetUserFriendlyMessage(new ClientResultException(raw));

        StringAssert.Contains(mapped, "minimax/minimax-m3",
            "服务端给出的替代 slug 是唯一能指导用户修复的信息，必须透出");
        Assert.IsFalse(mapped.Contains("操作失败"), "不得退化为无信息量的通用文案");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProviderError_LongMessage_ShouldBeTruncated()
    {
        var raw = "HTTP 500 (: )\n" + new string('x', 500);
        var mapped = ErrorMessageMapper.GetUserFriendlyMessage(new ClientResultException(raw));

        Assert.IsTrue(mapped.Length < 260, "超长服务端原文须截断，避免撑破提示浮层");
        StringAssert.Contains(mapped, "模型服务返回错误");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProviderError_WithUnreadableMessage_ShouldFallBackGracefully()
    {
        var mapped = ErrorMessageMapper.GetUserFriendlyMessage(new NullMessageClientResultException());

        Assert.AreEqual("操作失败，请稍后重试", mapped,
            "取不到服务端原文时应落入默认文案，而不是让错误映射器自身抛异常");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ProviderError_FreeModelDailyQuota_ShouldExplainResetTimeAndNextSteps()
    {
        // 实测原文（OpenRouter 免费模型日配额）：状态行无信息量，正文为英文且不含重置时间
        var raw = "HTTP 429 (: )\n\nRate limit exceeded: free-models-per-day. Add 10 credits to unlock 1000 free model requests per day: https://openrouter.ai/settings/credits?utm_source=api&utm_campaign=free_daily_limit";
        var mapped = ErrorMessageMapper.GetUserFriendlyMessage(new ClientResultException(raw));

        StringAssert.Contains(mapped, "今日请求额度已用完");
        StringAssert.Contains(mapped, "08:00", "须告知额度何时重置");
        StringAssert.Contains(mapped, "付费模型", "须给出可操作的处理建议");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void TryGetProviderErrorMessage_ShouldOnlyMapProviderErrors()
    {
        var isProviderError = ErrorMessageMapper.TryGetProviderErrorMessage(
            new ClientResultException("HTTP 404 (: )\nmodel gone"), out var providerMessage);

        Assert.IsTrue(isProviderError);
        StringAssert.Contains(providerMessage, "model gone");

        // 其他异常必须让调用方保留自己的 Message：通用映射会把 InvalidOperationException 换成泛化文案
        var isOtherProviderError = ErrorMessageMapper.TryGetProviderErrorMessage(
            new InvalidOperationException("模型未返回任何文本结论"), out var otherMessage);

        Assert.IsFalse(isOtherProviderError);
        Assert.IsNull(otherMessage);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void FriendlyException_ShouldPassThroughUnchanged()
    {
        var mapped = ErrorMessageMapper.GetUserFriendlyMessage(
            new FriendlyException("币安服务在当前地区不可用"));

        Assert.AreEqual("币安服务在当前地区不可用", mapped);
    }
}
