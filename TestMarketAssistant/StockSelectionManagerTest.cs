using MarketAssistant.Agents;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public sealed class StockSelectionManagerTest : BaseKernelTest
{
    private StockSelectionManager _stockSelectionManager = null!;

    [TestInitialize]
    public void Initialize()
    {
        // 创建选股管理器，使用基类提供的_kernel
        var managerLogger = new Mock<ILogger<StockSelectionManager>>();
        _stockSelectionManager = new StockSelectionManager(_kernel, managerLogger.Object);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _stockSelectionManager?.Dispose();
    }

    [TestMethod]
    public async Task TestStockSelectionManager_AnalyzeUserRequirementAsync_WithValidRequest_ShouldReturnResult()
    {
        // Arrange
        var request = new StockRecommendationRequest
        {
            UserRequirements = "市值大于1000万的旅游行业股票",
            RiskPreference = "conservative"
        };

        // Act
        var result = await _stockSelectionManager.AnalyzeUserRequirementAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0);
        Assert.IsTrue(result.ConfidenceScore <= 100);

        Console.WriteLine($"用户需求分析完成，推荐股票数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public async Task TestStockSelectionManager_AnalyzeUserRequirementAsync_WithScreeningRequest_ShouldUseStockScreener()
    {
        // Arrange - 测试需要使用筛选功能的用户需求
        var request = new StockRecommendationRequest
        {
            UserRequirements = "我想找一些市值100亿以上的成长股，ROE要大于15%，近期涨幅不要太大",
            RiskPreference = "moderate",
            InvestmentAmount = 500000m,
            InvestmentHorizon = 180
        };

        // Act
        var result = await _stockSelectionManager.AnalyzeUserRequirementAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0);
        Assert.IsTrue(result.ConfidenceScore <= 100);

        Console.WriteLine($"=== 股票筛选需求分析测试结果 ===");
        Console.WriteLine($"用户需求: {request.UserRequirements}");
        Console.WriteLine($"推荐股票数量: {result.Recommendations.Count}");
        Console.WriteLine($"置信度: {result.ConfidenceScore:F1}%");
        Console.WriteLine($"分析摘要: {result.AnalysisSummary}");

        if (result.Recommendations.Any())
        {
            Console.WriteLine("\n推荐股票:");
            foreach (var stock in result.Recommendations.Take(3))
            {
                Console.WriteLine($"- {stock.Name} ({stock.Symbol}): {stock.Reason}");
            }
        }
    }

    [TestMethod]
    public async Task TestStockSelectionManager_AnalyzeNewsHotspotAsync_WithValidRequest_ShouldReturnResult()
    {
        // Arrange
        var request = new NewsBasedSelectionRequest
        {
            NewsContent = "AI技术获得突破",
            MaxRecommendations = 5
        };

        // Act
        var result = await _stockSelectionManager.AnalyzeNewsHotspotAsync(request);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.ConfidenceScore >= 0);
        Assert.IsTrue(result.ConfidenceScore <= 100);

        Console.WriteLine($"新闻热点分析完成，推荐股票数量: {result.Recommendations.Count}, 置信度: {result.ConfidenceScore:F1}%");
    }
}
