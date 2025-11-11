using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Services.Cache;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Moq;

namespace TestMarketAssistant;

/// <summary>
/// 分析缓存服务测试类，验证基本的读取和写入功能
/// </summary>
[TestClass]
public class AnalysisCacheServiceTest
{
    private Mock<ILogger<AnalysisCacheService>> _mockLogger;
    private IMemoryCache _memoryCache;
    private AnalysisCacheService _cacheService;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<AnalysisCacheService>>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());
        _cacheService = new AnalysisCacheService(_mockLogger.Object, _memoryCache);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _cacheService?.Dispose();
        _memoryCache?.Dispose();
    }

    /// <summary>
    /// 测试缓存分析数据的写入功能
    /// </summary>
    [TestMethod]
    public async Task CacheAnalysisAsync_ShouldSaveDataSuccessfully()
    {
        // Arrange
        var stockSymbol = "AAPL";
        var analysisResult = CreateTestAnalysisReport(stockSymbol);

        // Act
        await _cacheService.CacheAnalysisAsync(stockSymbol, analysisResult);

        // Assert
        var cachedResult = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNotNull(cachedResult);
        Assert.AreEqual(stockSymbol, cachedResult.StockSymbol);
        Assert.AreEqual("买入", cachedResult.CoordinatorResult.InvestmentRating);
    }

    /// <summary>
    /// 测试缓存分析数据的读取功能
    /// </summary>
    [TestMethod]
    public async Task GetCachedAnalysisAsync_ShouldReturnCorrectData()
    {
        // Arrange
        var stockSymbol = "MSFT";
        var analysisResult = CreateTestAnalysisReport(stockSymbol);
        await _cacheService.CacheAnalysisAsync(stockSymbol, analysisResult);

        // Act
        var result = await _cacheService.GetCachedAnalysisAsync(stockSymbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(stockSymbol, result.StockSymbol);
        Assert.AreEqual(8.5f, result.CoordinatorResult.OverallScore);
    }

    /// <summary>
    /// 测试读取不存在的缓存数据
    /// </summary>
    [TestMethod]
    public async Task GetCachedAnalysisAsync_WithNonExistentStock_ShouldReturnNull()
    {
        // Act
        var result = await _cacheService.GetCachedAnalysisAsync("NONEXISTENT");

        // Assert
        Assert.IsNull(result);
    }

    /// <summary>
    /// 测试缓存数据的覆盖写入功能
    /// </summary>
    [TestMethod]
    public async Task CacheAnalysisAsync_ShouldOverwriteExistingData()
    {
        // Arrange
        var stockSymbol = "GOOGL";
        var firstResult = CreateTestAnalysisReport(stockSymbol);
        firstResult.CoordinatorResult.InvestmentRating = "卖出";
        var secondResult = CreateTestAnalysisReport(stockSymbol);
        secondResult.CoordinatorResult.InvestmentRating = "买入";

        // Act
        await _cacheService.CacheAnalysisAsync(stockSymbol, firstResult);
        await _cacheService.CacheAnalysisAsync(stockSymbol, secondResult);

        // Assert
        var cachedResult = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNotNull(cachedResult);
        Assert.AreEqual("买入", cachedResult.CoordinatorResult.InvestmentRating);
    }

    private MarketAnalysisReport CreateTestAnalysisReport(string stockSymbol)
    {
        return new MarketAnalysisReport
        {
            StockSymbol = stockSymbol,
            AnalystMessages = new List<ChatMessage>
            {
                new(ChatRole.Assistant, "Test content") { AuthorName = "TestAnalyst" }
            },
            CoordinatorResult = new CoordinatorResult
            {
                InvestmentRating = "买入",
                OverallScore = 8.5f,
                TargetPrice = "180-200美元"
            }
        };
    }
}
