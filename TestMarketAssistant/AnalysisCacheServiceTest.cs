using MarketAssistant.Applications.Cache;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
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
        
        _cacheService = new AnalysisCacheService(_mockLogger.Object, _memoryCache, TimeSpan.FromMinutes(30));
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
        var chatHistory = CreateTestChatHistory();

        // Act
        await _cacheService.CacheAnalysisAsync(stockSymbol, chatHistory);

        // Assert
        var cachedResult = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNotNull(cachedResult);
        Assert.AreEqual(2, cachedResult.Count);
        Assert.AreEqual("分析AAPL股票", cachedResult.First().Content);
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
        var chatHistory = CreateTestChatHistory();
        
        // 使用很短的过期时间
        using var shortMemoryCache = new MemoryCache(new MemoryCacheOptions());
        using var shortCacheService = new AnalysisCacheService(_mockLogger.Object, shortMemoryCache, TimeSpan.FromMilliseconds(100));

        // Act
        await shortCacheService.CacheAnalysisAsync(stockSymbol, chatHistory);
        
        // 等待缓存过期
        await Task.Delay(200);
        
        var result = await shortCacheService.GetCachedAnalysisAsync(stockSymbol);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task CacheAnalysisAsync_OverwritesExistingCache_WhenSameStockSymbol()
    {
        // Arrange
        var stockSymbol = "AAPL";
        var firstHistory = CreateTestChatHistory("第一次分析");
        var secondHistory = CreateTestChatHistory("第二次分析");

        // Act
        await _cacheService.CacheAnalysisAsync(stockSymbol, firstHistory);
        await _cacheService.CacheAnalysisAsync(stockSymbol, secondHistory);

        // Assert
        var cachedResult = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNotNull(cachedResult);
        Assert.AreEqual("第二次分析", cachedResult.First().Content);
    }

    [TestMethod]
    public async Task ClearCacheAsync_WithSpecificStock_RemovesOnlyThatStock()
    {
        // Arrange
        var stockSymbol1 = "AAPL";
        var stockSymbol2 = "MSFT";
        var chatHistory1 = CreateTestChatHistory("AAPL分析");
        var chatHistory2 = CreateTestChatHistory("MSFT分析");

        await _cacheService.CacheAnalysisAsync(stockSymbol1, chatHistory1);
        await _cacheService.CacheAnalysisAsync(stockSymbol2, chatHistory2);

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
        var originalHistory = CreateTestChatHistory("原始分析");
        var updatedHistory = CreateTestChatHistory("更新分析");

        // Act
        await _cacheService.CacheAnalysisAsync(stockSymbol, originalHistory);
        await _cacheService.CacheAnalysisAsync(stockSymbol, updatedHistory);

        // Assert
        var result = await _cacheService.GetCachedAnalysisAsync(stockSymbol);
        Assert.IsNotNull(result);
        Assert.AreEqual("更新分析", result.First().Content);
    }

    [TestMethod]
    public async Task CacheService_HandlesMultipleStocks_Correctly()
    {
        // Arrange
        var stockSymbols = new[] { "AAPL", "GOOGL", "MSFT", "TSLA" };
        var histories = stockSymbols.Select(symbol => CreateTestChatHistory($"{symbol}分析")).ToArray();

        // Act
        for (int i = 0; i < stockSymbols.Length; i++)
        {
            await _cacheService.CacheAnalysisAsync(stockSymbols[i], histories[i]);
        }

        // Assert
        for (int i = 0; i < stockSymbols.Length; i++)
        {
            var result = await _cacheService.GetCachedAnalysisAsync(stockSymbols[i]);
            Assert.IsNotNull(result, $"股票 {stockSymbols[i]} 的缓存应该存在");
            Assert.AreEqual($"{stockSymbols[i]}分析", result.First().Content);
        }
    }


    #region Helper Methods

    private ChatHistory CreateTestChatHistory(string content = "分析AAPL股票")
    {
        var chatHistory = new ChatHistory();
        chatHistory.AddUserMessage(content);
        chatHistory.AddAssistantMessage("根据分析，AAPL是一只优质股票，建议买入。");
        return chatHistory;
    }

    #endregion
}