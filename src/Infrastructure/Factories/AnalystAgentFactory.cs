using MarketAssistant.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// 分析师代理工厂接口
/// </summary>
public interface IAnalystAgentFactory
{
    /// <summary>
    /// 创建指定类型的分析师代理
    /// </summary>
    AIAgent CreateAnalyst(AnalysisAgent agent);

    /// <summary>
    /// 批量创建分析师代理
    /// </summary>
    List<AIAgent> CreateAnalysts(IEnumerable<AnalysisAgent> agents);
}

/// <summary>
/// 分析师代理工厂实现
/// 负责创建配置好的 ChatClientAgent（Agent Framework 风格）
/// </summary>
public class AnalystAgentFactory : IAnalystAgentFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IAgentToolsConfig _toolsConfig;
    private readonly ILogger<AnalystAgentFactory> _logger;

    public AnalystAgentFactory(
        IChatClientFactory chatClientFactory,
        IAgentToolsConfig toolsConfig,
        ILogger<AnalystAgentFactory> logger)
    {
        _chatClientFactory = chatClientFactory ?? throw new ArgumentNullException(nameof(chatClientFactory));
        _toolsConfig = toolsConfig ?? throw new ArgumentNullException(nameof(toolsConfig));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 创建带有自定义 ChatOptions 的分析师代理（用于结构化输出）
    /// </summary>
    public AIAgent CreateAnalyst(AnalysisAgent analyst)
    {
        try
        {
            // 1. 获取工具列表（根据分析师 ID 映射）
            var tools = _toolsConfig.GetToolsForAgent(analyst);

            // 2. 创建 ChatClient 并配置默认 ChatOptions（如果提供）
            var chatClient = _chatClientFactory.CreateClient();

            // 3. 创建 ChatClientAgent
            var baseAgent = chatClient.CreateAIAgent(new ChatClientAgentOptions(analyst.Instructions, analyst.Name, analyst.Description)
            {
                ChatOptions = new ChatOptions
                {
                    TopK = analyst.TopK,
                    TopP = analyst.TopP,
                    Temperature = analyst.Temperature,
                    Tools = tools,
                    ResponseFormat = analyst.ResponseFormat
                }
            });

            _logger.LogInformation(
                "成功创建分析师代理: {AgentName}, 工具数量: {ToolCount}",
                analyst.Name,
                tools.Count);

            // 4. 添加代理运行中间件和函数调用中间件
            return baseAgent
                .AsBuilder()
                .Use(runFunc: CreateAgentRunMiddleware(analyst.Name), runStreamingFunc: null)
                .Use(CreateFunctionInvocationMiddleware(analyst.Name))
                .Build();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建分析师代理时发生错误: {AgentName}", analyst.Name);
            throw;
        }
    }

    /// <summary>
    /// 批量创建分析师代理
    /// </summary>
    public List<AIAgent> CreateAnalysts(IEnumerable<AnalysisAgent> agents)
    {
        var createdAgents = new List<AIAgent>();

        foreach (var agent in agents)
        {
            try
            {
                var createdAgent = CreateAnalyst(agent);
                createdAgents.Add(createdAgent);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "跳过创建分析师代理: {AgentName}", agent.Name);
            }
        }

        _logger.LogInformation("批量创建分析师代理完成，成功创建: {Count} 个", createdAgents.Count);
        return createdAgents;
    }

    /// <summary>
    /// 创建 Agent 运行日志中间件
    /// </summary>
    private Func<IEnumerable<ChatMessage>, AgentThread?, AgentRunOptions?, AIAgent, CancellationToken, Task<AgentRunResponse>> CreateAgentRunMiddleware(string agentName)
    {
        return async (messages, thread, options, innerAgent, cancellationToken) =>
        {
            _logger.LogInformation("Agent {AgentName} 运行开始，输入消息数: {Count}", agentName, messages.Count());
            var response = await innerAgent.RunAsync(messages, thread, options, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Agent {AgentName} 运行完成，输出消息数: {Count}", agentName, response.Messages.Count());
            return response;
        };
    }

    /// <summary>
    /// 创建函数调用日志中间件
    /// </summary>
    private Func<AIAgent, FunctionInvocationContext, Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>>, CancellationToken, ValueTask<object?>> CreateFunctionInvocationMiddleware(string agentName)
    {
        return async (agent, context, next, cancellationToken) =>
        {
            _logger.LogInformation("🔧 Agent {AgentName} 调用函数: {FunctionName}", agentName, context.Function.Name);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            object? result = null;

            try
            {
                result = await next(context, cancellationToken);
                _logger.LogInformation(
                    "✅ 函数 {FunctionName} 执行成功，耗时: {Duration:F3}秒",
                    context.Function.Name,
                    stopwatch.Elapsed.TotalSeconds);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "❌ 函数 {FunctionName} 执行失败: {Message}",
                    context.Function.Name,
                    ex.Message);
                throw;
            }
        };
    }
}
