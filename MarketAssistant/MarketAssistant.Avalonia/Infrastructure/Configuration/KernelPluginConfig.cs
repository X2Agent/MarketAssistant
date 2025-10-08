using MarketAssistant.Agents;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Plugins;
using MarketAssistant.Services.Settings;
using MarketAssistant.Vectors.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Plugins.Web;

namespace MarketAssistant.Infrastructure.Configuration;

public class KernelPluginConfig : IKernelPluginConfig
{
    private readonly StockBasicPlugin _stockBasicPlugin;
    private readonly StockTechnicalPlugin _stockTechnicalPlugin;
    private readonly StockFinancialPlugin _stockFinancialPlugin;
    private readonly StockNewsPlugin _stockNewsPlugin;
    private readonly GroundingSearchPlugin _groundingSearchPlugin;

    public KernelPluginConfig(
        IHttpClientFactory httpClientFactory,
        IUserSettingService userSettingService,
        IServiceProvider serviceProvider)
    {
        _stockBasicPlugin = new StockBasicPlugin(httpClientFactory, userSettingService);
        _stockTechnicalPlugin = new StockTechnicalPlugin(httpClientFactory, userSettingService);
        _stockFinancialPlugin = new StockFinancialPlugin(httpClientFactory, userSettingService);
        _stockNewsPlugin = new StockNewsPlugin(serviceProvider);

        // 获取GroundingSearchPlugin的依赖
        var orchestrator = serviceProvider.GetRequiredService<IRetrievalOrchestrator>();
        var webTextSearchFactory = serviceProvider.GetRequiredService<IWebTextSearchFactory>();
        var logger = serviceProvider.GetService<ILogger<GroundingSearchPlugin>>();

        _groundingSearchPlugin = new GroundingSearchPlugin(orchestrator!, webTextSearchFactory!, userSettingService, logger!);
    }
    public Kernel PluginConfig(Kernel kernel, AnalysisAgents analysisAgent)
    {
        var k = kernel.Clone();
        switch (analysisAgent)
        {
            //DocumentPlugin
            case AnalysisAgents.FundamentalAnalystAgent:
                k.Plugins.AddFromObject(_stockBasicPlugin);
                break;
            case AnalysisAgents.TechnicalAnalystAgent:
                k.Plugins.AddFromObject(_stockTechnicalPlugin);
                break;
            case AnalysisAgents.FinancialAnalystAgent:
                k.Plugins.AddFromObject(_stockFinancialPlugin);
                break;
            case AnalysisAgents.MarketSentimentAnalystAgent:
                //k.Plugins.AddFromType<WebSearchEnginePlugin>();
                k.Plugins.AddFromType<SearchUrlPlugin>();
                break;
            case AnalysisAgents.NewsEventAnalystAgent:
                k.Plugins.AddFromObject(_stockNewsPlugin);
                break;
            case AnalysisAgents.CoordinatorAnalystAgent:
                k.Plugins.AddFromObject(_groundingSearchPlugin);
                break;
            default:
                break;
        }
        return k;
    }
}

