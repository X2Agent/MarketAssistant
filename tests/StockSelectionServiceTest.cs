using MarketAssistant.Agents.StockSelection;
using MarketAssistant.Applications.StockSelection;
using MarketAssistant.Applications.StockSelection.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// 选股服务测试（最小原则：仅验证业务层独有逻辑）
/// 注：底层工作流逻辑由 StockSelectionWorkflowTest 覆盖
/// </summary>
[TestClass]
public sealed class StockSelectionServiceTest : BaseAgentTest
{
    private StockSelectionService _stockSelectionService = null!;
    private StockSelectionWorkflow _stockSelectionWorkflow = null!;
    private Mock<ILogger<StockSelectionService>> _mockLogger = null!;

    [TestInitialize]
    public void Initialize()
    {
        _stockSelectionWorkflow = _serviceProvider.GetRequiredService<StockSelectionWorkflow>();
        _mockLogger = new Mock<ILogger<StockSelectionService>>();
        _stockSelectionService = new StockSelectionService(_stockSelectionWorkflow, _mockLogger.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _stockSelectionService?.Dispose();
    }

    [TestMethod]
    public async Task RecommendStocksByUserRequirement_ShouldReturnValidResult()
    {
        // Arrange
        var request = new StockRecommendationRequest
        {
            UserRequirements = "寻找价值股投资机会",
            RiskPreference = "conservative",
            InvestmentAmount = 100000m,
            InvestmentHorizon = 365
        };

        // Act
        var result = await _stockSelectionService.RecommendStocksByUserRequirementAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0 && result.ConfidenceScore <= 100);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AnalysisSummary));

        Console.WriteLine($"用户需求选股 - 推荐数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public async Task RecommendStocksByNews_ShouldReturnResultWithNewsTag()
    {
        // Arrange - 测试业务层添加新闻标签的逻辑
        var request = new NewsBasedSelectionRequest
        {
            NewsContent = "人工智能技术取得重大突破，相关概念股受到市场追捧",
            MaxRecommendations = 5
        };

        // Act
        var result = await _stockSelectionService.RecommendStocksByNewsAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.Recommendations.Count <= request.MaxRecommendations);

        // 验证业务层特有逻辑：新闻热点标识被添加
        if (result.Recommendations.Count > 0)
        {
            foreach (var recommendation in result.Recommendations)
            {
                Assert.IsTrue(recommendation.Reason.Contains("[新闻热点]"),
                    "业务层应该为新闻推荐添加 [新闻热点] 标签");
            }
        }

        Console.WriteLine($"新闻推荐 - 推荐数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public async Task QuickSelect_ShouldReturnValidResult()
    {
        // Arrange - 测试业务层策略转换逻辑
        var strategy = QuickSelectionStrategy.ValueStocks;

        // Act
        var result = await _stockSelectionService.QuickSelectAsync(strategy);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AnalysisSummary));

        Console.WriteLine($"快速选股({strategy}) - 推荐数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public void GetQuickSelectionStrategies_ShouldReturnCompleteStrategies()
    {
        // Act - 测试业务层提供的策略元数据
        var strategies = _stockSelectionService.GetQuickSelectionStrategies();

        // Assert
        Assert.IsNotNull(strategies);
        Assert.IsTrue(strategies.Count > 0, "应该返回至少一个策略");

        foreach (var strategyInfo in strategies)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(strategyInfo.Name), "策略名称不能为空");
            Assert.IsFalse(string.IsNullOrWhiteSpace(strategyInfo.Description), "策略描述不能为空");
            Assert.IsFalse(string.IsNullOrWhiteSpace(strategyInfo.Scenario), "适用场景不能为空");
            Assert.IsFalse(string.IsNullOrWhiteSpace(strategyInfo.RiskLevel), "风险等级不能为空");
        }

        Console.WriteLine($"策略列表 - 总数: {strategies.Count}");
    }
}
