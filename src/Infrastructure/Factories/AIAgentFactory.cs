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
    AIAgent CreateAgent(AnalysisAgents agentType);
}

/// <summary>
/// AI Agent 工厂实现
/// 创建配置好模型和工具的 AIAgent，支持 YAML 配置和 Kernel 插件
/// </summary>
public class AIAgentFactory : IAIAgentFactory
{
    private readonly IChatClientFactory _chatClientFactory;
    private readonly IKernelPluginConfig _kernelPluginConfig;
    private readonly ILogger<AIAgentFactory> _logger;
    private readonly Kernel _kernel;

    /// <summary>
    /// 全局分析准则，应用于所有分析师
    /// </summary>
    private const string GlobalGuidelines = @"
        ## 分析准则
        - 采用1-10分量化评估
        - 提供具体价格点位和数值区间
        - 控制总字数300字内
        - 直接输出专业分析，无需询问
        ";

    public AIAgentFactory(
        IChatClientFactory chatClientFactory,
        IKernelPluginConfig kernelPluginConfig,
        ILogger<AIAgentFactory> logger,
        Kernel kernel)
    {
        _chatClientFactory = chatClientFactory;
        _kernelPluginConfig = kernelPluginConfig;
        _logger = logger;
        _kernel = kernel;
    }

    public AIAgent CreateAgent(AnalysisAgents agentType)
    {
        // 1. 加载 YAML 配置
        var templateConfig = LoadAgentConfiguration(agentType);

        // 2. 配置 Kernel 插件
        var kernel = _kernelPluginConfig.PluginConfig(_kernel, agentType);

        // 3. 创建 ChatClient
        var chatClient = _chatClientFactory.CreateClient();

        // 4. 转换插件为 AITool 列表
        var tools = new List<AITool>();
        foreach (var plugin in kernel.Plugins)
        {
            var aiFunctions = plugin.AsAIFunctions();
            foreach (var function in aiFunctions)
            {
                tools.Add(function);
            }
        }

        // 5. 创建 AIAgent
        var agent = chatClient.CreateAIAgent(
            instructions: templateConfig.Template + "\n\n" + GlobalGuidelines,
            name: templateConfig.Name,
            description: templateConfig.Description,
            tools: tools
        );

        _logger.LogInformation("成功创建 AIAgent: {AgentName} ({AgentType}), 工具数量: {ToolCount}", 
            agent.Name, agentType, tools.Count);

        return agent;
    }

    /// <summary>
    /// 加载分析师的 YAML 配置文件
    /// </summary>
    private PromptTemplateConfig LoadAgentConfiguration(AnalysisAgents agentType)
    {
        string agentYamlPath = Path.Combine(
            Directory.GetCurrentDirectory(),
            "Agents",
            "Yaml",
            $"{agentType}.yaml"
        );

        if (!File.Exists(agentYamlPath))
        {
            _logger.LogError("未找到分析师配置文件: {Path}", agentYamlPath);
            throw new FileNotFoundException($"未找到分析师配置文件: {agentYamlPath}");
        }

        string yamlContent = File.ReadAllText(agentYamlPath);
        var templateConfig = KernelFunctionYaml.ToPromptTemplateConfig(yamlContent);

        _logger.LogInformation("成功加载 YAML 配置: {AgentName} from {Path}", agentType, agentYamlPath);

        return templateConfig;
    }

    /// <summary>
    /// 获取指定代理类型的默认指令（后备方案，当 YAML 不存在时使用）
    /// </summary>
    private string GetDefaultInstructions(AnalysisAgents agentType)
    {
        return agentType switch
        {
            AnalysisAgents.FundamentalAnalystAgent =>
                "你是一位专业的基本面分析师，擅长分析公司财务数据、行业地位和竞争优势。",

            AnalysisAgents.TechnicalAnalystAgent =>
                "你是一位资深的技术分析师，精通K线形态、技术指标和趋势分析。",

            AnalysisAgents.FinancialAnalystAgent =>
                "你是一位财务分析专家，擅长解读财务报表、分析盈利能力和财务健康状况。",

            AnalysisAgents.NewsEventAnalystAgent =>
                "你是一位新闻事件分析师，善于捕捉市场热点、解读新闻对股价的影响。",

            AnalysisAgents.MarketSentimentAnalystAgent =>
                "你是一位市场情绪分析师，擅长分析市场氛围、投资者情绪和资金流向。",

            AnalysisAgents.CoordinatorAnalystAgent =>
                "你是一位综合分析协调员，负责整合各方分析结果，给出全面的投资建议。",

            _ => "你是一位专业的金融分析助手。"
        };
    }
}

