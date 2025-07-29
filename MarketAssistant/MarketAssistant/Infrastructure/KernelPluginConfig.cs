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

    public KernelPluginConfig(
        IHttpClientFactory httpClientFactory,
        IUserSettingService userSettingService,
        IServiceProvider serviceProvider)
    {
        _stockBasicPlugin = new StockBasicPlugin(httpClientFactory, userSettingService);
        _stockTechnicalPlugin = new StockTechnicalPlugin(httpClientFactory, userSettingService);
        _stockFinancialPlugin = new StockFinancialPlugin(httpClientFactory, userSettingService);
        _stockNewsPlugin = new StockNewsPlugin(serviceProvider);
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
            default:
                break;
        }
        return k;
    }
}
