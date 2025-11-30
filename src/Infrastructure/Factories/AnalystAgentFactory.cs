using MarketAssistant.Agents.Analysts;
using Microsoft.Agents.AI;
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
    /// 创建指定类型的分析师代理（泛型版本，提供编译时类型检查）
    /// </summary>
    /// <typeparam name="TAgent">代理类型，必须继承自 AnalystAgentBase</typeparam>
    TAgent CreateAnalyst<TAgent>() where TAgent : AnalystAgentBase;
}

/// <summary>
/// 分析师代理工厂实现
/// 负责创建配置好的分析师代理（使用 DI 容器）
/// </summary>
public class AnalystAgentFactory : IAnalystAgentFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IChatClientFactory _chatClientFactory;
    private readonly ILogger<AnalystAgentFactory> _logger;

    public AnalystAgentFactory(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        ILogger<AnalystAgentFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 根据类型创建对应的代理
    /// </summary>
    public AIAgent CreateAnalyst(Type agentType)
    {
        try
        {
            // 严格限制必须是 AnalystAgentBase 的子类
            if (!typeof(AnalystAgentBase).IsAssignableFrom(agentType))
            {
                throw new ArgumentException($"Type {agentType.Name} must inherit from AnalystAgentBase", nameof(agentType));
            }

            // 创建 ChatClient
            var chatClient = _chatClientFactory.CreateClient();

            // 使用 ActivatorUtilities.CreateInstance
            // 显式传递 chatClient，其他依赖从 DI 获取
            var agent = (AIAgent)ActivatorUtilities.CreateInstance(_serviceProvider, agentType, chatClient);

            _logger.LogInformation(
                "成功创建分析师代理: {AgentType}",
                agentType.Name);

            return agent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建分析师代理时发生错误: {AgentType}", agentType.Name);
            throw;
        }
    }

    /// <summary>
    /// 创建指定类型的分析师代理（泛型版本）
    /// </summary>
    public TAgent CreateAnalyst<TAgent>() where TAgent : AnalystAgentBase
    {
        return (TAgent)CreateAnalyst(typeof(TAgent));
    }
}

