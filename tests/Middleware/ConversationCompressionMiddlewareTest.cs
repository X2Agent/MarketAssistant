using MarketAssistant.Agents.Middleware;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Middleware;

[TestClass]
public class ConversationCompressionMiddlewareTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Create_ShouldReturnIndependentProviders()
    {
        var factory = new ConversationCompactionProviderFactory(NullLoggerFactory.Instance);
        var chatClient = new Mock<IChatClient>().Object;

        var first = factory.Create(chatClient);
        var second = factory.Create(chatClient);

        Assert.IsInstanceOfType(first, typeof(AIContextProvider));
        Assert.IsInstanceOfType(second, typeof(AIContextProvider));
        Assert.AreNotSame(first, second);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Create_NonPositiveMaxTokens_ShouldThrow()
    {
        var factory = new ConversationCompactionProviderFactory(NullLoggerFactory.Instance);
        var chatClient = new Mock<IChatClient>().Object;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            factory.Create(chatClient, maxTokens: 0));
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void Create_NonPositivePreservedGroups_ShouldThrow()
    {
        var factory = new ConversationCompactionProviderFactory(NullLoggerFactory.Instance);
        var chatClient = new Mock<IChatClient>().Object;

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            factory.Create(chatClient, minimumPreservedGroups: 0));
    }
}
