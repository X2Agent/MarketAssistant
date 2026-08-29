using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Applications.AssetScreener.Models;
using Microsoft.Extensions.DependencyInjection;

namespace MarketAssistant.Agents.InvestmentSelection;

/// <summary>
/// 基于 DI 容器的投资选择 Executor 工厂实现，每次调用解析新的 Transient 实例。
/// </summary>
public sealed class InvestmentExecutorFactory : IInvestmentExecutorFactory
{
    private readonly IServiceProvider _serviceProvider;

    public InvestmentExecutorFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public GenerateCriteriaExecutor<StockCriteria> CreateStockCriteriaExecutor()
        => _serviceProvider.GetRequiredService<GenerateCriteriaExecutor<StockCriteria>>();

    public GenerateCriteriaExecutor<CryptoCriteria> CreateCryptoCriteriaExecutor()
        => _serviceProvider.GetRequiredService<GenerateCriteriaExecutor<CryptoCriteria>>();

    public ScreenInvestmentTargetsExecutor CreateScreenTargetsExecutor()
        => _serviceProvider.GetRequiredService<ScreenInvestmentTargetsExecutor>();

    public AnalyzeAssetsExecutor CreateAnalyzeAssetsExecutor()
        => _serviceProvider.GetRequiredService<AnalyzeAssetsExecutor>();
}
