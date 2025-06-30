using MarketAssistant.Applications.Stocks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TestMarketAssistant;

[TestClass]
public class StockKLineServiceTest
{
    private StockKLineService _stockKLineService = null!;

    [TestInitialize]
    public void Initialize()
    {
        // 使用NullLogger作为测试环境的日志记录器
        _stockKLineService = new StockKLineService(NullLogger<StockKLineService>.Instance);
    }

    [TestMethod]
    public async Task GetDailyKLineDataAsync_ValidSymbol_ReturnsCorrectData()
    {
        // Arrange
        string symbol = "600000";
        string expectedTsCode = "600000.SH";

        // Act
        var result = await _stockKLineService.GetDailyKLineDataAsync(symbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(symbol, result.Symbol);
        Assert.AreEqual(expectedTsCode, result.Name);
        Assert.AreEqual("daily", result.Interval);
        Assert.IsTrue(result.Data.Count > 0);
    }

    [TestMethod]
    public async Task GetWeeklyKLineDataAsync_ValidSymbol_ReturnsCorrectData()
    {
        // Arrange
        string symbol = "000001";
        string expectedTsCode = "000001.SZ";

        // Act
        var result = await _stockKLineService.GetWeeklyKLineDataAsync(symbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(symbol, result.Symbol);
        Assert.AreEqual(expectedTsCode, result.Name);
        Assert.AreEqual("weekly", result.Interval);
        Assert.IsTrue(result.Data.Count > 0);
    }

    [TestMethod]
    public async Task GetMonthlyKLineDataAsync_ValidSymbol_ReturnsCorrectData()
    {
        // Arrange
        string symbol = "sh601398";
        string expectedTsCode = "601398.SH";

        // Act
        var result = await _stockKLineService.GetMonthlyKLineDataAsync(symbol);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(symbol, result.Symbol);
        Assert.AreEqual(expectedTsCode, result.Name);
        Assert.AreEqual("monthly", result.Interval);
        Assert.IsTrue(result.Data.Count > 0);
    }

    [TestMethod]
    public async Task GetMinuteKLineDataAsync_ValidSymbol_ReturnsCorrectData()
    {
        // Arrange
        string symbol = "600000";
        string expectedTsCode = "600000.SH";
        string freq = "5min";

        // Act
        var result = await _stockKLineService.GetMinuteKLineDataAsync(symbol, freq);

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(symbol, result.Symbol);
        Assert.AreEqual(expectedTsCode, result.Name);
        Assert.AreEqual(freq, result.Interval);
        Assert.IsTrue(result.Data.Count > 0);
    }
}