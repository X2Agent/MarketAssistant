using MarketAssistant.Agents.Middleware;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Middleware;

[TestClass]
public class ConversationCompressionMiddlewareTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void Constructor_ShouldSetDefaults()
    {
        var middleware = CreateMiddleware();

        Assert.AreEqual(8000, middleware.MaxTokens);
        Assert.AreEqual(4, middleware.ReserveRecentCount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void MaxTokens_CanBeChanged()
    {
        var middleware = CreateMiddleware();

        middleware.MaxTokens = 4000;

        Assert.AreEqual(4000, middleware.MaxTokens);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void ReserveRecentCount_CanBeChanged()
    {
        var middleware = CreateMiddleware();

        middleware.ReserveRecentCount = 6;

        Assert.AreEqual(6, middleware.ReserveRecentCount);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void PreCompressHook_DefaultNull()
    {
        var middleware = CreateMiddleware();

        Assert.IsNull(middleware.PreCompressHook);
    }

    private static ConversationCompressionMiddleware CreateMiddleware()
    {
        var chatClientFactory = new Mock<Func<IChatClient>>();
        chatClientFactory.Setup(f => f()).Returns(new Mock<IChatClient>().Object);

        return new ConversationCompressionMiddleware(
            chatClientFactory.Object,
            NullLogger<ConversationCompressionMiddleware>.Instance);
    }
}
