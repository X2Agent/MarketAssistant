using MarketAssistant.Agents.Analysts;
using MarketAssistant.Agents.Tools.Abstractions;
using MarketAssistant.Infrastructure.Core;
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
    private readonly MarketContext _marketContext;
    private readonly ILogger<AnalystAgentFactory> _logger;

    public AnalystAgentFactory(
        IServiceProvider serviceProvider,
        IChatClientFactory chatClientFactory,
        MarketContext marketContext,
        ILogger<AnalystAgentFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _marketContext = marketContext ?? throw new ArgumentNullException(nameof(marketContext));
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

            // 根据当前市场类型获取对应的工具实现
            var currentMarket = _marketContext.CurrentMarket;

            // 根据 Analyst 类型获取需要的工具
            var tools = ResolveToolsForAnalyst(agentType, currentMarket);

            // 使用 ActivatorUtilities.CreateInstance
            // 显式传递 chatClient 和工具，其他依赖从 DI 获取
            var agent = (AIAgent)ActivatorUtilities.CreateInstance(_serviceProvider, agentType, chatClient, tools.ToArray());

            _logger.LogInformation(
                "成功创建分析师代理: {AgentType} (市场: {Market})",
                agentType.Name, currentMarket);

            return agent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建分析师代理时发生错误: {AgentType}", agentType.Name);
            throw;
        }
    }

    /// <summary>
    /// 根据 Analyst 类型和市场类型解析所需的工具
    /// </summary>
    private List<object> ResolveToolsForAnalyst(Type agentType, MarketType marketType)
    {
        var tools = new List<object>();

        // 根据不同的 Analyst 类型，解析对应的工具接口
        switch (agentType.Name)
        {
            case nameof(FinancialAnalystAgent):
                tools.Add(_serviceProvider.GetRequiredKeyedService<IFinancialDataTools>(marketType));
                break;

            case nameof(FundamentalAnalystAgent):
                tools.Add(_serviceProvider.GetRequiredKeyedService<IBasicDataTools>(marketType));
                break;

            case nameof(MarketSentimentAnalystAgent):
                tools.Add(_serviceProvider.GetRequiredKeyedService<IFinancialDataTools>(marketType));
                tools.Add(_serviceProvider.GetRequiredKeyedService<ISentimentDataTools>(marketType));
                break;

            case nameof(NewsEventAnalystAgent):
                tools.Add(_serviceProvider.GetRequiredKeyedService<INewsDataTools>(marketType));
                break;

            case nameof(TechnicalAnalystAgent):
                tools.Add(_serviceProvider.GetRequiredKeyedService<ITechnicalDataTools>(marketType));
                break;

            default:
                _logger.LogWarning("未知的 Analyst 类型: {AgentType}，不注入任何工具", agentType.Name);
                break;
        }

        return tools;
    }

    /// <summary>
    /// 创建指定类型的分析师代理（泛型版本）
    /// </summary>
    public TAgent CreateAnalyst<TAgent>() where TAgent : AnalystAgentBase
    {
        return (TAgent)CreateAnalyst(typeof(TAgent));
    }
}

