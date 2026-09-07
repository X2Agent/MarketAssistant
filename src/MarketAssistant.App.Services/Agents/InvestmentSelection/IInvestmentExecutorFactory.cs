using MarketAssistant.Agents.InvestmentSelection.Executors;
using MarketAssistant.Applications.AssetScreener.Models;

namespace MarketAssistant.Agents.InvestmentSelection;

/// <summary>
/// 投资选择工作流 Executor 工厂：每次 Run 创建全新 Executor 实例（Transient 注册），
/// 避免 Singleton Executor 在并发分析间共享可变状态与模型/运行时引用。
/// </summary>
public interface IInvestmentExecutorFactory
{
    GenerateCriteriaExecutor<StockCriteria> CreateStockCriteriaExecutor();

    GenerateCriteriaExecutor<CryptoCriteria> CreateCryptoCriteriaExecutor();

    ScreenInvestmentTargetsExecutor CreateScreenTargetsExecutor();

    AnalyzeAssetsExecutor CreateAnalyzeAssetsExecutor();
}
