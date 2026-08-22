using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant.Infrastructure;

[TestClass]
public sealed class TradingAgentFactoryTest
{
    [TestMethod]
    [TestCategory("Unit")]
    public void CreateAutomationAgent_AllCryptoToolsRegistered_ShouldCreateAgent()
    {
        using var services = CreateServices(includeTradingTools: true);
        var factory = CreateFactory(services);

        var agent = factory.CreateAutomationAgent();

        Assert.IsNotNull(agent);
        Assert.AreEqual("TradingAgent", agent.Name);
        var functionClient = agent.GetService<FunctionInvokingChatClient>();
        Assert.IsNotNull(functionClient);
        Assert.AreEqual(20, functionClient.MaximumIterationsPerRequest);
    }

    [TestMethod]
    [TestCategory("Unit")]
    public void CreateAutomationAgent_MissingTradingTools_ShouldFailClosed()
    {
        using var services = CreateServices(includeTradingTools: false);
        var factory = CreateFactory(services);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            factory.CreateAutomationAgent);

        StringAssert.Contains(exception.Message, nameof(ITradingExecutionTools));
        StringAssert.Contains(exception.Message, nameof(MarketType.Crypto));
    }

    private static ServiceProvider CreateServices(bool includeTradingTools)
    {
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IBasicDataTools>(
            MarketType.Crypto,
            CreateToolsProvider<IBasicDataTools>());
        services.AddKeyedSingleton<ITechnicalDataTools>(
            MarketType.Crypto,
            CreateToolsProvider<ITechnicalDataTools>());
        services.AddKeyedSingleton<IStrategyTools>(
            MarketType.Crypto,
            CreateToolsProvider<IStrategyTools>());

        if (includeTradingTools)
        {
            services.AddKeyedSingleton<ITradingExecutionTools>(
                MarketType.Crypto,
                CreateToolsProvider<ITradingExecutionTools>());
        }

        return services.BuildServiceProvider();
    }

    private static TradingAgentFactory CreateFactory(IServiceProvider services)
    {
        var chatClientFactory = new Mock<IChatClientFactory>();
        chatClientFactory
            .Setup(factory => factory.CreateClient())
            .Returns(new Mock<IChatClient>().Object);

        return new TradingAgentFactory(
            services,
            chatClientFactory.Object,
            new TokenTrackingMiddleware(NullLogger<TokenTrackingMiddleware>.Instance),
            NullLogger<TradingAgentFactory>.Instance);
    }

    private static T CreateToolsProvider<T>() where T : class, IToolsProvider
    {
        var provider = new Mock<T>();
        provider.Setup(item => item.GetFunctions()).Returns([]);
        return provider.Object;
    }
}
