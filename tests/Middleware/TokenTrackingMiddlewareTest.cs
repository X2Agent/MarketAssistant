using MarketAssistant.Agents.Middleware;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Middleware;

[TestClass]
public class TokenTrackingMiddlewareTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void GetCumulativeTokens_NullSession_ReturnsZeros()
    {
        var (input, output) = TokenTrackingMiddleware.GetCumulativeTokens(null);

        Assert.AreEqual(0, input);
        Assert.AreEqual(0, output);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetCumulativeTokens_EmptyStateBag_ReturnsZeros()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { Name = "TestAgent" });
        var session = await agent.CreateSessionAsync();

        var (input, output) = TokenTrackingMiddleware.GetCumulativeTokens(session);

        Assert.AreEqual(0, input);
        Assert.AreEqual(0, output);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetCumulativeTokens_WithValues_ReturnsCorrectly()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { Name = "TestAgent" });
        var session = await agent.CreateSessionAsync();

        session.StateBag.SetValue(TokenTrackingMiddleware.InputTokensKey, "150");
        session.StateBag.SetValue(TokenTrackingMiddleware.OutputTokensKey, "75");

        var (input, output) = TokenTrackingMiddleware.GetCumulativeTokens(session);

        Assert.AreEqual(150, input);
        Assert.AreEqual(75, output);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LogAndAccumulate_ExceedsMaxCumulativeTokens_ShouldThrow()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { Name = "TestAgent" });
        var session = await agent.CreateSessionAsync();
        var middleware = new TokenTrackingMiddleware(
            NullLogger<TokenTrackingMiddleware>.Instance);

        // 上限内正常累计
        middleware.LogAndAccumulate(session, inputTokens: 100_000, outputTokens: 50_000, agentName: "TestAgent");
        var (input, output) = TokenTrackingMiddleware.GetCumulativeTokens(session);
        Assert.AreEqual(150_000L, input + output);

        // 累计突破 200k 熔断上限时抛出异常终止执行，防止工具调用循环失控
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            middleware.LogAndAccumulate(session, inputTokens: 100_000, outputTokens: 60_000, agentName: "TestAgent"));
        StringAssert.Contains(exception.Message, "熔断");
    }

    [TestMethod]
    [TestCategory("Unit")]
    public async Task LogAndAccumulate_ConcurrentUpdates_ShouldNotLoseCounts()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { Name = "TestAgent" });
        var session = await agent.CreateSessionAsync();
        var middleware = new TokenTrackingMiddleware(
            NullLogger<TokenTrackingMiddleware>.Instance);

        const int updateCount = 1_000;
        await Task.WhenAll(Enumerable.Range(0, updateCount).Select(_ => Task.Run(() =>
            middleware.LogAndAccumulate(
                session,
                inputTokens: 2,
                outputTokens: 3,
                agentName: "TestAgent",
                isPrecise: true))));

        var (input, output) = TokenTrackingMiddleware.GetCumulativeTokens(session);

        Assert.AreEqual(updateCount * 2L, input);
        Assert.AreEqual(updateCount * 3L, output);
    }
}
