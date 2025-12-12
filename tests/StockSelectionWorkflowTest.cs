using MarketAssistant.Agents.StockSelection;
using MarketAssistant.Applications.StockSelection.Models;
using Microsoft.Extensions.DependencyInjection;

namespace TestMarketAssistant;

/// <summary>
/// 选股工作流测试（最小原则：验证核心工作流功能）
/// </summary>
[TestClass]
public sealed class StockSelectionWorkflowTest : BaseAgentTest
{
    private StockSelectionWorkflow _workflow = null!;

    [TestInitialize]
    public void Initialize()
    {
        _workflow = _serviceProvider.GetRequiredService<StockSelectionWorkflow>();
    }

    [TestCleanup]
    public void Cleanup()
    {
        _workflow?.Dispose();
    }

    [TestMethod]
    public async Task AnalyzeUserRequirement_ShouldReturnValidResult()
    {
        // Arrange
        var request = new StockRecommendationRequest
        {
            UserRequirements = "寻找市值大于100亿的成长股，ROE要大于15%",
            RiskPreference = "moderate",
            InvestmentAmount = 500000m,
            InvestmentHorizon = 180
        };

        // Act
        var result = await _workflow.AnalyzeUserRequirementAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0 && result.ConfidenceScore <= 100);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AnalysisSummary));

        Console.WriteLine($"用户需求分析完成 - 推荐数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public async Task AnalyzeNewsHotspot_ShouldReturnValidResult()
    {
        // Arrange
        var request = new NewsBasedSelectionRequest
        {
            NewsContent = "人工智能技术取得重大突破，相关概念股受到市场追捧",
            MaxRecommendations = 5
        };

        // Act
        var result = await _workflow.AnalyzeNewsHotspotAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0 && result.ConfidenceScore <= 100);
        Assert.IsTrue(result.Recommendations.Count <= request.MaxRecommendations);

        Console.WriteLine($"新闻热点分析完成 - 推荐数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }
}
