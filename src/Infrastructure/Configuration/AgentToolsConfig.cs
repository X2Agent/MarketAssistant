using MarketAssistant.Agents;
using MarketAssistant.Agents.Plugins;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
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
    IList<AIFunction> GetToolsForAgent(AnalysisAgents analysisAgent);
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
        _stockNewsPlugin = new StockNewsPlugin(serviceProvider);

        var orchestrator = serviceProvider.GetRequiredService<IRetrievalOrchestrator>();
        var webTextSearchFactory = serviceProvider.GetRequiredService<IWebTextSearchFactory>();
        var logger = serviceProvider.GetService<ILogger<GroundingSearchPlugin>>();

        _groundingSearchPlugin = new GroundingSearchPlugin(orchestrator!, webTextSearchFactory!, userSettingService, logger!);
    }

    /// <summary>
    /// 获取指定分析代理所需的工具列表
    /// </summary>
    public IList<AIFunction> GetToolsForAgent(AnalysisAgents analysisAgent)
    {
        var tools = new List<AIFunction>();

        switch (analysisAgent)
        {
            case AnalysisAgents.FundamentalAnalystAgent:
                tools.AddRange(ConvertPluginToTools(_stockBasicPlugin));
                break;
            case AnalysisAgents.TechnicalAnalystAgent:
                tools.AddRange(ConvertPluginToTools(_stockTechnicalPlugin));
                break;
            case AnalysisAgents.FinancialAnalystAgent:
                tools.AddRange(ConvertPluginToTools(_stockFinancialPlugin));
                break;
            case AnalysisAgents.MarketSentimentAnalystAgent:
                // 需要时添加 SearchUrlPlugin 工具
                break;
            case AnalysisAgents.NewsEventAnalystAgent:
                tools.AddRange(ConvertPluginToTools(_stockNewsPlugin));
                break;
            case AnalysisAgents.CoordinatorAnalystAgent:
                tools.AddRange(ConvertPluginToTools(_groundingSearchPlugin));
                break;
            default:
                break;
        }

        return tools;
    }

    /// <summary>
    /// 将 Semantic Kernel 插件转换为 Agent Framework 工具
    /// 注意：这是临时实现，完整迁移需要使用 AIFunctionFactory.Create()
    /// </summary>
    private IEnumerable<AIFunction> ConvertPluginToTools(object plugin)
    {
        // TODO: 这里需要实际实现将插件方法转换为 AIFunction
        // 当前返回空列表，完整实现需要：
        // 1. 反射插件类中标记为 [KernelFunction] 的方法
        // 2. 使用 AIFunctionFactory.Create() 将方法转换为 AIFunction
        // 3. 或者重构插件使用新的注解系统
        return new List<AIFunction>();
    }
}

