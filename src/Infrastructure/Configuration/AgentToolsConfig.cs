using MarketAssistant.Agents;
using MarketAssistant.Agents.Plugins;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Browser;
using MarketAssistant.Services.Settings;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace MarketAssistant.Infrastructure.Configuration;

/// <summary>
/// Agent 工具配置接口
/// 为不同类型的分析代理提供所需的工具列表
/// </summary>
public interface IAgentToolsConfig
{
    IList<AITool> GetToolsForAgent(AnalysisAgent agent);
}

/// <summary>
/// Agent 工具配置实现
/// 管理和提供不同分析代理所需的工具/插件
/// </summary>
public class AgentToolsConfig : IAgentToolsConfig
{
    private readonly StockBasicPlugin _stockBasicPlugin;
    private readonly StockTechnicalPlugin _stockTechnicalPlugin;
    private readonly StockFinancialPlugin _stockFinancialPlugin;
    private readonly StockNewsPlugin _stockNewsPlugin;
    private readonly GroundingSearchPlugin _groundingSearchPlugin;

    public AgentToolsConfig(
        IHttpClientFactory httpClientFactory,
        IUserSettingService userSettingService,
        IServiceProvider serviceProvider)
    {
        _stockBasicPlugin = new StockBasicPlugin(httpClientFactory, userSettingService);
        _stockTechnicalPlugin = new StockTechnicalPlugin(httpClientFactory, userSettingService);
        _stockFinancialPlugin = new StockFinancialPlugin(httpClientFactory, userSettingService);
        _stockNewsPlugin = new StockNewsPlugin(
            serviceProvider,
            serviceProvider.GetRequiredService<PlaywrightService>(),
            serviceProvider.GetRequiredService<IChatClientFactory>());

        var orchestrator = serviceProvider.GetRequiredService<IRetrievalOrchestrator>();
        var webTextSearchFactory = serviceProvider.GetRequiredService<IWebTextSearchFactory>();
        var logger = serviceProvider.GetService<ILogger<GroundingSearchPlugin>>();

        _groundingSearchPlugin = new GroundingSearchPlugin(orchestrator!, webTextSearchFactory!, userSettingService, logger!);
    }

    /// <summary>
    /// 获取指定分析代理所需的工具列表
    /// </summary>
    public IList<AITool> GetToolsForAgent(AnalysisAgent agent)
    {
        var tools = new List<AITool>();

        if (agent == AnalysisAgent.FundamentalAnalyst)
        {
            tools.AddRange(ConvertPluginToTools(_stockBasicPlugin));
        }
        else if (agent == AnalysisAgent.TechnicalAnalyst)
        {
            tools.AddRange(ConvertPluginToTools(_stockTechnicalPlugin));
        }
        else if (agent == AnalysisAgent.FinancialAnalyst)
        {
            tools.AddRange(ConvertPluginToTools(_stockFinancialPlugin));
        }
        else if (agent == AnalysisAgent.MarketSentimentAnalyst)
        {
            // 需要时添加 SearchUrlPlugin 工具
        }
        else if (agent == AnalysisAgent.NewsEventAnalyst)
        {
            tools.AddRange(ConvertPluginToTools(_stockNewsPlugin));
        }
        else if (agent == AnalysisAgent.CoordinatorAnalyst)
        {
            tools.AddRange(ConvertPluginToTools(_groundingSearchPlugin));
        }

        return tools;
    }

    /// <summary>
    /// 将插件实例转换为 Agent Framework 的 AIFunction 列表
    /// 使用 Semantic Kernel 的 AsAIFunctions 扩展方法
    /// </summary>
    private IEnumerable<AIFunction> ConvertPluginToTools(object pluginInstance)
    {
        var tools = new List<AIFunction>();

        // 使用 Semantic Kernel 的 KernelPluginFactory 创建 KernelPlugin
        var plugin = Microsoft.SemanticKernel.KernelPluginFactory.CreateFromObject(pluginInstance);

        // 使用 AsAIFunctions 扩展方法将 KernelPlugin 转换为 AIFunction
        var aiFunctions = plugin.AsAIFunctions();
        tools.AddRange(aiFunctions);

        return tools;
    }
}

