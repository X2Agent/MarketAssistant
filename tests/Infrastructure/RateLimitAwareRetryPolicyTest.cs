using System.ClientModel;
using System.ClientModel.Primitives;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.Logging;
using OpenAI;
using OpenAI.Chat;

namespace TestMarketAssistant.Infrastructure;

/// <summary>
/// 429 限流重试策略契约测试：用桩 HttpMessageHandler 驱动真实的 OpenAI ChatClient 管线（不碰真实网络），
/// 锁定三条规则：免费模型日配额不重试；分钟级限流等到 X-RateLimit-Reset；重置时间过远时沿用 SDK 默认退避。
/// 另锁定一条边界：模型下线（404）等不可重试状态只发一次请求，不被本策略放宽。
/// </summary>
[TestClass]
public sealed class RateLimitAwareRetryPolicyTest
{
    private const string DailyQuotaBody =
        """{"error":{"message":"Rate limit exceeded: free-models-per-day. Add 10 credits to unlock 1000 free model requests per day","code":429}}""";

    private const string PerMinuteBody =
        """{"error":{"message":"Rate limit exceeded: free-models-per-min.","code":429}}""";

    private const string ModelRemovedBody =
        """{"error":{"message":"dots-studio/dots-3-note-preview:free is no longer available on OpenRouter: no provider serves this model anymore. See available models at https://openrouter.ai/models","code":404}}""";

    private const string CompletionBody =
        """{"id":"chatcmpl-1","object":"chat.completion","created":1,"model":"test-model","choices":[{"index":0,"message":{"role":"assistant","content":"ok"},"finish_reason":"stop"}]}""";

    [TestMethod]
    [TestCategory("Unit")]
    public async Task DailyQuota429_ShouldNotRetry()
    {
        // 日配额的重置时间在数小时后：即使响应带 X-RateLimit-Reset 也不能等
        var handler = new ScriptedHandler(TooManyRequests(DailyQuotaBody, TimeSpan.FromHours(8)));
        var chatClient = CreateChatClient(handler, maxRetries: 3);

        var exception = await Assert.ThrowsExactlyAsync<ClientResultException>(
            () => chatClient.CompleteChatAsync("hi"));

        Assert.AreEqual(1, handler.CallCount, "日配额要到 UTC 零点才恢复，重试只会白等");
        StringAssert.Contains(exception.Message, "free-models-per-day", "缓冲正文后异常仍须带服务端原文");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task PerMinute429_ShouldWaitUntilResetThenSucceed()
    {
        var logger = new CapturingLogger();
        var handler = new ScriptedHandler(TooManyRequests(PerMinuteBody, TimeSpan.FromSeconds(2)), Completion());
        var chatClient = CreateChatClient(handler, maxRetries: 3, logger);

        var stopwatch = Stopwatch.StartNew();
        var result = await chatClient.CompleteChatAsync("hi");
        stopwatch.Stop();

        Assert.AreEqual("ok", result.Value.Content[0].Text);
        Assert.AreEqual(2, handler.CallCount);

        // SDK 默认首次退避只有 0.8 秒；超过 1.5 秒说明按 X-RateLimit-Reset 对齐到了窗口重置
        Assert.IsTrue(stopwatch.Elapsed >= TimeSpan.FromSeconds(1.5),
            $"应等到限流窗口重置再重试，实际仅等待 {stopwatch.Elapsed.TotalSeconds:F1}s");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"等待不应远超重置时间，实际 {stopwatch.Elapsed.TotalSeconds:F1}s");

        // 有意的长等待必须在日志里留痕，否则又会出现"耗时很长却查不出原因"
        Assert.HasCount(1, logger.Entries);
        Assert.AreEqual(LogLevel.Warning, logger.Entries[0].Level);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ResetFarAway429_ShouldFallBackToDefaultBackoff()
    {
        // 非分钟级窗口（重置在一小时后）：不能静默等一小时，沿用 SDK 默认退避后交出错误
        var handler = new ScriptedHandler(TooManyRequests(PerMinuteBody, TimeSpan.FromHours(1)));
        var chatClient = CreateChatClient(handler, maxRetries: 1);

        var stopwatch = Stopwatch.StartNew();
        await Assert.ThrowsExactlyAsync<ClientResultException>(() => chatClient.CompleteChatAsync("hi"));
        stopwatch.Stop();

        Assert.AreEqual(2, handler.CallCount, "仍应按默认策略重试 1 次");
        Assert.IsTrue(stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"重置时间过远时不应等待，实际 {stopwatch.Elapsed.TotalSeconds:F1}s");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task ModelNotFound404_ShouldNotRetry()
    {
        // 模型下线返回 404，重试不会改变结果。调试器里同一次失败被多层 await 重抛而反复命中，并不是重试；
        // 这里在真实 SDK 管线上锁定"只发一次请求"，防止以后误把 404 加入重试
        var handler = new ScriptedHandler(NotFound(ModelRemovedBody));
        var chatClient = CreateChatClient(handler, maxRetries: 3);

        var exception = await Assert.ThrowsExactlyAsync<ClientResultException>(
            () => chatClient.CompleteChatAsync("hi"));

        Assert.AreEqual(404, exception.Status);
        Assert.AreEqual(1, handler.CallCount, "404 不是可重试状态，只应发出一次请求");
    }

    private static ChatClient CreateChatClient(HttpMessageHandler handler, int maxRetries, ILogger? logger = null)
    {
        var options = new OpenAIClientOptions
        {
            Endpoint = new Uri("https://openrouter.test/api/v1"),
            RetryPolicy = new RateLimitAwareRetryPolicy(maxRetries, logger),
            Transport = new HttpClientPipelineTransport(new HttpClient(handler))
        };
        return new ChatClient("test-model", new ApiKeyCredential("test-key"), options);
    }

    private static Func<HttpResponseMessage> TooManyRequests(string body, TimeSpan resetAfter) => () =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var resetAt = DateTimeOffset.UtcNow + resetAfter;
        response.Headers.TryAddWithoutValidation(
            "X-RateLimit-Reset",
            resetAt.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        return response;
    };

    private static Func<HttpResponseMessage> NotFound(string body) => () => new HttpResponseMessage(HttpStatusCode.NotFound)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static Func<HttpResponseMessage> Completion() => () => new HttpResponseMessage(HttpStatusCode.OK)
    {
        Content = new StringContent(CompletionBody, Encoding.UTF8, "application/json")
    };

    /// <summary>
    /// 按顺序返回预设响应，耗尽后重复最后一个；每次新建响应对象（管线会释放用过的响应）。
    /// </summary>
    private sealed class ScriptedHandler(params Func<HttpResponseMessage>[] responses) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = responses[Math.Min(CallCount, responses.Length - 1)]();
            CallCount++;
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }
    }
}
