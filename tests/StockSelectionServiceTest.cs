using MarketAssistant.Agents;
using MarketAssistant.Applications.StockSelection;
using MarketAssistant.Infrastructure.Factories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public sealed class StockSelectionServiceTest : BaseKernelTest
{
    private StockSelectionService _stockSelectionService = null!;
    private StockSelectionManager _stockSelectionManager = null!;
    private Mock<ILogger<StockSelectionService>> _mockLogger = null!;

    [TestInitialize]
    public void Initialize()
    {
        var managerLogger = new Mock<ILogger<StockSelectionManager>>();
        // 直接使用测试 Kernel 上下文中的 IKernelFactory
        var factory = _kernel.Services.GetRequiredService<IKernelFactory>();
        _stockSelectionManager = new StockSelectionManager(_kernel.Services, factory, managerLogger.Object);

        // 创建服务日志Mock
        _mockLogger = new Mock<ILogger<StockSelectionService>>();

        // 创建选股服务
        _stockSelectionService = new StockSelectionService(_stockSelectionManager, _mockLogger.Object);
    }

    // 不再需要回调委托

    [TestCleanup]
    public void Cleanup()
    {
        _stockSelectionService?.Dispose();
    }

    #region 构造函数测试

    [TestMethod]
    public void TestStockSelectionService_Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act & Assert
        Assert.IsNotNull(_stockSelectionService);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void TestStockSelectionService_Constructor_WithNullManager_ShouldThrowArgumentNullException()
    {
        // Act
        new StockSelectionService(null!, _mockLogger.Object);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void TestStockSelectionService_Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act
        new StockSelectionService(_stockSelectionManager, null!);
    }

    #endregion

    #region 功能1: 根据用户需求推荐股票

    [TestMethod]
    public async Task TestStockSelectionService_RecommendStocksByUserRequirementAsync_WithValidRequest_ShouldReturnResult()
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

        Console.WriteLine($"=== 用户需求选股测试结果 ===");
        Console.WriteLine($"用户需求: {request.UserRequirements}");
        Console.WriteLine($"推荐股票数量: {result.Recommendations.Count}");
        Console.WriteLine($"置信度: {result.ConfidenceScore:F1}%");
        Console.WriteLine($"分析摘要: {result.AnalysisSummary}");
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public async Task TestStockSelectionService_RecommendStocksByUserRequirementAsync_WithNullRequest_ShouldThrowException()
    {
        // Act
        await _stockSelectionService.RecommendStocksByUserRequirementAsync(null!);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public async Task TestStockSelectionService_RecommendStocksByUserRequirementAsync_WithEmptyRequirements_ShouldThrowException()
    {
        // Arrange
        var request = new StockRecommendationRequest
        {
            UserRequirements = ""
        };

        // Act
        await _stockSelectionService.RecommendStocksByUserRequirementAsync(request);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentException))]
    public async Task TestStockSelectionService_RecommendStocksByUserRequirementAsync_WithWhitespaceRequirements_ShouldThrowException()
    {
        // Arrange
        var request = new StockRecommendationRequest
        {
            UserRequirements = "   "
        };

        // Act
        await _stockSelectionService.RecommendStocksByUserRequirementAsync(request);
    }

    #endregion

    #region 功能2: 根据热点新闻推荐股票

    [TestMethod]
    public async Task TestStockSelectionService_RecommendStocksByNewsHotspotAsync_WithValidRequest_ShouldReturnResult()
    {
        // Arrange
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
        Assert.IsTrue(result.ConfidenceScore >= 0 && result.ConfidenceScore <= 100);
        Assert.IsTrue(result.Recommendations.Count <= request.MaxRecommendations);

        // 验证新闻热点标识被添加
        foreach (var recommendation in result.Recommendations)
        {
            Assert.IsTrue(recommendation.Reason.Contains("[新闻热点]"));
        }

        Console.WriteLine($"=== 新闻热点选股测试结果 ===");
        Console.WriteLine($"新闻内容: {request.NewsContent}");
        Console.WriteLine($"推荐股票数量: {result.Recommendations.Count}");
        Console.WriteLine($"置信度: {result.ConfidenceScore:F1}%");
    }

    [TestMethod]
    public async Task TestStockSelectionService_RecommendStocksByNewsHotspotAsync_WithNullRequest_ShouldUseDefaultRequest()
    {
        // Act
        var result = await _stockSelectionService.RecommendStocksByNewsAsync(null!);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);

        Console.WriteLine($"=== 空新闻请求测试结果 ===");
        Console.WriteLine($"推荐股票数量: {result.Recommendations.Count}");
        Console.WriteLine($"置信度: {result.ConfidenceScore:F1}%");
    }

    #endregion

    #region 功能4: 快速选股（预设策略）

    [TestMethod]
    public async Task TestStockSelectionService_QuickSelectAsync_ValueStocks_ShouldReturnFormattedResult()
    {
        // Act
        var result = await _stockSelectionService.QuickSelectAsync(QuickSelectionStrategy.ValueStocks);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.Recommendations.Count >= 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AnalysisSummary));
        Assert.AreEqual("user_request", result.SelectionType);

        Console.WriteLine($"=== 价值股快速选股测试结果 ===");
        Console.WriteLine($"推荐股票数量: {result.Recommendations.Count}");
        Console.WriteLine($"分析摘要: {result.AnalysisSummary.Substring(0, Math.Min(100, result.AnalysisSummary.Length))}...");
        Console.WriteLine($"置信度: {result.ConfidenceScore}%");
    }

    [TestMethod]
    public async Task TestStockSelectionService_QuickSelectAsync_GrowthStocks_ShouldReturnFormattedResult()
    {
        // Act
        var result = await _stockSelectionService.QuickSelectAsync(QuickSelectionStrategy.GrowthStocks);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsNotNull(result.Recommendations);
        Assert.IsTrue(result.Recommendations.Count >= 0);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.AnalysisSummary));

        Console.WriteLine($"=== 成长股快速选股测试结果 ===");
        Console.WriteLine($"推荐股票数量: {result.Recommendations.Count}");
        Console.WriteLine($"分析摘要: {result.AnalysisSummary.Substring(0, Math.Min(100, result.AnalysisSummary.Length))}...");
    }

    [TestMethod]
    public async Task TestStockSelectionService_QuickSelectAsync_AllStrategies_ShouldWork()
    {
        // Arrange
        var strategies = new[]
        {
            QuickSelectionStrategy.ValueStocks,
            QuickSelectionStrategy.GrowthStocks,
            QuickSelectionStrategy.ActiveStocks,
            QuickSelectionStrategy.LargeCap,
            QuickSelectionStrategy.SmallCap,
            QuickSelectionStrategy.Dividend
        };

        // Act & Assert
        foreach (var strategy in strategies)
        {
            var result = await _stockSelectionService.QuickSelectAsync(strategy);
            Assert.IsNotNull(result);
            Assert.IsNotNull(result.Recommendations);
            Assert.IsTrue(result.Recommendations.Count >= 0);

            Console.WriteLine($"策略 {strategy} 测试通过，推荐股票数量: {result.Recommendations.Count}");
        }
    }

    #endregion

    #region 功能5: 获取快速选股策略列表

    [TestMethod]
    public void TestStockSelectionService_GetQuickSelectionStrategies_ShouldReturnAllStrategies()
    {
        // Act
        var strategies = _stockSelectionService.GetQuickSelectionStrategies();

        // Assert
        Assert.IsNotNull(strategies);
        Assert.AreEqual(6, strategies.Count);

        // 验证所有策略都存在
        var strategyTypes = strategies.Select(s => s.Strategy).ToList();
        Assert.IsTrue(strategyTypes.Contains(QuickSelectionStrategy.ValueStocks));
        Assert.IsTrue(strategyTypes.Contains(QuickSelectionStrategy.GrowthStocks));
        Assert.IsTrue(strategyTypes.Contains(QuickSelectionStrategy.ActiveStocks));
        Assert.IsTrue(strategyTypes.Contains(QuickSelectionStrategy.LargeCap));
        Assert.IsTrue(strategyTypes.Contains(QuickSelectionStrategy.SmallCap));
        Assert.IsTrue(strategyTypes.Contains(QuickSelectionStrategy.Dividend));

        // 验证每个策略的基本信息
        foreach (var strategy in strategies)
        {
            Assert.IsFalse(string.IsNullOrEmpty(strategy.Name));
            Assert.IsFalse(string.IsNullOrEmpty(strategy.Description));
            Assert.IsFalse(string.IsNullOrEmpty(strategy.Scenario));
            Assert.IsFalse(string.IsNullOrEmpty(strategy.RiskLevel));
        }

        Console.WriteLine($"=== 快速选股策略列表测试结果 ===");
        foreach (var strategy in strategies)
        {
            Console.WriteLine($"策略: {strategy.Name}, 风险等级: {strategy.RiskLevel}");
        }
    }

    #endregion

    #region 资源管理测试

    [TestMethod]
    public void TestStockSelectionService_Dispose_ShouldNotThrowException()
    {
        // Arrange
        var tempService = new StockSelectionService(_stockSelectionManager, _mockLogger.Object);

        // Act & Assert
        try
        {
            tempService.Dispose();
        }
        catch (Exception ex)
        {
            Assert.Fail($"Dispose方法不应该抛出异常，但得到: {ex.GetType().Name}: {ex.Message}");
        }
    }

    [TestMethod]
    public void TestStockSelectionService_Dispose_MultipleCalls_ShouldNotThrowException()
    {
        // Arrange
        var tempService = new StockSelectionService(_stockSelectionManager, _mockLogger.Object);

        // Act & Assert
        try
        {
            tempService.Dispose();
            tempService.Dispose(); // 第二次调用应该是安全的
        }
        catch (Exception ex)
        {
            Assert.Fail($"多次调用Dispose方法不应该抛出异常，但得到: {ex.GetType().Name}: {ex.Message}");
        }
    }

    #endregion
}