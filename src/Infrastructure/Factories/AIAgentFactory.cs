using MarketAssistant.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;

namespace MarketAssistant.Infrastructure.Factories;

/// <summary>
/// AI Agent 工厂接口
/// 负责创建配置好模型和工具的 AIAgent（用于分析师角色）
/// </summary>
public interface IAIAgentFactory
{
    /// <summary>
    /// 创建指定类型的分析师 Agent
    /// </summary>
    AIAgent CreateAgent(AnalysisAgent agent);
}

/// <summary>
/// AI Agent 工厂实现
/// 创建配置好模型和工具的 AIAgent，支持 YAML 配置和 Kernel 插件
/// </summary>
public class AIAgentFactory : IAIAgentFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IKernelPluginConfig _kernelPluginConfig;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AIAgentFactory> _logger;
    private readonly Kernel _kernel;

    public AIAgentFactory(
        IChatClientFactory chatClientFactory,
        IKernelPluginConfig kernelPluginConfig,
        IServiceProvider serviceProvider,
        ILogger<AIAgentFactory> logger,
        Kernel kernel)
    {
        _chatClientFactory = chatClientFactory;
        _kernelPluginConfig = kernelPluginConfig;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _kernel = kernel;
    }

    public AIAgent CreateAgent(AnalysisAgent agent)
    {
        // 直接使用 AnalysisAgent 的配置（不再需要 Provider）
        // 配置 Kernel 插件
        var kernel = _kernelPluginConfig.PluginConfig(_kernel, agent);

        // 创建 ChatClient
        var chatClient = _chatClientFactory.CreateClient();

        // 转换插件为 AITool 列表
        var tools = new List<AITool>();
        foreach (var plugin in kernel.Plugins)
        {
            var aiFunctions = plugin.AsAIFunctions();
            foreach (var function in aiFunctions)
            {
                tools.Add(function);
            }
        }

        // 创建 AIAgent（直接使用 AnalysisAgent 的配置，已包含 GlobalGuidelines）
        var createdAgent = chatClient.CreateAIAgent(
            instructions: agent.Instructions,
            name: agent.Name,
            description: agent.Description,
            tools: tools
        );

        _logger.LogInformation("成功创建 AIAgent: {AgentName}, 工具数量: {ToolCount}", 
            createdAgent.Name, tools.Count);

        return createdAgent;
    }
}

