using System.Reflection;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Services.Market;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// Agent 工厂基类：封装基于 RequiresToolsAttribute 的工具解析和 Token 追踪中间件附加。
/// </summary>
public abstract class AgentFactoryBase
{
    protected readonly IServiceProvider ServiceProvider;
    protected readonly TokenTrackingMiddleware TokenTracking;
    protected readonly ILogger Logger;

    protected AgentFactoryBase(
        IServiceProvider serviceProvider,
        TokenTrackingMiddleware tokenTracking,
        ILogger logger)
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        TokenTracking = tokenTracking ?? throw new ArgumentNullException(nameof(tokenTracking));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 基于 agentType 上的 <see cref="RequiresToolsAttribute"/> 与指定市场类型解析工具列表。
    /// </summary>
    protected IList<AITool> ResolveToolsFor(Type agentType, MarketType marketType)
    {
        var tools = new List<AITool>();
        var resolvedCount = 0;

        var toolAttributes = agentType.GetCustomAttributes<RequiresToolsAttribute>().ToList();
        foreach (var attr in toolAttributes)
        {
            var toolService = ServiceProvider.GetKeyedService(attr.ToolInterfaceType, marketType);
            if (toolService is IToolsProvider provider)
            {
                var providerTools = provider.GetFunctions().ToList();

                foreach (var tool in providerTools)
                {
                    Logger.LogDebug(
                        "解析 Agent 专业工具: {AgentType}, 市场: {Market}, Tool: {ToolName}, RuntimeType: {RuntimeType}",
                        agentType.Name,
                        marketType,
                        tool.Name,
                        tool.GetType().FullName);
                }

                tools.AddRange(providerTools);
                resolvedCount++;
            }
            else
            {
                OnToolMissing(agentType, attr.ToolInterfaceType, marketType);
            }
        }

        if (resolvedCount != toolAttributes.Count)
        {
            Logger.LogError(
                "{AgentType} 缺少 {Missing}/{Total} 个工具（市场: {Market}）",
                agentType.Name, toolAttributes.Count - resolvedCount, toolAttributes.Count, marketType);
        }

        return tools;
    }

    /// <summary>
    /// 工具未找到时的回调，子类可重写以决定是抛异常还是仅记录日志。
    /// </summary>
    protected virtual void OnToolMissing(Type agentType, Type toolInterfaceType, MarketType marketType)
    {
        Logger.LogError(
            "未找到 Agent {AgentType} 所需的工具 {ToolType}（市场: {Market}），分析能力可能受限",
            agentType.Name, toolInterfaceType.Name, marketType);
    }

    /// <summary>
    /// 通过 MAF Builder 模式附加 Token 追踪中间件，返回包装后的 Agent。
    /// 子类可链式追加更多中间件。
    /// </summary>
    protected AIAgent WrapWithTokenTracking(AIAgent agent)
    {
        return agent
            .AsBuilder()
            .Use(
                runFunc: TokenTracking.InvokeAsync,
                runStreamingFunc: TokenTracking.InvokeStreamingAsync)
            .Build();
    }
}
