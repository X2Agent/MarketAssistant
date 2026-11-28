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
    public async Task LogAndAccumulate_SingleRunExceedsLimit_ShouldThrowButStillRecordUsage()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { Name = "TestAgent" });
        var session = await agent.CreateSessionAsync();
        var middleware = new TokenTrackingMiddleware(
            NullLogger<TokenTrackingMiddleware>.Instance);

        // 单次运行在上限内正常累计
        middleware.LogAndAccumulate(session, inputTokens: 100_000, outputTokens: 50_000, agentName: "TestAgent");

        // 单次运行突破上限时抛出异常，使该次运行按失败处理；超限那次的用量仍计入统计
        var exception = Assert.ThrowsExactly<InvalidOperationException>(() =>
            middleware.LogAndAccumulate(
                session,
                inputTokens: TokenTrackingMiddleware.MaxTokensPerRun,
                outputTokens: 1,
                agentName: "TestAgent"));

        Assert.Contains("熔断", exception.Message);
        var (input, output) = TokenTrackingMiddleware.GetCumulativeTokens(session);
        Assert.AreEqual(100_000L + TokenTrackingMiddleware.MaxTokensPerRun, input);
        Assert.AreEqual(50_001L, output);
    }

    // 聊天每轮重发整段历史，会话累计随轮数二次增长；累计值只做统计，不得让每轮都在上限内的对话被熔断
    [TestMethod]
    [TestCategory("Unit")]
    public async Task LogAndAccumulate_CumulativeBeyondLimitAcrossRuns_ShouldOnlyAccumulate()
    {
        var chatClient = new Mock<IChatClient>().Object;
        var agent = new ChatClientAgent(chatClient, new ChatClientAgentOptions { Name = "TestAgent" });
        var session = await agent.CreateSessionAsync();
        var middleware = new TokenTrackingMiddleware(
            NullLogger<TokenTrackingMiddleware>.Instance);

        const int runCount = 30;
        for (var run = 0; run < runCount; run++)
            middleware.LogAndAccumulate(session, inputTokens: 10_000, outputTokens: 2_000, agentName: "TestAgent");

        var (input, output) = TokenTrackingMiddleware.GetCumulativeTokens(session);
        Assert.AreEqual(runCount * 10_000L, input);
        Assert.AreEqual(runCount * 2_000L, output);
        Assert.IsGreaterThan(TokenTrackingMiddleware.MaxTokensPerRun, input + output);
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
