using MarketAssistant.Applications.Cache;
using MarketAssistant.Views.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace TestMarketAssistant;

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
        _memoryCache = new MemoryCache(new MemoryCacheOptions
        {
            SizeLimit = 5 * 1024 * 1024 // 5MB for testing
        });
        
        _cacheService = new AnalysisCacheService(_mockLogger.Object, _memoryCache);
    }

    [TestCleanup]
    public void Cleanup()
    {
        _cacheService?.Dispose();
        _memoryCache?.Dispose();
    }

    [TestMethod]
    public async Task CacheAnalysisAsync_WithValidData_SavesSuccessfully()
    {
        // Arrange
        var stockSymbol = "AAPL";
        var analysisResult = CreateTestAnalysisResult(stockSymbol);

        // Act
        await _cacheService.CacheAnalysisAsync(stockSymbol, analysisResult);

        // Assert
        var cachedResult = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNotNull(cachedResult);
        Assert.AreEqual(stockSymbol, cachedResult.StockSymbol);
        Assert.AreEqual("买入", cachedResult.Rating);
        Assert.AreEqual(8.5f, cachedResult.OverallScore);
    }

    [TestMethod]
    public async Task GetCachedAnalysisAsync_WithNonExistentStock_ReturnsNull()
    {
        // Arrange
        var stockSymbol = "NONEXISTENT";

        // Act
        var result = await _cacheService.GetCachedAnalysisAsync(stockSymbol);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task GetCachedAnalysisAsync_WithExpiredCache_ReturnsNull()
    {
        // Arrange
        var stockSymbol = "AAPL";
        var analysisResult = CreateTestAnalysisResult(stockSymbol);

        // 直接设置一个很短的过期时间到内存缓存中
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMilliseconds(50),
            Size = 1000
        };
        _memoryCache.Set($"{stockSymbol.ToUpperInvariant()}_{DateTime.UtcNow:yyyyMMdd}", analysisResult, cacheOptions);

        // Act
        // 等待缓存过期
        await Task.Delay(100);

        // Assert
        var result = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task CacheAnalysisAsync_OverwritesExistingCache_WhenSameStockSymbol()
    {
        // Arrange
        var stockSymbol = "AAPL";
        var firstResult = CreateTestAnalysisResult(stockSymbol);
        firstResult.Rating = "卖出";
        var secondResult = CreateTestAnalysisResult(stockSymbol);
        secondResult.Rating = "买入";

        // Act
        await _cacheService.CacheAnalysisAsync(stockSymbol, firstResult);
        await _cacheService.CacheAnalysisAsync(stockSymbol, secondResult);

        // Assert
        var cachedResult = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNotNull(cachedResult);
        Assert.AreEqual("买入", cachedResult.Rating);
    }

    [TestMethod]
    public async Task ClearCacheAsync_WithSpecificStock_RemovesOnlyThatStock()
    {
        // Arrange
        var stockSymbol1 = "AAPL";
        var stockSymbol2 = "MSFT";
        var analysisResult1 = CreateTestAnalysisResult(stockSymbol1);
        var analysisResult2 = CreateTestAnalysisResult(stockSymbol2);

        await _cacheService.CacheAnalysisAsync(stockSymbol1, analysisResult1);
        await _cacheService.CacheAnalysisAsync(stockSymbol2, analysisResult2);

        // Act
        await _cacheService.ClearCacheAsync(stockSymbol1);

        // Assert
        var result1 = await _cacheService.GetCachedAnalysisAsync(stockSymbol1);
        var result2 = await _cacheService.GetCachedAnalysisAsync(stockSymbol2);

        Assert.IsNull(result1);
        Assert.IsNotNull(result2);
    }


    [TestMethod]
    public async Task CacheAnalysisAsync_UpdatesExistingCache_WhenSameKeyUsed()
    {
        // Arrange
        var stockSymbol = "AAPL";
        var originalResult = CreateTestAnalysisResult(stockSymbol);
        originalResult.TargetPrice = "150-160美元";
        var updatedResult = CreateTestAnalysisResult(stockSymbol);
        updatedResult.TargetPrice = "180-200美元";

        // Act
        await _cacheService.CacheAnalysisAsync(stockSymbol, originalResult);
        await _cacheService.CacheAnalysisAsync(stockSymbol, updatedResult);

        // Assert
        var result = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNotNull(result);
        Assert.AreEqual("180-200美元", result.TargetPrice);
    }

    [TestMethod]
    public async Task CacheService_HandlesMultipleStocks_Correctly()
    {
        // Arrange
        var stockSymbols = new[] { "AAPL", "GOOGL", "MSFT", "TSLA" };
        var analysisResults = stockSymbols.Select(symbol => CreateTestAnalysisResult(symbol)).ToArray();

        // Act
        for (int i = 0; i < stockSymbols.Length; i++)
        {
            await _cacheService.CacheAnalysisAsync(stockSymbols[i], analysisResults[i]);
        }

        // Assert
        for (int i = 0; i < stockSymbols.Length; i++)
        {
            var result = await _cacheService.GetCachedAnalysisAsync(stockSymbols[i]);
            Assert.IsNotNull(result, $"股票 {stockSymbols[i]} 的缓存应该存在");
            Assert.AreEqual(stockSymbols[i], result.StockSymbol);
        }
    }


    #region Helper Methods

    private AnalystResult CreateTestAnalysisResult(string stockSymbol = "AAPL")
    {
        return new AnalystResult
        {
            StockSymbol = stockSymbol,
            TargetPrice = "180-200美元",
            PriceChange = "上涨15-25%",
            Rating = "买入",
            InvestmentRating = "买入",
            RiskLevel = "中等",
            OverallScore = 8.5f,
            ConfidencePercentage = 85f,
            ConsensusInfo = "分析师普遍看好该股票",
            DisagreementInfo = "对短期波动存在分歧",
            DimensionScores = new Dictionary<string, float>
            {
                { "基本面", 8.0f },
                { "技术面", 7.5f },
                { "市场情绪", 9.0f }
            },
            InvestmentHighlights = new List<string>
            {
                "业绩稳定增长",
                "市场地位领先",
                "创新能力强"
            },
            RiskFactors = new List<string>
            {
                "宏观经济风险",
                "行业竞争加剧"
            },
            OperationSuggestions = new List<string>
            {
                "建议分批买入",
                "设置止损位"
            },
            AnalysisData = new List<AnalysisDataItem>
            {
                new AnalysisDataItem
                {
                    DataType = "TechnicalIndicator",
                    Name = "RSI",
                    Value = "65",
                    Unit = "",
                    Signal = "中性",
                    Impact = "中等",
                    Strategy = "观察突破"
                }
            }
        };
    }

    #endregion
}