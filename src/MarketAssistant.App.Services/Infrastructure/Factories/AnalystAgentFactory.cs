using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Analysts.Attributes;
using MarketAssistant.Agents.Middleware;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
using MarketAssistant.Infrastructure.Providers;
using MarketAssistant.Services.Market;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Reflection;

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
public class AnalystAgentFactory : IAnalystAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly MarketContext _marketContext;
    private readonly TokenTrackingMiddleware _tokenTracking;
    private readonly ILogger<AnalystAgentFactory> _logger;

    public AnalystAgentFactory(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        MarketContext marketContext,
        TokenTrackingMiddleware tokenTracking,
        ILogger<AnalystAgentFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _marketContext = marketContext ?? throw new ArgumentNullException(nameof(marketContext));
        _tokenTracking = tokenTracking ?? throw new ArgumentNullException(nameof(tokenTracking));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 根据类型创建对应的代理
    /// </summary>
    public AIAgent CreateAnalyst(Type agentType) => CreateAnalyst(agentType, additionalProviders: null);

    /// <summary>
    /// 根据类型创建对应的代理，支持附加额外的 AIContextProvider
    /// </summary>
    public AIAgent CreateAnalyst(Type agentType, AIContextProvider[]? additionalProviders)
    {
        return CreateAnalyst(agentType, _chatClientFactory.CreateClient(), additionalProviders);
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

            // 根据 Analyst 类型解析工具并合并为 AITool 列表
            var tools = ResolveToolsForAnalyst(agentType, currentMarket);

            // 显式传递 chatClient、合并后的工具列表和 AIContextProvider[]，其余由 DI 自动解析
            var parameters = new List<object> { chatClient, tools };

            if (additionalProviders is { Length: > 0 })
                parameters.Add(additionalProviders);

            var agent = (AIAgent)ActivatorUtilities.CreateInstance(_serviceProvider, agentType, parameters.ToArray());

            // 通过 MAF Builder 模式附加 Token 追踪中间件
            var middlewareAgent = agent
                .AsBuilder()
                .Use(
                    runFunc: _tokenTracking.InvokeAsync,
                    runStreamingFunc: _tokenTracking.InvokeStreamingAsync)
                .Build();

            _logger.LogInformation(
                "成功创建分析师代理: {AgentType} (市场: {Market}，已附加中间件)",
                agentType.Name, currentMarket);

            return middlewareAgent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建分析师代理时发生错误: {AgentType}", agentType.Name);
            throw;
        }
    }

    /// <summary>
    /// 根据 Analyst 类上的 RequiresToolsAttribute 和市场类型解析工具，
    /// 调用 <see cref="IToolsProvider.GetFunctions"/> 合并为统一的 AITool 列表
    /// </summary>
    private IList<AITool> ResolveToolsForAnalyst(Type agentType, MarketType marketType)
    {
        var tools = new List<AITool>();
        var resolvedCount = 0;

        var toolAttributes = agentType.GetCustomAttributes<RequiresToolsAttribute>().ToList();
        foreach (var attr in toolAttributes)
        {
            var toolService = _serviceProvider.GetKeyedService(attr.ToolInterfaceType, marketType);
            if (toolService is IToolsProvider provider)
            {
                tools.AddRange(provider.GetFunctions());
                resolvedCount++;
            }
            else
            {
                _logger.LogError(
                    "未找到 Analyst {AgentType} 所需的工具 {ToolType}（市场: {Market}），分析能力可能受限",
                    agentType.Name, attr.ToolInterfaceType.Name, marketType);
            }
        }

        if (resolvedCount != toolAttributes.Count)
        {
            _logger.LogError(
                "Analyst {AgentType} 缺少 {Missing}/{Total} 个工具（市场: {Market}）",
                agentType.Name, toolAttributes.Count - resolvedCount, toolAttributes.Count, marketType);
        }

        return tools;
    }
}

