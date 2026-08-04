using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Market;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 分析师代理工厂接口
/// </summary>
public interface IAnalystAgentFactory
{
    /// <summary>
    /// 根据类型创建对应的代理（动态调用，运行时检查）
    /// </summary>
    AIAgent CreateAnalyst(Type agentType);

    /// <summary>
    /// 根据类型创建代理，附加额外的 AIContextProvider（如共享市场快照）
    /// </summary>
    AIAgent CreateAnalyst(Type agentType, AIContextProvider[]? additionalProviders);

    /// <summary>
    /// 使用调用方提供的不可变 Runtime Client 创建代理，确保同一次工作流模型配置一致。
    /// </summary>
    AIAgent CreateAnalyst(
        Type agentType,
        IChatClient chatClient,
        AIContextProvider[]? additionalProviders = null);
}

/// <summary>
/// 分析师代理工厂实现
/// 负责创建配置好的分析师代理（使用 DI 容器）
/// </summary>
public class AnalystAgentFactory : AgentFactoryBase, IAnalystAgentFactory
{
    private readonly MarketContext _marketContext;

    public AnalystAgentFactory(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        MarketContext marketContext,
        TokenTrackingMiddleware tokenTracking,
        ILogger<AnalystAgentFactory> logger)
        : base(serviceProvider, chatClientFactory, tokenTracking, logger)
    {
        _marketContext = marketContext ?? throw new ArgumentNullException(nameof(marketContext));
    }

    /// <inheritdoc />
    public AIAgent CreateAnalyst(Type agentType) => CreateAnalyst(agentType, additionalProviders: null);

    /// <inheritdoc />
    public AIAgent CreateAnalyst(Type agentType, AIContextProvider[]? additionalProviders)
    {
        return CreateAnalyst(agentType, CreateChatClient(), additionalProviders);
    }

    public AIAgent CreateAnalyst(
        Type agentType,
        IChatClient chatClient,
        AIContextProvider[]? additionalProviders = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(chatClient);

            // 严格限制必须是 AnalystAgentBase 的子类
            if (!typeof(AnalystAgentBase).IsAssignableFrom(agentType))
            {
                throw new ArgumentException($"Type {agentType.Name} must inherit from AnalystAgentBase", nameof(agentType));
            }

            // 根据当前市场类型获取对应的工具实现
            var currentMarket = _marketContext.CurrentMarket;
            var tools = ResolveToolsFor(agentType, currentMarket);

            // 显式传递 chatClient、合并后的工具列表和 AIContextProvider[]，其余由 DI 自动解析
            var parameters = new List<object> { chatClient, tools };

            if (additionalProviders is { Length: > 0 })
                parameters.Add(additionalProviders);

            var agent = (AIAgent)ActivatorUtilities.CreateInstance(ServiceProvider, agentType, parameters.ToArray());

            var middlewareAgent = WrapWithTokenTracking(agent);

            Logger.LogInformation(
                "成功创建分析师代理: {AgentType} (市场: {Market}，已附加中间件)",
                agentType.Name, currentMarket);

            return middlewareAgent;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "创建分析师代理时发生错误: {AgentType}", agentType.Name);
            throw;
        }
    }
}
