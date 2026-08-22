using System.Runtime.CompilerServices;
using MarketAssistant.Agents.Analysts;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace TestMarketAssistant;

/// <summary>
/// 分析师失败隔离包装器的确定性单测：验证异常与空输出被转换为失败标记消息，
/// 调用方取消不被拦截，成功路径原样透传。
/// </summary>
[TestClass]
public sealed class AIAgentFailureIsolationTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public async Task RunAsync_InnerThrows_ShouldReturnMarkerMessage()
    {
        var failures = new List<Exception>();
        var wrapped = CreateWrappedAgent("FundamentalAnalyst", new ThrowingChatClient(), failures.Add);

        var response = await wrapped.RunAsync([new ChatMessage(ChatRole.User, "分析")]);

        Assert.AreEqual(1, failures.Count, "失败回调应被触发一次");
        Assert.IsTrue(AnalystFailureMessages.IsFailureMarker(response.Text),
            $"响应应为失败标记，实际: {response.Text}");
        StringAssert.Contains(response.Text, "FundamentalAnalyst");
        StringAssert.Contains(response.Text, ThrowingChatClient.FailureReason);
        Assert.AreEqual(ChatRole.Assistant, response.Messages[0].Role);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RunStreamingAsync_InnerThrows_ShouldYieldMarkerUpdate()
    {
        var failures = new List<Exception>();
        var wrapped = CreateWrappedAgent("FundamentalAnalyst", new ThrowingChatClient(), failures.Add);

        var updates = new List<AgentResponseUpdate>();
        await foreach (var update in wrapped.RunStreamingAsync([new ChatMessage(ChatRole.User, "分析")]))
        {
            updates.Add(update);
        }

        Assert.AreEqual(1, updates.Count, "失败时应恰好产出一个失败标记更新");
        Assert.AreEqual(1, failures.Count);
        Assert.IsTrue(AnalystFailureMessages.IsFailureMarker(updates[0].Text));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void IsFailureMarker_PartialTextBeforeMarker_ShouldStillDetect()
    {
        // 流式失败场景：部分正文先产出，随后异常 → 最终消息 = 部分正文 + 标记。
        // 检测必须用 Contains，否则半成品会被当成功结论送给协调分析师。
        var combined = "该股票基本面良好，营收增长"
                       + AnalystFailureMessages.BuildFailureText("FundamentalAnalyst", "流式中断");

        Assert.IsTrue(AnalystFailureMessages.IsFailureMarker(combined));
        // 纯正文（不含标记）不得误判
        Assert.IsFalse(AnalystFailureMessages.IsFailureMarker("该股票基本面良好，营收增长 20%"));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RunAsync_InnerReturnsEmptyText_ShouldReturnMarkerMessage()
    {
        var wrapped = CreateWrappedAgent("NewsEventAnalyst", new StubChatClient("   "), null);

        var response = await wrapped.RunAsync([new ChatMessage(ChatRole.User, "分析")]);

        Assert.IsTrue(AnalystFailureMessages.IsFailureMarker(response.Text), "空输出应视为失败");
        StringAssert.Contains(response.Text, "未返回任何文本结论");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RunAsync_Success_ShouldPassThroughResponse()
    {
        var wrapped = CreateWrappedAgent("NewsEventAnalyst", new StubChatClient("正常结论"),
            _ => Assert.Fail("成功路径不应触发失败回调"));

        var response = await wrapped.RunAsync([new ChatMessage(ChatRole.User, "分析")]);

        Assert.AreEqual("正常结论", response.Text);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task RunAsync_OuterCancellation_ShouldNotBeSwallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var wrapped = CreateWrappedAgent("FundamentalAnalyst", new CancellationRespectingChatClient(),
            _ => Assert.Fail("外部取消不应触发失败回调"));

        await Assert.ThrowsExactlyAsync<OperationCanceledException>(
            () => wrapped.RunAsync([new ChatMessage(ChatRole.User, "分析")], null, null, cts.Token));
    }

    private static AIAgent CreateWrappedAgent(
        string name,
        IChatClient chatClient,
        Action<Exception>? onAnalysisFailed)
    {
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = name,
            ChatOptions = new ChatOptions()
        });

        return agent.WithFailureIsolation(onAnalysisFailed);
    }

    private sealed class StubChatClient(string replyText) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, replyText)));

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, replyText);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public const string FailureReason = "模拟的分析师运行故障";

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(FailureReason);

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException(FailureReason);

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// 遵循取消令牌的桩：令牌已取消时抛 OperationCanceledException（模拟真实 LLM 客户端行为）。
    /// </summary>
    private sealed class CancellationRespectingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "不应到达")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new OperationCanceledException(cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
