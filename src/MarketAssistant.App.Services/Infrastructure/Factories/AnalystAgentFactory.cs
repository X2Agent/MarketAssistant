using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Market;
using Microsoft.Agents.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 分析师代理工厂接口
/// </summary>
public interface IAnalystAgentFactory
{
    /// <summary>
    /// 使用调用方提供的不可变 Runtime Client 创建代理，确保同一次工作流模型配置一致。
    /// </summary>
    AIAgent CreateAnalyst(
        Type agentType,
        ChatClientRuntime runtime,
        AIContextProvider[]? additionalProviders = null);
}

/// <summary>
/// 分析师代理工厂实现
/// 负责创建配置好的分析师代理（使用 DI 容器）
/// </summary>
public class AnalystAgentFactory : AgentFactoryBase, IAnalystAgentFactory
{
    private readonly MarketContext _marketContext;
    private readonly AgentSkillsProvider _skillsProvider;

    public AnalystAgentFactory(
        IServiceProvider serviceProvider,
        MarketContext marketContext,
        AgentSkillsProvider skillsProvider,
        TokenTrackingMiddleware tokenTracking,
        ILogger<AnalystAgentFactory> logger)
        : base(serviceProvider, tokenTracking, logger)
    {
        _marketContext = marketContext ?? throw new ArgumentNullException(nameof(marketContext));
        _skillsProvider = skillsProvider ?? throw new ArgumentNullException(nameof(skillsProvider));
    }

    public AIAgent CreateAnalyst(
        Type agentType,
        ChatClientRuntime runtime,
        AIContextProvider[]? additionalProviders = null)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(runtime);

            // 严格限制必须是 AnalystAgentBase 的子类
            if (!typeof(AnalystAgentBase).IsAssignableFrom(agentType))
            {
                throw new ArgumentException($"Type {agentType.Name} must inherit from AnalystAgentBase", nameof(agentType));
            }

            // 根据当前市场类型获取对应的工具实现
            var currentMarket = _marketContext.CurrentMarket;
            var tools = ResolveToolsFor(agentType, currentMarket);

            // 显式传递 Runtime Client、结构化输出模式、工具和上下文，其余由 DI 自动解析。
            var parameters = new List<object>
            {
                runtime.Client,
                tools,
                runtime.StructuredOutputMode
            };

            Logger.LogInformation(
                "创建分析师代理: {AgentType}, 市场: {Market}, Provider: {ProviderId}, Model: {ModelId}, ResponseFormat: {StructuredOutputMode}, Tools: {ToolCount}",
                agentType.Name,
                currentMarket,
                runtime.ProviderId,
                runtime.ModelId,
                runtime.StructuredOutputMode,
                tools.Count);

            AIContextProvider[] contextProviders =
            [
                _skillsProvider,
                .. (additionalProviders ?? [])
            ];
            parameters.Add(contextProviders);

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
