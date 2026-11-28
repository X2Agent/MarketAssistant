using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Agents.Tools.Crypto;
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

    // 余额、持仓是决策依据：缺失时拒绝创建 Agent，而不是在看不到账户的情况下决策
    [TestMethod]
    [TestCategory("Unit")]
    public void CreateAutomationAgent_MissingTradingTools_ShouldFailClosed()
    {
        using var services = CreateServices(includeTradingTools: false);
        var factory = CreateFactory(services);

        var exception = Assert.ThrowsExactly<InvalidOperationException>(
            factory.CreateAutomationAgent);

        Assert.Contains(nameof(ITradingExecutionTools), exception.Message);
        Assert.Contains(nameof(MarketType.Crypto), exception.Message);
    }

    // 模型一次工具调用就能绕过执行器的置信度/仓位/方向校验，因此账户与策略工具必须恰为只读白名单。
    // 新增只读工具时同步更新白名单；任何下单、撤单、改策略状态、建护栏类工具都不得加入。
    [TestMethod]
    [TestCategory("Unit")]
    public void TradingAgentTools_ShouldBeReadOnly()
    {
        IToolsProvider[] providers =
        [
            new CryptoTradingExecutionTools(null!, null!, NullLogger<CryptoTradingExecutionTools>.Instance),
            new CryptoStrategyTools(null!, null!, NullLogger<CryptoStrategyTools>.Instance)
        ];

        var toolNames = providers
            .SelectMany(provider => provider.GetFunctions())
            .Select(function => function.Name)
            .ToList();

        CollectionAssert.AreEquivalent(
            new[] { "GetAccountBalance", "GetCurrentPositions", "GetOrderStatus", "GetActiveStrategies", "GetStrategy" },
            toolNames);
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
