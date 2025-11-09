using MarketAssistant.Agents;
using MarketAssistant.Agents.Plugins;
using MarketAssistant.Infrastructure.Factories;
using MarketAssistant.Rag.Interfaces;
using MarketAssistant.Services.Settings;
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
    public Kernel PluginConfig(Kernel kernel, AnalysisAgent agent)
    {
        var k = kernel.Clone();
        
        if (agent == AnalysisAgent.FundamentalAnalyst)
        {
            k.Plugins.AddFromObject(_stockBasicPlugin);
        }
        else if (agent == AnalysisAgent.TechnicalAnalyst)
        {
            k.Plugins.AddFromObject(_stockTechnicalPlugin);
        }
        else if (agent == AnalysisAgent.FinancialAnalyst)
        {
            k.Plugins.AddFromObject(_stockFinancialPlugin);
        }
        else if (agent == AnalysisAgent.MarketSentimentAnalyst)
        {
            k.Plugins.AddFromType<SearchUrlPlugin>();
        }
        else if (agent == AnalysisAgent.NewsEventAnalyst)
        {
            k.Plugins.AddFromObject(_stockNewsPlugin);
        }
        else if (agent == AnalysisAgent.CoordinatorAnalyst)
        {
            k.Plugins.AddFromObject(_groundingSearchPlugin);
        }
        
        return k;
    }
}

