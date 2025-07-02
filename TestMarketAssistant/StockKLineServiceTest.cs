using MarketAssistant.Applications.Settings;
using MarketAssistant.Applications.Stocks;
using MarketAssistant.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace TestMarketAssistant;

[TestClass]
public class StockKLineServiceTest
{
    private StockKLineService _stockKLineService = null!;
    private Mock<IUserSettingService> _mockUserSettingService = null!;

    [TestInitialize]
    public void Initialize()
    {
        var tushareApiToken = Environment.GetEnvironmentVariable("TUSHARE_API_TOKEN") ?? throw new InvalidOperationException("TUSHARE_API_TOKEN environment variable is not set");

        // 创建模拟的用户设置服务
        _mockUserSettingService = new Mock<IUserSettingService>();
        var testUserSetting = new UserSetting
        {
            TushareApiToken = tushareApiToken
        };
        _mockUserSettingService.Setup(x => x.CurrentSetting).Returns(testUserSetting);

        // 使用NullLogger和模拟的用户设置服务创建StockKLineService实例
        _stockKLineService = new StockKLineService(
            NullLogger<StockKLineService>.Instance,
            _mockUserSettingService.Object);
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