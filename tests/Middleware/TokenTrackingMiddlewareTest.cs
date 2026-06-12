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
}
