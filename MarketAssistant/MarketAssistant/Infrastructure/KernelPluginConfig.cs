using MarketAssistant.Agents;
using MarketAssistant.Plugins;
using Microsoft.SemanticKernel;

namespace MarketAssistant.Infrastructure;

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
        _groundingSearchPlugin = new GroundingSearchPlugin();
    }
    public Kernel PluginConfig(Kernel kernel, AnalysisAgents analysisAgent)
    {
        var k = kernel.Clone();
        switch (analysisAgent)
        {
            case AnalysisAgents.FundamentalAnalystAgent:
                k.Plugins.AddFromObject(_stockBasicPlugin);
                break;
            case AnalysisAgents.TechnicalAnalystAgent:
                k.Plugins.AddFromObject(_stockTechnicalPlugin);
                break;
            case AnalysisAgents.FinancialAnalystAgent:
                k.Plugins.AddFromObject(_stockFinancialPlugin);
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
