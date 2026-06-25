using MarketAssistant.Agents.MarketAnalysis.Models;
using MarketAssistant.Applications.Settings;
using MarketAssistant.Services.Cache;
using MarketAssistant.Services.Market;
using MarketAssistant.Services.Settings;
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
    private Mock<ILogger<AnalysisCacheService>> _mockLogger = null!;
    private Mock<IUserSettingService> _mockSettingService = null!;
    private IMemoryCache _memoryCache = null!;
    private AnalysisCacheService _cacheService = null!;
    private MarketContext _marketContext = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<AnalysisCacheService>>();
        _memoryCache = new MemoryCache(new MemoryCacheOptions());

        _mockSettingService = new Mock<IUserSettingService>();
        _mockSettingService.Setup(s => s.CurrentSetting).Returns(new UserSetting());
        var mockServiceProvider = new Mock<IServiceProvider>();
        _marketContext = new MarketContext(_mockSettingService.Object, mockServiceProvider.Object);

        _cacheService = new AnalysisCacheService(_mockLogger.Object, _memoryCache, _marketContext, _mockSettingService.Object);
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
    [TestCategory("Unit")]
    public async Task CacheAnalysisAsync_ShouldSaveDataSuccessfully()
    {
        // Arrange
        var assetSymbol = "AAPL";
        var analysisResult = CreateTestAnalysisReport(assetSymbol);

        // Act
        await _cacheService.CacheAnalysisAsync(assetSymbol, analysisResult);

        // Assert
        var cachedResult = await _cacheService.GetCachedAnalysisAsync(assetSymbol);
        Assert.IsNotNull(cachedResult);
        Assert.AreEqual(assetSymbol, cachedResult.AssetSymbol);
        Assert.AreEqual(InvestmentRating.Buy, cachedResult.CoordinatorResult.InvestmentRating);
    }

    /// <summary>
    /// 测试缓存分析数据的读取功能
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
    public async Task GetCachedAnalysisAsync_ShouldReturnCorrectData()
    {
        // Arrange
        var assetSymbol = "MSFT";
        var analysisResult = CreateTestAnalysisReport(assetSymbol);
        await _cacheService.CacheAnalysisAsync(assetSymbol, analysisResult);

        // Act
        var result = await _cacheService.GetCachedAnalysisAsync(assetSymbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(assetSymbol, result.AssetSymbol);
        Assert.AreEqual(8.5f, result.CoordinatorResult.OverallScore);
    }

    /// <summary>
    /// 测试读取不存在的缓存数据
    /// </summary>
    [TestMethod]
    [TestCategory("Unit")]
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
    [TestCategory("Unit")]
    public async Task CacheAnalysisAsync_ShouldOverwriteExistingData()
    {
        // Arrange
        var assetSymbol = "GOOGL";
        var firstResult = CreateTestAnalysisReport(assetSymbol);
        firstResult.CoordinatorResult.InvestmentRating = InvestmentRating.Sell;
        var secondResult = CreateTestAnalysisReport(assetSymbol);
        secondResult.CoordinatorResult.InvestmentRating = InvestmentRating.Buy;

        // Act
        await _cacheService.CacheAnalysisAsync(assetSymbol, firstResult);
        await _cacheService.CacheAnalysisAsync(assetSymbol, secondResult);

        // Assert
        var cachedResult = await _cacheService.GetCachedAnalysisAsync(assetSymbol);
        Assert.IsNotNull(cachedResult);
        Assert.AreEqual(InvestmentRating.Buy, cachedResult.CoordinatorResult.InvestmentRating);
    }

    private MarketAnalysisReport CreateTestAnalysisReport(string assetSymbol)
    {
        return new MarketAnalysisReport
        {
            AssetSymbol = assetSymbol,
            AnalystMessages = new List<ChatMessage>
            {
                new(ChatRole.Assistant, "Test content") { AuthorName = "TestAnalyst" }
            },
            CoordinatorResult = new CoordinatorResult
            {
                InvestmentRating = InvestmentRating.Buy,
                OverallScore = 8.5f,
                TargetPrice = "180-200美元"
            }
        };
    }
}
